using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using PEPacker;
using SharpTS.Compilation;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.Compilation;

/// <summary>
/// Pins what the <c>--ref-asm</c> post-pass — <c>PEPacker.AssemblyReferenceRewriter</c> — must carry
/// across unchanged. The rewriter rebuilds the whole metadata image, so anything it does not know how
/// to copy vanishes from the output silently: the assembly still loads and still runs, which is
/// exactly why the loss needs an assertion rather than a smoke test.
/// </summary>
/// <remarks>
/// <para>These are guards against a dependency regression, not against SharpTS code. Both were live
/// defects in NickNa.PEPacker 1.0.2 and were fixed in 1.0.3:</para>
/// <list type="bullet">
/// <item>the <c>Property</c>/<c>PropertyMap</c>/<c>MethodSemantics</c> tables were dropped entirely,
/// which strips the property identity off generator state machines (<c>Current</c>), auto-accessors
/// and <c>$IHasFields.Fields</c> — invisible to interface dispatch, visible to any .NET consumer
/// reflecting over a compiled TypeScript assembly;</item>
/// <item>the PE header was rebuilt with <c>PEHeaderBuilder.CreateExecutableHeader()</c> regardless of
/// the source, so a rewritten library came back without <c>Characteristics.Dll</c>.</item>
/// </list>
/// <para>Row counts are compared against the pre-rewrite image rather than pinned to literals, so the
/// test tracks whatever the compiler happens to emit for the fixture below.</para>
/// </remarks>
public class ReferenceRewriteFidelityTests
{
    /// <summary>
    /// Shaped to populate the tables at issue: <c>accessor</c> and a getter give the class real
    /// property rows, and the generator plus async generator each contribute a state machine with a
    /// <c>Current</c> property and a <c>switch</c>-dispatched <c>MoveNext</c>.
    /// </summary>
    private const string Source = """
        class Counter {
            accessor total: number = 0;
            #step: number;
            constructor(step: number = 2) { this.#step = step; }
            get step(): number { return this.#step; }
            bump(): void { this.total = this.total + this.#step; }
        }

        function* seq(n: number) {
            for (let i = 0; i < n; i++) yield i;
        }

        async function* aseq(n: number) {
            for (let i = 0; i < n; i++) yield i * 10;
        }

        async function main() {
            const c = new Counter();
            c.bump();
            let sum = 0;
            for (const v of seq(3)) sum += v;
            for await (const v of aseq(3)) sum += v;
            console.log(c.total, c.step, sum);
        }

        main();
        """;

    /// <summary>
    /// Every metadata table SharpTS's own output populates must come through the rewrite with the
    /// same number of rows. <c>Property</c> and <c>MethodSemantics</c> are the ones that regressed;
    /// the rest are here so a future table loss is caught by the same test.
    /// </summary>
    [Fact]
    public void RewriteKeepsEveryMetadataTableRowCount()
    {
        byte[] before = Compile();
        byte[] after = RewriteReferences(before);

        TableIndex[] tables =
        [
            TableIndex.TypeDef, TableIndex.MethodDef, TableIndex.Field, TableIndex.Param,
            TableIndex.Property, TableIndex.PropertyMap, TableIndex.Event, TableIndex.EventMap,
            TableIndex.MethodSemantics, TableIndex.MethodImpl, TableIndex.InterfaceImpl,
            TableIndex.NestedClass, TableIndex.GenericParam, TableIndex.GenericParamConstraint,
            TableIndex.Constant, TableIndex.StandAloneSig, TableIndex.CustomAttribute,
        ];

        var expected = RowCounts(before, tables);
        var actual = RowCounts(after, tables);

        // Asserted as whole dictionaries so a failure names every table that moved at once.
        Assert.Equal(expected, actual);

        // Guards the guard: a fixture that stopped emitting properties would let the regression
        // through while every count still matched at zero.
        Assert.True(expected[TableIndex.Property] > 0, "fixture emitted no properties");
        Assert.True(expected[TableIndex.MethodSemantics] > 0, "fixture emitted no method semantics");
    }

    /// <summary>
    /// A rewritten library must still describe itself as a library. The runtime tolerates a missing
    /// <c>Dll</c> bit, but <c>Assembly.LoadFrom</c> on some hosts and most PE tooling do not.
    /// </summary>
    [Fact]
    public void RewritePreservesLibraryPeCharacteristics()
    {
        byte[] before = Compile();
        byte[] after = RewriteReferences(before);

        using var source = new PEReader(new MemoryStream(before, writable: false));
        using var rewritten = new PEReader(new MemoryStream(after, writable: false));

        Assert.True(source.PEHeaders.CoffHeader.Characteristics.HasFlag(Characteristics.Dll));
        Assert.Equal(
            source.PEHeaders.CoffHeader.Characteristics,
            rewritten.PEHeaders.CoffHeader.Characteristics);
        Assert.Equal(source.PEHeaders.CoffHeader.Machine, rewritten.PEHeaders.CoffHeader.Machine);
        Assert.Equal(source.PEHeaders.PEHeader!.Subsystem, rewritten.PEHeaders.PEHeader!.Subsystem);
    }

    /// <summary>
    /// The property rows have to keep naming the same members, not merely arrive in the same number,
    /// and each accessor must still be reachable through <c>MethodSemantics</c>.
    /// </summary>
    [Fact]
    public void RewriteKeepsPropertyNamesAndAccessorLinks()
    {
        byte[] before = Compile();
        byte[] after = RewriteReferences(before);

        Assert.Equal(PropertyAccessors(before), PropertyAccessors(after));
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// Every <c>Type::Property</c> in the image mapped to the names of its accessor methods, which
    /// only resolve when the <c>MethodSemantics</c> rows survived and still point at the right rows.
    /// </summary>
    private static SortedDictionary<string, string> PropertyAccessors(byte[] image)
    {
        using var peReader = new PEReader(new MemoryStream(image, writable: false));
        var reader = peReader.GetMetadataReader();
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(typeHandle);
            foreach (var propertyHandle in type.GetProperties())
            {
                var property = reader.GetPropertyDefinition(propertyHandle);
                var accessors = property.GetAccessors();

                string Name(MethodDefinitionHandle handle) => handle.IsNil
                    ? "-"
                    : reader.GetString(reader.GetMethodDefinition(handle).Name);

                result[$"{reader.GetString(type.Name)}::{reader.GetString(property.Name)}"] =
                    $"{Name(accessors.Getter)}/{Name(accessors.Setter)}";
            }
        }

        return result;
    }

    /// <summary>
    /// Sorted so the two sides compare in a fixed order regardless of how the table list is written.
    /// </summary>
    private static SortedDictionary<TableIndex, int> RowCounts(byte[] image, TableIndex[] tables)
    {
        using var peReader = new PEReader(new MemoryStream(image, writable: false));
        var reader = peReader.GetMetadataReader();
        return new SortedDictionary<TableIndex, int>(tables.ToDictionary(t => t, reader.GetTableRowCount));
    }

    /// <summary>
    /// Compiles <see cref="Source"/> to a library image without the post-pass, so the rewrite can be
    /// run against it explicitly and the two images compared.
    /// </summary>
    private static byte[] Compile()
    {
        var document = new SourceDocument(Path.Combine(Path.GetTempPath(), "fidelity.ts"), Source);
        var statements = new Parser(new Lexer(Source).ScanTokens())
            .WithSourceDocument(document)
            .ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        var deadCode = new DeadCodeAnalyzer(typeMap).Analyze(statements);

        var compiler = new ILCompiler("fidelity", preserveConstEnums: false);
        compiler.SetSourceDocument(document);
        compiler.Compile(statements, typeMap, deadCode);

        byte[] image = compiler.SaveToBytes();

        // SaveArtifacts also rewrites when the output references SharpTS, which would leave nothing
        // to compare against. The fixture is chosen to avoid that; fail loudly if it stops holding.
        using var peReader = new PEReader(new MemoryStream(image, writable: false));
        var reader = peReader.GetMetadataReader();
        Assert.Contains(
            "System.Private.CoreLib",
            reader.AssemblyReferences.Select(h => reader.GetString(reader.GetAssemblyReference(h).Name)));

        return image;
    }

    private static byte[] RewriteReferences(byte[] image)
    {
        using var source = new MemoryStream(image, writable: false);
        using var rewriter = new AssemblyReferenceRewriter(
            source,
            EmbeddedReferenceAssemblyIndex.Default,
            ReferencePolicy.RetargetCoreLibOnly);
        rewriter.Rewrite();

        using var output = new MemoryStream();
        rewriter.Save(output);
        return output.ToArray();
    }
}

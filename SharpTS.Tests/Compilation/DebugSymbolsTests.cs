using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using PEPacker;
using SharpTS.Compilation;
using SharpTS.Compilation.Symbols;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.Compilation;

/// <summary>
/// Covers the compile-time symbol pipeline: a portable PDB whose documents, sequence points and
/// CodeView identity still describe the <i>final</i> assembly after
/// <c>PEPacker.AssemblyReferenceRewriter</c> has rebuilt it.
/// </summary>
/// <remarks>
/// The rewriter drops the debug directory and exposes no hook to preserve it, so SharpTS re-attaches
/// symbols to the finished bytes. These tests pin the two properties that makes safe: the rewriter
/// preserves <c>MethodDef</c> row identity, and the injected directory leaves a loadable image.
/// </remarks>
public class DebugSymbolsTests
{
    private const string SourceText = """
        function add(a: number, b: number): number {
          const sum = a + b;
          return sum;
        }
        console.log(add(40, 2));
        """;

    // ---------------------------------------------------------------- pipeline through the rewriter

    /// <summary>
    /// The gate for the whole debugger effort: symbols must survive the real reference rewriter.
    /// </summary>
    [Fact]
    public void SymbolsSurviveTheRealAssemblyReferenceRewriter()
    {
        var fixture = EmitFixtureAssembly();

        // The rewriter is what erases the debug directory; run the genuine one, not a stand-in.
        byte[] rewritten = RewriteReferences(fixture.Image);
        Assert.Empty(ReadDebugDirectory(rewritten));

        PdbEmitter.VerifyMethodMappingPreserved(fixture.Image, rewritten);

        var pdbMetadata = fixture.DebugInfo.BuildPdbMetadata(
            fixture.MethodDefRowCount, PdbEmitter.ReadLocalSignatureRids(rewritten));
        var pdb = PdbEmitter.Serialize(
            pdbMetadata, PdbEmitter.ReadTypeSystemRowCounts(rewritten), default);

        byte[] final = DebugDirectoryInjector.Inject(
            rewritten, pdb.ContentId, pdb.FormatVersion, "Fixture.pdb", pdb.Checksum);

        AssertCodeViewMatchesPdb(final, pdb.Bytes, "Fixture.pdb");
        AssertLoadable(final);

        var reader = ReadPdb(pdb.Bytes);

        // Documents carry the source path and a SHA-256 hash of exactly what was compiled.
        var document = Assert.Single(reader.Documents.Select(reader.GetDocument));
        Assert.Equal(fixture.DocumentPath, reader.GetString(document.Name));
        Assert.Equal(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(SourceText)), reader.GetBlobBytes(document.Hash));

        // MethodDebugInformation is parallel to MethodDef: row N describes method N.
        Assert.Equal(fixture.MethodDefRowCount, reader.MethodDebugInformation.Count);

        // ...which means line information must land on the methods it was recorded against, even
        // though the fixture deliberately interleaves body-less interface methods.
        Assert.Equal([10, 11], SequencePointLines(reader, fixture.WithPointsRid));
        Assert.Equal([30], SequencePointLines(reader, fixture.AlsoWithPointsRid));
        Assert.Empty(SequencePointLines(reader, fixture.NoPointsRid));
        Assert.Empty(SequencePointLines(reader, fixture.AbstractRid));
    }

    /// <summary>
    /// A hidden point marks compiler-generated IL so a debugger steps over it rather than blaming
    /// the previous statement.
    /// </summary>
    [Fact]
    public void HiddenSequencePointsAreEncodedAsHidden()
    {
        var fixture = EmitFixtureAssembly(withHiddenPoint: true);
        var pdbMetadata = fixture.DebugInfo.BuildPdbMetadata(
            fixture.MethodDefRowCount, PdbEmitter.ReadLocalSignatureRids(fixture.Image));
        var pdb = PdbEmitter.Serialize(
            pdbMetadata, PdbEmitter.ReadTypeSystemRowCounts(fixture.Image), default);

        var reader = ReadPdb(pdb.Bytes);
        var points = SequencePoints(reader, fixture.AlsoWithPointsRid);

        Assert.Collection(points,
            p => Assert.True(p.IsHidden),
            p => Assert.Equal(30, p.StartLine));
    }

    /// <summary>
    /// Injection appends to a section rather than moving anything, so the metadata a loader reads
    /// must be untouched.
    /// </summary>
    [Fact]
    public void InjectionPreservesAssemblyMetadata()
    {
        var fixture = EmitFixtureAssembly();
        var pdb = PdbEmitter.Serialize(
            fixture.DebugInfo.BuildPdbMetadata(fixture.MethodDefRowCount, _ => 0),
            PdbEmitter.ReadTypeSystemRowCounts(fixture.Image),
            default);

        byte[] injected = DebugDirectoryInjector.Inject(
            fixture.Image, pdb.ContentId, pdb.FormatVersion, "Fixture.pdb", pdb.Checksum);

        Assert.Equal(DescribeMethods(fixture.Image), DescribeMethods(injected));
        AssertLoadable(injected);
    }

    // ---------------------------------------------------------------- end-to-end through ILCompiler

    [Fact]
    public void CompilingWithDebugSymbolsProducesAMatchingPdb()
    {
        var artifacts = CompileTypeScript(SourceText, emitDebugSymbols: true);

        Assert.NotNull(artifacts.Pdb);
        Assert.Equal("output.pdb", artifacts.PdbFileName);
        AssertCodeViewMatchesPdb(artifacts.Assembly, artifacts.Pdb!, "output.pdb");
        AssertLoadable(artifacts.Assembly);

        // Even with nothing recorded yet, the parallel-table invariant has to hold or every future
        // sequence point would be attributed to the wrong method.
        using var peReader = new PEReader(new MemoryStream(artifacts.Assembly, writable: false));
        Assert.Equal(
            peReader.GetMetadataReader().MethodDefinitions.Count,
            ReadPdb(artifacts.Pdb!).MethodDebugInformation.Count);
    }

    // ---------------------------------------------------------------- statement sequence points

    /// <summary>
    /// Every executable statement gets a point on its own line, which is what makes a breakpoint
    /// bind to TypeScript rather than to emitted IL.
    /// </summary>
    [Fact]
    public void EachExecutableStatementGetsAPointOnItsOwnLine()
    {
        var artifacts = CompileTypeScript(SourceText, emitDebugSymbols: true);
        var lines = AllSequencePointLines(artifacts);

        // add()'s two body statements, then the two top-level statements.
        Assert.Equal([2, 3, 5], lines);
    }

    /// <summary>
    /// A block or a <c>try</c> emits no instructions of its own, so the statement inside it must
    /// own the offset they would otherwise share — otherwise stepping lands on a brace.
    /// </summary>
    [Fact]
    public void ContainersDoNotClaimTheirContentsPoint()
    {
        const string source = """
            let n = 0;
            if (n === 0) {
              n = 1;
            }
            try {
              n = 2;
            } catch (e) {
              n = 3;
            }
            """;

        var lines = AllSequencePointLines(CompileTypeScript(source, emitDebugSymbols: true));

        // Line 1 the declaration, 2 the `if` test, 3 its body; `try` (5) yields to its body (6),
        // and the catch body is 8. No point sits on a line that is only a brace.
        Assert.Equal([1, 2, 3, 6, 8], lines);
    }

    /// <summary>
    /// Loops and their bodies stay distinguishable, including the desugared update expression.
    /// </summary>
    [Fact]
    public void LoopHeadersAndBodiesGetSeparatePoints()
    {
        const string source = """
            let total = 0;
            for (let i = 0; i < 3; i++) {
              total = total + i;
            }
            """;

        var lines = AllSequencePointLines(CompileTypeScript(source, emitDebugSymbols: true));

        Assert.Equal([1, 2, 3], lines);
    }

    /// <summary>
    /// Hoisting moves a <c>var</c>'s binding to the top of the body. The synthesized declaration is
    /// marked hidden so stepping passes over it instead of jumping back to the original line.
    /// </summary>
    [Fact]
    public void HoistedVarDeclarationsBecomeHiddenPoints()
    {
        const string source = """
            function f(): number {
              if (true) { var x = 1; }
              return x;
            }
            f();
            """;

        var artifacts = CompileTypeScript(source, emitDebugSymbols: true);
        var points = AllSequencePoints(artifacts);

        Assert.Contains(points, p => p.IsHidden);
        // The user's own statements still resolve to the lines they were written on.
        Assert.Contains(points, p => !p.IsHidden && p.StartLine == 2);
        Assert.Contains(points, p => !p.IsHidden && p.StartLine == 3);
    }

    [Fact]
    public void DebugBuildsAreMarkedDebuggableAndOthersAreNot()
    {
        Assert.True(HasDebuggableAttribute(CompileTypeScript(SourceText, emitDebugSymbols: true).Assembly));
        Assert.False(HasDebuggableAttribute(CompileTypeScript(SourceText, emitDebugSymbols: false).Assembly));
    }

    [Fact]
    public void CompilingWithoutDebugSymbolsEmitsNoDebugDirectory()
    {
        var artifacts = CompileTypeScript(SourceText, emitDebugSymbols: false);

        Assert.Null(artifacts.Pdb);
        Assert.Null(artifacts.PdbFileName);
        Assert.Empty(ReadDebugDirectory(artifacts.Assembly));
    }

    [Fact]
    public void SaveWritesThePdbBesideTheAssembly()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"sharpts_pdb_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string dllPath = Path.Combine(directory, "program.dll");
            CompileTypeScript(SourceText, emitDebugSymbols: true, saveTo: dllPath);

            string pdbPath = Path.Combine(directory, "program.pdb");
            Assert.True(File.Exists(pdbPath), "Save(path) should write symbols beside the assembly.");
            AssertCodeViewMatchesPdb(File.ReadAllBytes(dllPath), File.ReadAllBytes(pdbPath), "program.pdb");
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { /* best effort */ }
        }
    }

    // ---------------------------------------------------------------- helpers

    private static CompilationArtifacts CompileTypeScript(string source, bool emitDebugSymbols, string? saveTo = null)
    {
        var document = new SourceDocument(Path.Combine(Path.GetTempPath(), "program.ts"), source);
        var statements = new Parser(new Lexer(source).ScanTokens())
            .WithSourceDocument(document)
            .ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        var deadCode = new DeadCodeAnalyzer(typeMap).Analyze(statements);

        // useReferenceAssemblies forces the post-processing rewriter, which is the path symbols
        // have to survive.
        var compiler = new ILCompiler(
            saveTo is null ? "output" : Path.GetFileNameWithoutExtension(saveTo),
            preserveConstEnums: false,
            useReferenceAssemblies: true,
            sdkPath: SdkResolver.FindReferenceAssembliesPath())
        {
            EmitDebugSymbols = emitDebugSymbols,
        };
        compiler.SetSourceDocument(document);
        compiler.Compile(statements, typeMap, deadCode);

        if (saveTo is null)
            return compiler.SaveArtifacts(emitDebugSymbols ? "output.pdb" : null);

        compiler.Save(saveTo);
        return new CompilationArtifacts(File.ReadAllBytes(saveTo), null, null);
    }

    /// <summary>
    /// An assembly shaped to catch the misalignment trap: two body-less interface methods come
    /// before the methods that carry line information, so any off-by-N in the
    /// <c>MethodDebugInformation</c> table shows up as points on the wrong method.
    /// </summary>
    private static Fixture EmitFixtureAssembly(bool withHiddenPoint = false)
    {
        var builder = new PersistedAssemblyBuilder(new AssemblyName("Fixture"), typeof(object).Assembly);
        var module = builder.DefineDynamicModule("Fixture");

        var iface = module.DefineType("IShape",
            TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);
        var abstractMethod = iface.DefineMethod("Area",
            MethodAttributes.Public | MethodAttributes.Abstract | MethodAttributes.Virtual, typeof(double), Type.EmptyTypes);
        iface.DefineMethod("Perimeter",
            MethodAttributes.Public | MethodAttributes.Abstract | MethodAttributes.Virtual, typeof(double), Type.EmptyTypes);
        iface.CreateType();

        var type = module.DefineType("Program", TypeAttributes.Public);
        var withPoints = DefineReturningMethod(type, "WithPoints", out var withPointsIl);
        var noPoints = DefineReturningMethod(type, "NoPoints", out _);
        var alsoWithPoints = DefineReturningMethod(type, "AlsoWithPoints", out var alsoIl);
        type.CreateType();

        var debugInfo = new DebugInfoCollector();
        string documentPath = Path.Combine(Path.GetTempPath(), "fixture.ts");
        var document = debugInfo.AddDocument(documentPath, SourceText);
        debugInfo.RecordSequencePoint(withPoints, document, 0, 10, 1, 10, 20);
        debugInfo.RecordSequencePoint(withPoints, document, 1, 11, 1, 11, 20);
        if (withHiddenPoint) debugInfo.RecordHiddenSequencePoint(alsoWithPoints, 0);
        debugInfo.RecordSequencePoint(alsoWithPoints, document, withHiddenPoint ? 1 : 0, 30, 3, 30, 25);

        var metadata = builder.GenerateMetadata(out var ilStream, out var fieldData);
        var pe = new ManagedPEBuilder(
            header: PEHeaderBuilder.CreateLibraryHeader(),
            metadataRootBuilder: new MetadataRootBuilder(metadata),
            ilStream: ilStream,
            mappedFieldData: fieldData);

        var blob = new BlobBuilder();
        pe.Serialize(blob);

        return new Fixture(
            blob.ToArray(),
            debugInfo,
            documentPath,
            metadata.GetRowCounts()[(int)TableIndex.MethodDef],
            RowOf(withPoints), RowOf(noPoints), RowOf(alsoWithPoints), RowOf(abstractMethod));

        static MethodBuilder DefineReturningMethod(TypeBuilder type, string name, out ILGenerator il)
        {
            var method = type.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
            il = method.GetILGenerator();
            il.Emit(OpCodes.Nop);
            il.Emit(OpCodes.Ret);
            return method;
        }

        static int RowOf(MethodInfo method) =>
            MetadataTokens.GetRowNumber(MetadataTokens.MethodDefinitionHandle(method.MetadataToken));
    }

    private sealed record Fixture(
        byte[] Image,
        DebugInfoCollector DebugInfo,
        string DocumentPath,
        int MethodDefRowCount,
        int WithPointsRid,
        int NoPointsRid,
        int AlsoWithPointsRid,
        int AbstractRid);

    private static byte[] RewriteReferences(byte[] image)
    {
        string referenceAssemblies = SdkResolver.FindReferenceAssembliesPath()
            ?? throw new InvalidOperationException("SDK reference assemblies are required for this test.");

        using var source = new MemoryStream(image, writable: false);
        using var rewriter = new AssemblyReferenceRewriter(source, referenceAssemblies);
        rewriter.Rewrite();

        using var output = new MemoryStream();
        rewriter.Save(output);
        return output.ToArray();
    }

    private static ImmutableArray<DebugDirectoryEntry> ReadDebugDirectory(byte[] image)
    {
        using var reader = new PEReader(new MemoryStream(image, writable: false));
        return reader.ReadDebugDirectory();
    }

    private static MetadataReader ReadPdb(byte[] pdb) =>
        MetadataReaderProvider.FromPortablePdbStream(new MemoryStream(pdb, writable: false)).GetMetadataReader();

    private static void AssertCodeViewMatchesPdb(byte[] image, byte[] pdb, string expectedPdbName)
    {
        using var reader = new PEReader(new MemoryStream(image, writable: false));
        var entries = reader.ReadDebugDirectory();

        var codeView = Assert.Single(entries, e => e.Type == DebugDirectoryEntryType.CodeView);
        var data = reader.ReadCodeViewDebugDirectoryData(codeView);
        Assert.Equal(expectedPdbName, Path.GetFileName(data.Path));
        Assert.Equal(1, data.Age);

        var header = ReadPdb(pdb).DebugMetadataHeader!;
        Assert.Equal(data.Guid, new Guid(header.Id.Take(16).ToArray()));

        var checksum = Assert.Single(entries, e => e.Type == DebugDirectoryEntryType.PdbChecksum);
        var checksumData = reader.ReadPdbChecksumDebugDirectoryData(checksum);
        Assert.Equal("SHA256", checksumData.AlgorithmName);
        Assert.Equal(SHA256.HashData(pdb), checksumData.Checksum);
    }

    private static void AssertLoadable(byte[] image)
    {
        using var reader = new PEReader(new MemoryStream(image, writable: false));
        Assert.True(reader.HasMetadata, "Injected image lost its CLI metadata.");
        Assert.NotEmpty(reader.GetMetadataReader().MethodDefinitions);
    }

    private static string[] DescribeMethods(byte[] image)
    {
        using var reader = new PEReader(new MemoryStream(image, writable: false));
        var metadata = reader.GetMetadataReader();
        return metadata.MethodDefinitions
            .Select(handle =>
            {
                var method = metadata.GetMethodDefinition(handle);
                var declaringType = metadata.GetTypeDefinition(method.GetDeclaringType());
                return $"{metadata.GetString(declaringType.Name)}::{metadata.GetString(method.Name)}";
            })
            .ToArray();
    }

    /// <summary>Every sequence point in the artifacts' PDB, in method then offset order.</summary>
    private static List<SequencePoint> AllSequencePoints(CompilationArtifacts artifacts)
    {
        var pdb = ReadPdb(artifacts.Pdb!);
        var points = new List<SequencePoint>();

        foreach (var handle in pdb.MethodDebugInformation)
        {
            var info = pdb.GetMethodDebugInformation(handle);
            if (info.SequencePointsBlob.IsNil) continue;
            points.AddRange(info.GetSequencePoints());
        }
        return points;
    }

    /// <summary>The distinct source lines a debugger could stop on, ascending.</summary>
    private static int[] AllSequencePointLines(CompilationArtifacts artifacts) =>
        AllSequencePoints(artifacts)
            .Where(p => !p.IsHidden)
            .Select(p => p.StartLine)
            .Distinct()
            .Order()
            .ToArray();

    private static bool HasDebuggableAttribute(byte[] image)
    {
        using var reader = new PEReader(new MemoryStream(image, writable: false));
        var metadata = reader.GetMetadataReader();

        foreach (var handle in metadata.GetAssemblyDefinition().GetCustomAttributes())
        {
            var attribute = metadata.GetCustomAttribute(handle);
            if (attribute.Constructor.Kind != HandleKind.MemberReference) continue;

            var constructor = metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
            if (constructor.Parent.Kind != HandleKind.TypeReference) continue;

            var type = metadata.GetTypeReference((TypeReferenceHandle)constructor.Parent);
            if (metadata.GetString(type.Name) == "DebuggableAttribute") return true;
        }
        return false;
    }

    private static List<SequencePoint> SequencePoints(MetadataReader pdb, int methodRid)
    {
        var handle = MetadataTokens.MethodDebugInformationHandle(methodRid);
        var info = pdb.GetMethodDebugInformation(handle);
        return info.SequencePointsBlob.IsNil ? [] : info.GetSequencePoints().ToList();
    }

    private static int[] SequencePointLines(MetadataReader pdb, int methodRid) =>
        SequencePoints(pdb, methodRid).Select(p => p.StartLine).ToArray();
}

using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// Pins the resolution precedence in <c>ILEmitter.ResolveTypeArg</c>: a generic type argument
/// naming one of the program's own TypeScript classes must resolve to that class, never to a
/// same-named .NET type discovered by scanning loaded assemblies.
///
/// The scan is a legitimate last resort for fully-qualified CLR names in interop code, but
/// compiled TypeScript classes are public types in the global namespace under their bare name,
/// so running it any earlier lets an unrelated loaded assembly capture the type argument. The
/// resulting assembly reference is unresolvable when the compiled program is loaded from bytes
/// (as the in-process test harness does), surfacing as
/// <c>FileNotFoundException: Could not load file or assembly 'test_&lt;guid&gt;'</c> thrown from
/// <c>$Program.Main</c> — and, being dependent on which sibling assemblies happen to be loaded,
/// it presents as an intermittent failure rather than a deterministic one.
///
/// <see cref="SharpTsAmbientProbe"/> is the collision bait: a global-namespace type in this
/// already-loaded test assembly whose name the program below also declares.
/// </summary>
public class AmbientTypeArgumentPrecedenceTests
{
    private const string Source = """
        class SharpTsAmbientProbe {
            value: number;
            constructor(v: number) {
                this.value = v;
            }
            get(): number {
                return this.value;
            }
        }
        class Box<T extends SharpTsAmbientProbe> {
            item: T;
            constructor(item: T) {
                this.item = item;
            }
            unwrap(): number {
                return this.item.get();
            }
        }
        const box: Box<SharpTsAmbientProbe> = new Box<SharpTsAmbientProbe>(new SharpTsAmbientProbe(7));
        console.log(box.unwrap());
        """;

    /// <summary>
    /// The emitted metadata must not reference this test assembly. Asserting on the assembly
    /// reference table catches the mis-resolution directly, independently of whether the stray
    /// reference happens to be loadable at runtime.
    /// </summary>
    [Fact]
    public void TypeArgumentNamingUserClass_DoesNotReferenceAmbientAssembly()
    {
        var tempDir = Path.Combine(
            Path.GetTempPath(), $"sharpts_ambient_typearg_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        var dllPath = Path.Combine(tempDir, "ambient_typearg_test.dll");

        try
        {
            var statements = new Parser(new Lexer(Source).ScanTokens()).ParseOrThrow();
            var typeMap = new TypeChecker().Check(statements);
            var deadCodeInfo = new DeadCodeAnalyzer(typeMap).Analyze(statements);

            var compiler = new ILCompiler("ambient_typearg_test");
            compiler.Compile(statements, typeMap, deadCodeInfo);
            compiler.Save(dllPath);

            using var stream = File.OpenRead(dllPath);
            using var peReader = new PEReader(stream);
            var metadata = peReader.GetMetadataReader();
            var references = metadata.AssemblyReferences
                .Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name))
                .ToList();

            var ambientAssembly = typeof(SharpTsAmbientProbe).Assembly.GetName().Name!;
            Assert.False(
                references.Contains(ambientAssembly),
                "Generic type argument 'SharpTsAmbientProbe' resolved to the ambient .NET type "
                    + "instead of the program's own class. Emitted references: "
                    + string.Join(", ", references));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    /// <summary>
    /// The user-visible half: the program must still run and see its own class.
    /// </summary>
    [Theory, ModeData]
    public void TypeArgumentNamingUserClass_RunsAgainstUserClass(ExecutionMode mode)
    {
        Assert.Equal("7\n", TestHarness.Run(Source, mode));
    }
}

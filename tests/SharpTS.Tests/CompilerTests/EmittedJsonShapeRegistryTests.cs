using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Diagnostics;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedJsonShapeRegistryTests
{
    [Fact]
    public void RegistryPreservesOrdinalIdentityInsertionNamesAndProgramOwnership()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("shape_declarations"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var program = module.DefineType("$Program");
        var other = module.DefineType("Other");
        var owner = new EmittedJsonShapeRegistry();
        var view = owner.Fields;
        Assert.Throws<NotSupportedException>(() => ((IDictionary)view).Clear());
        var first = owner.GetOrDefine("shape", program, typeof(object));
        Assert.Same(first, owner.GetOrDefine(new string("shape".ToCharArray()), program, typeof(object)));
        var second = owner.GetOrDefine("Shape", program, typeof(object));
        Assert.Equal(2, view.Count);
        Assert.Equal("$jsonShape_0", first.Name);
        Assert.Equal("$jsonShape_1", second.Name);
        Assert.Equal(FieldAttributes.Assembly | FieldAttributes.Static, first.Attributes);
        Assert.Throws<InvalidOperationException>(() => owner.GetOrDefine("shape", other, typeof(object)));
        Assert.Equal(2, view.Count);
        owner.CompleteEmission();
        Assert.True(owner.IsComplete);
        Assert.Throws<InvalidOperationException>(() => owner.GetOrDefine("shape", program, typeof(object)));
        Assert.Throws<InvalidOperationException>(() => owner.GetOrDefine("later", program, typeof(object)));
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
        Assert.Throws<NotSupportedException>(() => ((IDictionary)view).Clear());
        Assert.Equal(2, program.CreateType()!.GetFields(BindingFlags.NonPublic | BindingFlags.Static).Length);
        Assert.Empty(other.CreateType()!.GetFields(BindingFlags.NonPublic | BindingFlags.Static));
    }

    [Fact]
    public void EmptyRegistryCanCompleteWithoutBorrowingAnotherCompilation()
    {
        var first = new EmittedRuntime().JsonShapes;
        var second = new EmittedRuntime().JsonShapes;
        first.CompleteEmission();
        Assert.True(first.IsComplete);
        Assert.False(second.IsComplete);
        Assert.Empty(first.Fields);
        Assert.Empty(second.Fields);
    }

    public static IEnumerable<object[]> CompilationPaths => Enumerable.Range(0, 8)
        .Select(mask => new object[] {(mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0});

    [Theory]
    [MemberData(nameof(CompilationPaths))]
    public void LateFieldsFinishAfterGuestEmissionAcrossCompilationPaths(bool modules, bool timed, bool hosted)
    {
        const string definitions = """
            type Item = {x:number; name:string};
            type Group = {items:Item[]};
            function make(x:number): Group {return {items:[{x:x,name:"n"}]};}
            """;
        const string calls = "console.log(JSON.stringify(make(7))); console.log(JSON.stringify(make(8)));";
        var compiler = new ILCompiler($"shape_lifetime_{Guid.NewGuid():N}");
        if (timed) compiler.SetTimingCollector(new ExecutionTimingCollector());
        if (hosted) compiler.EnableHostedOutput();
        string? directory = null;
        try
        {
            if (modules)
            {
                directory = Path.Combine(Path.GetTempPath(), $"shape_lifetime_{Guid.NewGuid():N}");
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "record.ts"), definitions.Replace("function make", "export function make"));
                var entry = Path.Combine(directory, "main.ts");
                File.WriteAllText(entry, "import {make} from './record'; " + calls);
                var resolver = new ModuleResolver(entry);
                var loaded = resolver.LoadModule(entry);
                var ordered = resolver.GetModulesInOrder(loaded);
                compiler.CompileModules(ordered, resolver, TestHarness.CheckModulesOrThrow(new TypeChecker(), ordered, resolver));
            }
            else
            {
                var statements = new Parser(new Lexer(definitions + calls).ScanTokens()).ParseOrThrow();
                compiler.Compile(statements, new TypeChecker().Check(statements));
            }
            var runtime = (EmittedRuntime)typeof(ILCompiler).GetField("_runtime", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(compiler)!;
            Assert.True(runtime.Records.IsComplete);
            Assert.True(runtime.JsonShapes.IsComplete);
            var fields = runtime.JsonShapes.Fields.Values.ToArray();
            Assert.Equal(3, fields.Length);
            Assert.Equal(Enumerable.Range(0, 3).Select(index => "$jsonShape_" + index), fields.Select(field => field.Name));
            Assert.All(fields, field =>
            {
                Assert.Equal("$Program", field.DeclaringType!.Name);
                Assert.Equal(FieldAttributes.Assembly | FieldAttributes.Static, field.Attributes);
            });
            var programBuilder = (TypeBuilder)fields[0].DeclaringType!;
            Assert.Throws<InvalidOperationException>(() => runtime.JsonShapes.GetOrDefine("late", programBuilder, typeof(object)));
            var bytes = compiler.SaveToBytes();
            using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
            Assert.Empty(verifier.Verify(new MemoryStream(bytes)));
            var assembly = Assembly.Load(bytes);
            var references = assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();
            Assert.DoesNotContain("SharpTS", references);
            Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
            var program = assembly.GetType("$Program")!;
            var saved = fields.Select(field => program.GetField(field.Name, BindingFlags.Static | BindingFlags.NonPublic)!).ToArray();
            Assert.All(saved, field => Assert.Null(field.GetValue(null)));
            if (!hosted)
            {
                AsyncLocalConsoleRedirector.Install();
                using var capture = AsyncLocalConsoleRedirector.Capture();
                var previous = SynchronizationContext.Current;
                try {program.GetMethod("Main")!.Invoke(null, null);}
                finally {SynchronizationContext.SetSynchronizationContext(previous);}
                Assert.Equal("{\"items\":[{\"x\":7,\"name\":\"n\"}]}\n{\"items\":[{\"x\":8,\"name\":\"n\"}]}\n", capture.GetOutput().Replace("\r\n", "\n"));
                Assert.All(saved, field => Assert.NotNull(field.GetValue(null)));
            }
            // Completing compiler metadata must not freeze the generated guest fields.
            var replacement = new object();
            saved[0].SetValue(null, replacement);
            Assert.Same(replacement, saved[0].GetValue(null));
        }
        finally
        {
            if (directory is not null) Directory.Delete(directory, recursive: true);
        }
    }
}

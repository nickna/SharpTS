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

public sealed class EmittedRegexLiteralCacheRuntimeTests
{
    [Fact]
    public void SelectedSitesUseReferenceIdentityAndMissingFieldsRemainRepairable()
    {
        var owner = new EmittedRuntime().RegexLiteralCache;
        var first = new Expr.RegexLiteral("a", ""); var second = new Expr.RegexLiteral("a", "");
        var escaping = new Expr.RegexLiteral("a", "");
        Assert.Equal(first, second); Assert.NotSame(first, second);
        Assert.Throws<InvalidOperationException>(() => owner.Fields);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
        Assert.Throws<ArgumentNullException>(() => owner.BeginDeclarations(null!));
        Assert.Throws<ArgumentNullException>(() => owner.BeginDeclarations([first, null!]));
        Assert.Throws<InvalidOperationException>(() => owner.BeginDeclarations([first, first]));
        var selected = new List<Expr.RegexLiteral> { first, second };
        owner.BeginDeclarations(selected); selected.Clear();
        Assert.Throws<InvalidOperationException>(() => owner.BeginDeclarations([]));
        var view = owner.Fields;
        Assert.Throws<NotSupportedException>(() => ((IDictionary)view).Clear());
        var builder = NewAssembly(); var type = builder.DefineDynamicModule("main").DefineType("Forward", TypeAttributes.Public);
        var field = type.DefineField("first", typeof(object), FieldAttributes.Public | FieldAttributes.Static);
        Assert.Throws<ArgumentNullException>(() => owner.DeclareField(null!, field));
        Assert.Throws<ArgumentNullException>(() => owner.DeclareField(first, null!));
        Assert.Throws<InvalidOperationException>(() => owner.DeclareField(escaping, field));
        owner.DeclareField(first, field);
        Assert.Throws<InvalidOperationException>(() => owner.DeclareField(first, field));
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission); Assert.False(owner.IsComplete);
        Assert.Same(field, view[first]); Assert.False(view.ContainsKey(second)); Assert.False(view.ContainsKey(escaping));
        // Emit a consumer before completing declarations; the guest field remains writable.
        var method = type.DefineMethod("Store", MethodAttributes.Public | MethodAttributes.Static, typeof(object), [typeof(object)]);
        var il = method.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Stsfld, view[first]);
        il.Emit(OpCodes.Ldsfld, view[first]); il.Emit(OpCodes.Ret);
        owner.DeclareField(second, type.DefineField("second", typeof(object), FieldAttributes.Public | FieldAttributes.Static));
        owner.CompleteEmission(); Assert.True(owner.IsComplete); Assert.Same(view, owner.Fields);
        Assert.Equal(2, view.Count); Assert.NotSame(view[first], view[second]); Assert.False(view.ContainsKey(escaping));
        Assert.Throws<InvalidOperationException>(() => owner.DeclareField(escaping, field));
        Assert.Throws<InvalidOperationException>(() => owner.BeginDeclarations([]));
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
        Assert.Throws<NotSupportedException>(() => ((IDictionary)view).Clear());
        type.CreateType(); using var bytes = new MemoryStream(); builder.Save(bytes);
        var loaded = VerifyLoad(bytes.ToArray()); var store = loaded.GetType("Forward")!.GetMethod("Store")!;
        var one = new object(); var two = new object();
        Assert.Same(one, store.Invoke(null, [one])); Assert.Same(two, store.Invoke(null, [two]));
    }

    [Fact]
    public void EmptySelectionCompletesExplicitlyWithoutBorrowingAnotherCompilation()
    {
        var owner = new EmittedRuntime().RegexLiteralCache;
        var other = new EmittedRuntime().RegexLiteralCache;
        Assert.Throws<InvalidOperationException>(() => owner.DeclareField(new("a", ""), null!));
        owner.BeginDeclarations([]); owner.CompleteEmission();
        Assert.True(owner.IsComplete); Assert.Empty(owner.Fields); Assert.False(other.IsComplete);
        Assert.Throws<InvalidOperationException>(() => other.Fields);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.RegexLiteralCache))!.SetMethod);
        Assert.Null(typeof(EmittedRuntime).GetProperty("RegexHoistFields"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedRuntimeEmitterLeavesFreshRegistriesForProgramDeclaration(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        var owners = new HashSet<EmittedRegexLiteralCacheRuntime>();
        foreach (string source in new[] { "const n=1;", "/a/.test(\"a\");", "const n=2;" })
        {
            var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
            var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
            var runtime = emitter.EmitAll(module, new RuntimeFeatureDetector().Detect(statements));
            var owner = runtime.RegexLiteralCache; Assert.True(owners.Add(owner)); Assert.False(owner.IsComplete);
            Assert.Throws<InvalidOperationException>(() => owner.Fields);
            var sites = RegexLiteralHoistAnalyzer.Analyze(statements); owner.BeginDeclarations(sites);
            var program = module.DefineType("$Program"); int index = 0;
            foreach (var site in sites) owner.DeclareField(site, program.DefineField("$rx_" + index++, typeof(object), FieldAttributes.Public | FieldAttributes.Static));
            owner.CompleteEmission(); program.CreateType();
            using var bytes = new MemoryStream(); builder.Save(bytes); var loaded = VerifyLoad(bytes.ToArray());
            Assert.Equal(sites.Count, loaded.GetType("$Program")!.GetFields().Length);
            Assert.DoesNotContain(loaded.GetReferencedAssemblies(), a => a.Name == "SharpTS");
            Assert.Equal(hosted, loaded.GetReferencedAssemblies().Any(a => a.Name == "SharpTS.Hosting.Abstractions"));
        }
    }

    public static IEnumerable<object[]> CompilationPaths => Enumerable.Range(0, 8)
        .Select(mask => new object[] { (mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0 });

    [Theory]
    [MemberData(nameof(CompilationPaths))]
    public void RegistryCompletesBeforeBodiesAcrossSingleAndModulePaths(bool modules, bool timed, bool hosted)
    {
        const string definitions = "function use(){return /a/.test(\"a\");} function later(){return /b/.test(\"b\");}";
        const string calls = "use(); use();";
        var compiler = new ILCompiler($"regex_cache_{Guid.NewGuid():N}");
        if (timed) compiler.SetTimingCollector(new ExecutionTimingCollector());
        if (hosted) compiler.EnableHostedOutput();
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        if (modules)
        {
            directory.CreateFile("cache.ts", definitions.Replace("function use", "export function use"));
            var entry = directory.CreateFile("main.ts", "import {use} from './cache'; " + calls);
            var resolver = new ModuleResolver(entry); var loaded = resolver.LoadModule(entry);
            var ordered = resolver.GetModulesInOrder(loaded);
            compiler.CompileModules(ordered, resolver, TestHarness.CheckModulesOrThrow(new TypeChecker(), ordered, resolver));
        }
        else
        {
            var statements = new Parser(new Lexer(definitions + calls).ScanTokens()).ParseOrThrow();
            compiler.Compile(statements, new TypeChecker().Check(statements));
        }
        var runtime = (EmittedRuntime)typeof(ILCompiler).GetField("_runtime", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(compiler)!;
        var owner = runtime.RegexLiteralCache; Assert.True(owner.IsComplete);
        var fields = owner.Fields.Values.ToArray(); Assert.Equal(2, fields.Length);
        Assert.Equal(new[] { "$rx_0", "$rx_1" }, fields.Select(f => f.Name));
        Assert.All(fields, field =>
        {
            Assert.Equal("$Program", field.DeclaringType!.Name);
            Assert.Equal(typeof(object), field.FieldType);
            Assert.Equal(FieldAttributes.Public | FieldAttributes.Static, field.Attributes);
        });
        var assembly = VerifyLoad(compiler.SaveToBytes());
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), a => a.Name == "SharpTS");
        Assert.Equal(hosted, assembly.GetReferencedAssemblies().Any(a => a.Name == "SharpTS.Hosting.Abstractions"));
        var program = assembly.GetType("$Program")!;
        var saved = fields.Select(f => program.GetField(f.Name)!).ToArray();
        Assert.All(saved, field => Assert.Null(field.GetValue(null)));
        if (!hosted)
        {
            var previous = SynchronizationContext.Current;
            try { program.GetMethod("Main")!.Invoke(null, null); }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
            Assert.NotNull(saved[0].GetValue(null)); Assert.Null(saved[1].GetValue(null));
        }
        var replacement = new object(); saved[0].SetValue(null, replacement);
        Assert.Same(replacement, saved[0].GetValue(null));
    }

    [Theory]
    [InlineData("const n=1;", 0, false)]
    [InlineData("const r=/a/;", 0, true)]
    [InlineData("/a/.test(\"a\");", 1, true)]
    public void CompilerCompletesEmptyAndNonemptySelectionsIndependentlyOfRegExpAvailability(string source, int count, bool implementation)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var compiler = new ILCompiler($"regex_selection_{Guid.NewGuid():N}");
        compiler.Compile(statements, new TypeChecker().Check(statements));
        var runtime = (EmittedRuntime)typeof(ILCompiler).GetField("_runtime", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(compiler)!;
        Assert.True(runtime.RegexLiteralCache.IsComplete); Assert.Equal(count, runtime.RegexLiteralCache.Fields.Count);
        Assert.Equal(implementation, runtime.RegExps.Implementation is not null);
        VerifyLoad(compiler.SaveToBytes());
    }

    [Fact]
    public void LazyConstructionFailureLeavesTheFieldEmptyForAnotherEvaluation()
    {
        // Exercise the existing lowering contract for an invalid pattern accepted by the parser.
        // No JavaScript source-conformance claim: native construction fails only on evaluation.
        const string source = "function bad(){return /[z-a]/.test(\"a\");}";
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var compiler = new ILCompiler($"regex_failure_{Guid.NewGuid():N}");
        compiler.Compile(statements, new TypeChecker().Check(statements));
        var assembly = VerifyLoad(compiler.SaveToBytes()); var program = assembly.GetType("$Program")!;
        var cache = program.GetField("$rx_0")!; Assert.Null(cache.GetValue(null));
        var bad = program.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(method => method.Name.EndsWith("bad", StringComparison.Ordinal));
        var first = Assert.Throws<TargetInvocationException>(() => bad.Invoke(null, null)).InnerException!;
        Assert.Null(cache.GetValue(null));
        var second = Assert.Throws<TargetInvocationException>(() => bad.Invoke(null, null)).InnerException!;
        Assert.Equal(first.GetType(), second.GetType()); Assert.Equal(first.Message, second.Message);
        Assert.Null(cache.GetValue(null));
    }

    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName($"regex_cache_{Guid.NewGuid():N}"), typeof(object).Assembly);
    private static Assembly VerifyLoad(byte[] bytes)
    {
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(new MemoryStream(bytes))); return Assembly.Load(bytes);
    }
}

using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;
using static SharpTS.Tests.CompilerTests.NativeSourceInvocationPlanTests;

namespace SharpTS.Tests.CompilerTests;

public sealed class NativeNumericRestCapabilityTests
{
    [Theory]
    [InlineData("console.log(1);", false)]
    [InlineData("function add(a: number, b: number) { return a + b; }", false)]
    [InlineData("function add(...values: number[]) { return values[0]; }", true)]
    [InlineData("const fn = (...values: number[]) => values[0];", true)]
    [InlineData("class Item { method(...values: number[]) { return values[0]; } }", true)]
    [InlineData("eval('1 + 1');", true)]
    [InlineData("const owner = globalThis;", true)]
    public void PreparedSourceProofIsConservativeAndNativeOnly(string source, bool potential)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        Assert.True(FunctionEmissionPolicy.ForPreparedSource(statements, native: false).BindNumericRest4);
        var policy = FunctionEmissionPolicy.ForPreparedSource(statements, native: true);
        Assert.Equal(potential, policy.BindNumericRest4);
        if (potential) policy.RequireNumericRest4Producer();
        else Assert.Throws<InvalidOperationException>(policy.RequireNumericRest4Producer);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoProducerOmitsOnlyTheNativeOptionalLookup(bool native)
    {
        const string source = """
            function add(a: number, b: number, c: number, d: number): number { return a + b + c + d; }
            function invoke(fn: (a: number, b: number, c: number, d: number) => number): number { return fn(1, 2, 3, 4); }
            console.log(invoke(add));
            """;
        Assembly saved = Compile(source, native);
        Assert.Equal("10\n", Run(saved));
        Assert.NotNull(saved.GetType("$NumericRest4"));
        Assert.NotNull(saved.GetType("$TSFunction")!.GetField("_numericRest4", BindingFlags.Instance | BindingFlags.NonPublic));
        foreach (var constructor in saved.GetType("$TSFunction")!.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            Assert.Equal(!native, HasOptionalLookup(constructor));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProducerKeepsBindingAndAliasMutationUsesTheOrdinaryFallback(bool native)
    {
        const string source = """
            function add(...v: number[]): number { return v[0] + v[1] + v[2] + v[3]; }
            function mutate(...v: number[]): number { v[0]++; return v[0] + v[1] + v[2] + v[3]; }
            function plain(a: number, b: number, c: number, d: number): number { return a + b + c + d; }
            function invoke(fn: (...v: number[]) => number): number { return fn(1, 2, 3, 4); }
            console.log(invoke(add), invoke(mutate), invoke(plain as any), invoke(add.bind(null, 10)));
            let current: (...v: number[]) => number = add;
            function choose(value: number): number { current = mutate; return value; }
            function live(): number { return current(choose(1), 2, 3, 4); }
            console.log(live(), current === mutate);
            """;
        Assembly saved = Compile(source, native);
        Assert.Equal("10 11 10 16\n10 true\n", Run(saved));
        var program = saved.GetType("$Program")!;
        var original = program.GetMethod("add")!;
        Assert.Contains(original.CustomAttributes, attribute => attribute.AttributeType.Name == "$NumericRest4");
        Type function = saved.GetType("$TSFunction")!;
        MethodInfo factory = native ? function.GetMethod("GetOrCreateSource", BindingFlags.NonPublic | BindingFlags.Static)!
            : function.GetMethod("GetOrCreate")!;
        object wrapper = factory.Invoke(null, native ? [original, "add", 0, original] : [original, "add", 0])!;
        Assert.NotNull(function.GetField("_numericRest4", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(wrapper));
        Assert.All(function.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
            constructor => Assert.True(HasOptionalLookup(constructor)));
    }

    [Fact]
    public void ImportedProducerIsIncludedEvenWhenEntryHasNoRestDeclaration()
    {
        string directory = Path.Combine(Path.GetTempPath(), "native_rest_" + Guid.NewGuid().ToString("N"));
        string entry = Path.Combine(directory, "main.ts");
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.Combine(directory, "lib.ts")] = "export function add(...v: number[]): number { return v[0] + v[1] + v[2] + v[3]; }",
            [entry] = "import { add } from './lib'; function invoke(fn: (a:number,b:number,c:number,d:number)=>number):number{return fn(1,2,3,4);} console.log(invoke(add));"
        };
        var resolver = new ModuleResolver(entry, files);
        var modules = resolver.GetModulesInOrder(resolver.LoadModule(entry));
        var checker = new TypeChecker(); var types = checker.CheckModules(modules, resolver);
        Assert.DoesNotContain(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == SharpTS.Diagnostics.DiagnosticSeverity.Error);
        var compiler = Compiler(native: true);
        compiler.CompileModules(modules, resolver, types);
        Assembly saved = Verify(compiler.SaveToBytes());
        Assert.Equal("10\n", Run(saved));
        Assert.Contains(saved.GetType("$Program")!.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static),
            method => method.CustomAttributes.Any(attribute => attribute.AttributeType.Name == "$NumericRest4"));
        Assert.All(saved.GetType("$TSFunction")!.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
            constructor => Assert.True(HasOptionalLookup(constructor)));
    }

    [Fact]
    public void RuntimeFeatureOverridesCannotHideASourceProducer()
    {
        const string source = "function add(...v:number[]):number{return v[0]+v[1]+v[2]+v[3];} const alias=add; console.log(alias(1,2,3,4));";
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var types = new TypeChecker().Check(statements);
        var compiler = Compiler(native: true);
        compiler.SetRuntimeFeatures(new RuntimeFeatureDetector().Detect(
            new Parser(new Lexer("console.log(1);").ScanTokens()).ParseOrThrow()));
        compiler.Compile(statements, types);
        Assembly saved = Verify(compiler.SaveToBytes());
        Assert.Equal("10\n", Run(saved));
        Assert.Contains(saved.GetType("$Program")!.GetMethod("add")!.CustomAttributes,
            attribute => attribute.AttributeType.Name == "$NumericRest4");
        Assert.All(saved.GetType("$TSFunction")!.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
            constructor => Assert.True(HasOptionalLookup(constructor)));
    }

    [Fact]
    public void LegacyRuntimeEmitterOverloadsRetainFullBindingWithoutAProof()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("native_rest_default_" + Guid.NewGuid().ToString("N")), typeof(object).Assembly);
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, false, true);
        emitter.EmitAll(assembly.DefineDynamicModule("main"), new RuntimeFeatureDetector().Detect(
            new Parser(new Lexer("console.log(1);").ScanTokens()).ParseOrThrow()));
        using var stream = new MemoryStream(); assembly.Save(stream);
        Assembly saved = Verify(stream.ToArray());
        Assert.All(saved.GetType("$TSFunction")!.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
            constructor => Assert.True(HasOptionalLookup(constructor)));
    }

    private static bool HasOptionalLookup(MethodBase method) => Instructions(method).Any(instruction =>
        instruction.Member is MethodInfo called && called.DeclaringType == typeof(Type) && called.Name == "GetMethod" &&
        called.GetParameters().Any(parameter => parameter.ParameterType == typeof(BindingFlags)));
    private static string Run(Assembly assembly) => TestHarness.RunCompiledWithTimeout(() => assembly, TimeSpan.FromSeconds(20));
    private static ILCompiler Compiler(bool native)
    {
        var compiler = new ILCompiler("native_rest_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        if (native) compiler.EnableNativeAotProjectOutput();
        return compiler;
    }
    private static Assembly Compile(string source, bool native)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var types = new TypeChecker().Check(statements);
        var compiler = Compiler(native); compiler.Compile(statements, types);
        return Verify(compiler.SaveToBytes());
    }
    private static Assembly Verify(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes); using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(stream)); return Assembly.Load(bytes);
    }
}

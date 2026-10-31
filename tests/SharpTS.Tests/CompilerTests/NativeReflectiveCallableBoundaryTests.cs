using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;
using static SharpTS.Tests.CompilerTests.NativeSourceInvocationPlanTests;

namespace SharpTS.Tests.CompilerTests;

public sealed class NativeReflectiveCallableBoundaryTests
{
    public enum Profile { Managed, DefaultNativeRuntime, NativeSource }
    private const BindingFlags Methods = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    [Theory]
    [InlineData(Profile.Managed)]
    [InlineData(Profile.DefaultNativeRuntime)]
    [InlineData(Profile.NativeSource)]
    public void ResidualBridgesHaveAnExplicitBoundaryWithoutChangingDefaultAbi(Profile profile)
    {
        var assembly = Build(profile);
        bool rejects = profile == Profile.NativeSource;
        var both = new Both(); object[] args = [1d, 2d];
        var wrapper = Wrap(assembly, both);
        if (rejects) Boundary(assembly, () => Invoke(wrapper, args));
        else
        {
            Assert.Equal(11d, Invoke(wrapper, args)); Assert.Equal("Invoke", both.Branch); Assert.Same(args, both.Arguments);
        }
        if (rejects) Boundary(assembly, () => Call(assembly, both, args));
        else
        {
            Assert.Equal(22, Call(assembly, both, args)); Assert.Equal("Call", both.Branch);
            Assert.Null(both.Context); Assert.IsType<List<object>>(both.Arguments); Assert.NotSame(args, both.Arguments);
        }
        var original = new InvalidOperationException("sentinel");
        var throwing = new ThrowCall(original);
        if (rejects)
        {
            Boundary(assembly, () => Invoke(Wrap(assembly, throwing), args));
            Boundary(assembly, () => Call(assembly, throwing, args));
        }
        else
        {
            var wrapperError = Failure(() => Invoke(Wrap(assembly, throwing), args));
            Assert.Same(original, Assert.IsType<TargetInvocationException>(wrapperError).InnerException);
            Assert.Same(original, Failure(() => Call(assembly, throwing, args)));
        }
        var invokeOnly = new InvokeOnly();
        if (rejects) Boundary(assembly, () => Invoke(Wrap(assembly, invokeOnly), args));
        else Assert.Equal(44d, Invoke(Wrap(assembly, invokeOnly), args));
        GuestTypeError(assembly, () => Call(assembly, invokeOnly, args));
        if (rejects) Boundary(assembly, () => Invoke(Wrap(assembly, new object()), args));
        else Assert.Null(Invoke(Wrap(assembly, new object()), args));
        Assert.IsType<NullReferenceException>(Failure(() => Invoke(Wrap(assembly, null), args)));
        GuestTypeError(assembly, () => Call(assembly, null, args));
        if (rejects) Boundary(assembly, () => Invoke(Wrap(assembly, new Ambiguous()), args));
        else Assert.IsType<AmbiguousMatchException>(Failure(() => Invoke(Wrap(assembly, new Ambiguous()), args)));
    }

    [Theory]
    [InlineData(Profile.Managed)]
    [InlineData(Profile.DefaultNativeRuntime)]
    [InlineData(Profile.NativeSource)]
    public void GeneralDelegatesAreNotPromotedAndExistingExactFuncKeepsItsExceptionShape(Profile profile)
    {
        var assembly = Build(profile); object[] args = [1d, 2d];
        Func<object[], int> general = values => values.Length;
        if (profile == Profile.NativeSource) Boundary(assembly, () => Invoke(Wrap(assembly, general), args));
        else Assert.Equal(2d, Invoke(Wrap(assembly, general), args));
        GuestTypeError(assembly, () => Call(assembly, general, args));
        Func<object[], object> exact = values => values.Length;
        Assert.Equal(2d, Invoke(Wrap(assembly, exact), args));
        Assert.Equal(2, Call(assembly, exact, args));
        var original = new InvalidOperationException("sentinel");
        Func<object[], object> fail = _ => throw original;
        var failure = Failure(() => Invoke(Wrap(assembly, fail), args));
        if (profile == Profile.Managed) Assert.Same(original, Assert.IsType<TargetInvocationException>(failure).InnerException);
        else Assert.Same(original, failure);
        Assert.Same(original, Failure(() => Call(assembly, fail, args)));
    }

    [Fact]
    public void FullRuntimePreservesEveryActualErasedCapabilityAndRemovesOnlyTheResidualLookups()
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
        var runtime = new RuntimeEmitter(TypeProvider.Runtime, false, true).EmitAll(module, RuntimeFeatureSet.EmitEverything(),
            FunctionEmissionPolicy.Full with { RejectReflectiveCallableFallback = true });
        using var bytes = new MemoryStream(); builder.Save(bytes); var assembly = VerifyLoad(bytes.ToArray());
        var contract = assembly.GetType("$INativeCallable")!;
        var carriers = assembly.GetTypes().Where(type => !type.IsInterface && type.GetMethods(Methods | BindingFlags.DeclaredOnly).Any(method =>
            method.Name == "Invoke" && method.ReturnType == typeof(object) && method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual([typeof(object[]) ]))).ToArray();
        Assert.Equal(24, carriers.Length); Assert.Equal(24, runtime.NativeCallables!.CarrierCount);
        foreach (var carrier in carriers)
        {
            Assert.True(contract.IsAssignableFrom(carrier));
            var target = Assert.Single(carrier.GetInterfaceMap(contract).TargetMethods);
            Assert.Contains(Instructions(target), instruction => instruction.Member?.Name == "Invoke" && instruction.Member.DeclaringType == carrier);
        }
        foreach (var method in new[] { assembly.GetType("$MethodCallable")!.GetMethod("Invoke")!, assembly.GetType("$Runtime")!.GetMethod("InvokeMethodValue")! })
        {
            Assert.DoesNotContain(Instructions(method), instruction => instruction.Member?.Name == "GetMethod");
            Assert.Contains(Instructions(method), instruction => instruction.Member?.Name == "RejectReflectiveCallableFallback");
        }
        var rejection = assembly.GetType("$Runtime")!.GetMethod("RejectReflectiveCallableFallback", Methods)!;
        Assert.True(rejection.IsAssembly && rejection.IsStatic); Assert.Empty(rejection.GetParameters());
        Assert.Equal(typeof(object), rejection.ReturnType);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceAliasesCallbacksAndCurrentManagedDuckCallBehaviorRemainExplicit(bool native)
    {
        const string source = """
            const fn:any=(x:number)=>x+1;
            console.log(fn.call(null,2),[1,2].map(fn).join(','));
            Promise.resolve(2).then(fn).then((x:any)=>console.log(x));
            """;
        var assembly = Compile(source, native);
        Assert.Equal("3 2,3\n3\n", TestHarness.RunCompiledWithTimeout(() => assembly, TimeSpan.FromSeconds(20)));
        const string duck = """
            class C { Call(context:any,args:any):any { console.log('called');return 42; } }
            const value:any=new C();
            try { console.log(value(1,2)); }
            catch(error:any) { console.log(error instanceof TypeError);console.log(error.message); }
            """;
        var duckAssembly = Compile(duck, native);
        string output = TestHarness.RunCompiledWithTimeout(() => duckAssembly, TimeSpan.FromSeconds(20));
        Assert.Equal(native ? "true\n" + RuntimeEmitter.NativeReflectiveCallableMessage + "\n" : "called\n42\n", output);
    }

    private static object Wrap(Assembly assembly, object? callable) => Activator.CreateInstance(assembly.GetType("$MethodCallable")!, [callable])!;
    private static object? Invoke(object wrapper, object[] args) => wrapper.GetType().GetMethod("Invoke")!.Invoke(wrapper, [args]);
    private static object? Call(Assembly assembly, object? callable, object[] args) => assembly.GetType("$Runtime")!.GetMethod("InvokeMethodValue")!.Invoke(null, [new object(), callable, args]);
    private static Exception Failure(Action action) => Assert.Throws<TargetInvocationException>(action).InnerException!;
    private static object GuestTypeError(Assembly assembly, Action action)
    {
        object error = assembly.GetType("$Runtime")!.GetMethod("WrapException")!.Invoke(null, [Failure(action)])!;
        Assert.Equal("$TypeError", error.GetType().Name); return error;
    }
    private static void Boundary(Assembly assembly, Action action)
    {
        object error = GuestTypeError(assembly, action);
        Assert.Equal(RuntimeEmitter.NativeReflectiveCallableMessage, error.GetType().GetProperty("Message")!.GetValue(error));
    }
    private static Assembly Build(Profile profile)
    {
        if (profile != Profile.DefaultNativeRuntime) return Compile("console.log(42);", profile == Profile.NativeSource);
        var builder = NewAssembly(); new RuntimeEmitter(TypeProvider.Runtime, false, true).EmitAll(builder.DefineDynamicModule("main"), Detect("console.log(42);"));
        using var bytes = new MemoryStream(); builder.Save(bytes); return VerifyLoad(bytes.ToArray());
    }
    private static Assembly Compile(string source, bool native)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow(); var checker = new TypeChecker(); var types = checker.Check(statements);
        Assert.DoesNotContain(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == SharpTS.Diagnostics.DiagnosticSeverity.Error);
        var compiler = new ILCompiler("native_call_boundary_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        if (native) compiler.EnableNativeAotProjectOutput(); compiler.Compile(statements, types); return VerifyLoad(compiler.SaveToBytes());
    }
    private static RuntimeFeatureSet Detect(string source) => new RuntimeFeatureDetector().Detect(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName("call_boundary_" + Guid.NewGuid().ToString("N")), typeof(object).Assembly);
    private static Assembly VerifyLoad(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes); using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]); Assert.Empty(verifier.Verify(stream)); return Assembly.Load(bytes);
    }
    public sealed class Both
    {
        public string? Branch; public object? Context; public object? Arguments;
        public object Invoke(object[] args) { Branch="Invoke";Arguments=args;return 11; }
        public object Call(object? context,List<object> args) { Branch="Call";Context=context;Arguments=args;return 22; }
    }
    public sealed class InvokeOnly { public object Invoke(object[] args) => 44; }
    public sealed class ThrowCall(Exception error) { public object Call(object? context,List<object> args) => throw error; }
    public sealed class Ambiguous
    {
        public object Invoke(object[] args) => 1;
        public object Invoke(object args) => 2;
        public object Call(object? context,List<object> args) => 3;
    }
}

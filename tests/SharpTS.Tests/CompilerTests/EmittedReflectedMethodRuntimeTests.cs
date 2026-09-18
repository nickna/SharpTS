using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedReflectedMethodRuntimeTests
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static PropertyInfo[] Properties => typeof(EmittedReflectedMethodRuntime).GetProperties().Where(p => typeof(MemberInfo).IsAssignableFrom(p.PropertyType)).ToArray();
    public static IEnumerable<object[]> Declarations => Properties.Select(p => new object[] { p.Name });

    [Theory]
    [MemberData(nameof(Declarations))]
    public void MissingDeclarationCanBeRepairedAndCompletionFreezesMetadata(string missing)
    {
        var owner = new EmittedReflectedMethodRuntime(); var handles = NewHandles();
        var property = Properties.Single(p => p.Name == missing);
        Expect<InvalidOperationException>(() => property.GetValue(owner));
        Expect<ArgumentNullException>(() => property.SetValue(owner, null));
        foreach (var other in Properties.Where(p => p.Name != missing)) other.SetValue(owner, handles[other.Name]);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission); Assert.False(owner.IsComplete);
        property.SetValue(owner, handles[missing]); Assert.Same(handles[missing], property.GetValue(owner));
        Expect<InvalidOperationException>(() => property.SetValue(owner, handles[missing]));
        owner.MarkCallableFinalized(); owner.CompleteEmission(); Assert.True(owner.IsComplete);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
        Assert.Throws<InvalidOperationException>(owner.MarkCallableFinalized);
        foreach (var item in Properties) Expect<InvalidOperationException>(() => item.SetValue(owner, handles[item.Name]));
    }

    [Fact]
    public void DeclarationsAloneCannotCompleteTheCallableLifecycle()
    {
        var owner = new EmittedReflectedMethodRuntime();
        Assert.Throws<InvalidOperationException>(owner.MarkCallableFinalized);
        var handles = NewHandles();
        foreach (var item in Properties) item.SetValue(owner, handles[item.Name]);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
        Assert.False(owner.IsComplete); Assert.Same(handles["CallableInvoke"], owner.CallableInvoke);
        owner.MarkCallableFinalized(); Assert.Throws<InvalidOperationException>(owner.MarkCallableFinalized);
        owner.CompleteEmission(); Assert.True(owner.IsComplete);
    }

    [Fact]
    public void CallableShellAndFinalizationDoNotRequireUnrelatedRuntimeDeclarations()
    {
        var owner = new EmittedReflectedMethodRuntime(); var builder = NewAssembly();
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        emitter.EmitMethodCallableTypeDefinition(builder.DefineDynamicModule("main"), owner);
        Assert.Same(builder, owner.CallableInvoke.Module.Assembly); Assert.False(owner.IsComplete);
        Assert.Throws<InvalidOperationException>(() => owner.FindMethod);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
        emitter.EmitMethodCallableFinalize(owner);
        Assert.Throws<InvalidOperationException>(owner.MarkCallableFinalized);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
        var loaded = SaveVerifyLoad(builder);
        var wrapper = Activator.CreateInstance(loaded.GetType("$MethodCallable")!, [new InvokeTarget()])!;
        Assert.Equal("shell", wrapper.GetType().GetMethod("Invoke")!.Invoke(wrapper, [new object[] { "shell" }]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedEmitterKeepsReflectionCachesWrappersAndExceptionsIndependent(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        var owners = new HashSet<object>(); var caches = new HashSet<object>(); var wrappers = new HashSet<object>();
        foreach (string code in new[] { "const n=1;", "function f(a:number){return a;} f.bind(null,1)();", "const n=2;" })
        {
            var builder = NewAssembly(); var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"), Detect(code));
            var owner = runtime.ReflectedMethods; Assert.True(owners.Add(owner)); Assert.True(owner.IsComplete);
            foreach (var property in Properties) Assert.Same(builder, ((MemberInfo)property.GetValue(owner)!).Module.Assembly);
            var loaded = SaveVerifyLoad(builder); var type = loaded.GetType("$Runtime")!;
            Assert.True(caches.Add(type.GetField(owner.Cache.Name, Members)!.GetValue(null)!));
            var pascal = type.GetMethod(owner.ToPascalCase.Name)!;
            Assert.Equal("Echo", pascal.Invoke(null, ["echo"])); Assert.Equal("Echo", pascal.Invoke(null, ["Echo"]));
            Assert.Equal("", pascal.Invoke(null, [""])); Assert.Null(pascal.Invoke(null, [null]));
            var find = type.GetMethod(owner.FindMethod.Name)!;
            var method = (MethodInfo)find.Invoke(null, [typeof(HostTarget), "Echo", BindingFlags.Public | BindingFlags.Instance])!;
            Assert.Empty(method.GetParameters()); Assert.Equal("Echo", method.Name);
            Assert.Null(find.Invoke(null, [typeof(HostTarget), "Missing", BindingFlags.Public | BindingFlags.Instance]));
            var error = new InvalidOperationException("same exception"); var host = new HostTarget(error);
            var get = type.GetMethod(runtime.ObjectRead.FieldsProperty.Name)!;
            var wrapper = get.Invoke(null, [host, "echo"])!;
            Assert.True(wrappers.Add(wrapper)); Assert.Same(wrapper, get.Invoke(null, [host, "echo"]));
            Assert.NotSame(wrapper, get.Invoke(null, [new HostTarget(error), "echo"]));
            Assert.Equal("zero", type.GetMethod(runtime.Invocation.Value.Name)!.Invoke(null, [wrapper, Array.Empty<object>()]));
            var unwrapped = type.GetMethod(owner.InvokeUnwrapped.Name)!;
            Assert.Equal("zero", unwrapped.Invoke(null, [method, host, Array.Empty<object>()]));
            var thrown = Assert.Throws<TargetInvocationException>(() => unwrapped.Invoke(null, [typeof(HostTarget).GetMethod("Fail"), host, Array.Empty<object>()]));
            Assert.Same(error, thrown.InnerException);
            var callable = loaded.GetType(owner.CallableType.Name)!;
            object? Call(object target) => callable.GetMethod(owner.CallableInvoke.Name)!.Invoke(Activator.CreateInstance(callable, [target]), [new object[] { "value" }]);
            Assert.Equal("value", Call(new InvokeTarget())); Assert.Equal("value", Call(new CallTarget())); Assert.Null(Call(new object()));
            Assert.DoesNotContain(loaded.GetReferencedAssemblies(), a => a.Name == "SharpTS");
            Assert.Equal(hosted, loaded.GetReferencedAssemblies().Any(a => a.Name == "SharpTS.Hosting.Abstractions"));
        }
    }

    [Fact]
    public void OwnerAndHelpersUseExplicitDependencies()
    {
        Assert.Equal(8, Properties.Count(p => p.Name != "SuperMethod"));
        Assert.Equal(9, Properties.Length);
        Assert.NotSame(new EmittedRuntime().ReflectedMethods, new EmittedRuntime().ReflectedMethods);
        Assert.Null(typeof(EmittedRuntime).GetProperty("ReflectedMethods")!.SetMethod);
        foreach (string old in new[] { "ToPascalCase", "SafeGetMethod", "ReflectedMethodCacheField", "MethodCallableType", "MethodCallableCtor", "MethodCallableInvoke", "MethodCallableField", "InvokeMethodUnwrapped" })
            Assert.Null(typeof(EmittedRuntime).GetProperty(old));
        foreach (string name in new[] { "EmitToPascalCase", "EmitSafeGetMethod", "EmitMethodCallableTypeDefinition", "EmitMethodCallableFinalize", "EmitInvokeMethodUnwrapped" })
            Assert.DoesNotContain(typeof(RuntimeEmitter).GetMethod(name, Members)!.GetParameters(), p => p.ParameterType == typeof(EmittedRuntime) || p.ParameterType == typeof(RuntimeFeatureSet));
    }

    public sealed class HostTarget(Exception error)
    {
        public string Echo() => "zero";
        public string Echo(object value) => value.ToString()!;
        public object Fail() => throw error;
    }
    public sealed class InvokeTarget { public object Invoke(object[] values) => values[0]; }
    public sealed class CallTarget { public object Call(object? interpreter, List<object> values) { Assert.Null(interpreter); return values[0]; } }

    private static Dictionary<string, MemberInfo> NewHandles()
    {
        var type = NewAssembly().DefineDynamicModule("main").DefineType("Handles");
        var ctor = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [typeof(object)]);
        return new()
        {
            ["ToPascalCase"] = type.DefineMethod("ToPascalCase", MethodAttributes.Public | MethodAttributes.Static, typeof(string), [typeof(string)]),
            ["SuperMethod"] = type.DefineMethod("GetSuperMethod", MethodAttributes.Public | MethodAttributes.Static, typeof(object), [typeof(object), typeof(string)]),
            ["FindMethod"] = type.DefineMethod("FindMethod", MethodAttributes.Public | MethodAttributes.Static, typeof(MethodInfo), [typeof(Type), typeof(string), typeof(BindingFlags)]),
            ["Cache"] = type.DefineField("Cache", typeof(object), FieldAttributes.Static),
            ["CallableType"] = type, ["CallableConstructor"] = ctor,
            ["CallableInvoke"] = type.DefineMethod("Invoke", MethodAttributes.Public, typeof(object), [typeof(object[])]),
            ["CallableField"] = type.DefineField("Callable", typeof(object), FieldAttributes.Private),
            ["InvokeUnwrapped"] = type.DefineMethod("InvokeUnwrapped", MethodAttributes.Public | MethodAttributes.Static, typeof(object), [typeof(MethodBase), typeof(object), typeof(object[])])
        };
    }
    private static void Expect<T>(Action action) where T : Exception => Assert.IsType<T>(Assert.Throws<TargetInvocationException>(action).InnerException);
    private static RuntimeFeatureSet Detect(string code) => new RuntimeFeatureDetector().Detect(new Parser(new Lexer(code).ScanTokens()).ParseOrThrow());
    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName($"reflected_method_{Guid.NewGuid():N}"), typeof(object).Assembly);
    private static Assembly SaveVerifyLoad(PersistedAssemblyBuilder builder)
    {
        using var bytes = new MemoryStream(); builder.Save(bytes); bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes)); return Assembly.Load(bytes.ToArray());
    }
}

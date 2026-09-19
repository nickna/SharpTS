using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedProxyConstructionRuntimeTests
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    [Fact]
    public void OptionalFactoriesHaveExplicitAvailabilityAndCannotBeReplaced()
    {
        var runtime = new EmittedRuntime();
        Assert.Null(runtime.ProxyConstruction);
        Assert.Throws<InvalidOperationException>(runtime.RequireProxyConstruction);
        Begin(runtime);
        var owner = runtime.RequireProxyConstruction();
        Assert.Same(owner, runtime.ProxyConstruction);
        Expect<InvalidOperationException>(() => Begin(runtime));
        Assert.True(typeof(EmittedRuntime).GetProperty("ProxyConstruction")!.SetMethod!.IsPrivate);
        Assert.Null(typeof(EmittedRuntime).GetProperty("CreateProxy"));
        Assert.Null(typeof(EmittedRuntime).GetProperty("CreateRevocableProxy"));
        var other = new EmittedRuntime(); Begin(other);
        Assert.NotSame(owner, other.RequireProxyConstruction());
    }

    [Theory]
    [InlineData("Create")]
    [InlineData("CreateRevocable")]
    public void MissingDeclarationAllowsRepairWhileNullDuplicateAndCompletedWritesFail(string missing)
    {
        var runtime = new EmittedRuntime(); Begin(runtime); var owner = runtime.RequireProxyConstruction();
        var type = NewAssembly().DefineDynamicModule("main").DefineType("Handles");
        var methods = new Dictionary<string, MethodBuilder>();
        foreach (string name in new[] { "Create", "CreateRevocable" })
        {
            var method = type.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Static, typeof(object), [typeof(object), typeof(object)]);
            methods[name] = method;
            if (name != missing) typeof(EmittedProxyConstructionRuntime).GetProperty(name)!.SetValue(owner, method);
        }
        var property = typeof(EmittedProxyConstructionRuntime).GetProperty(missing)!;
        Expect<InvalidOperationException>(() => property.GetValue(owner));
        Expect<ArgumentNullException>(() => property.SetValue(owner, null));
        Expect<InvalidOperationException>(() => Complete(owner));
        Assert.False(owner.IsComplete);
        property.SetValue(owner, methods[missing]); Assert.Same(methods[missing], property.GetValue(owner));
        Expect<InvalidOperationException>(() => property.SetValue(owner, methods[missing]));
        Complete(owner); Assert.True(owner.IsComplete);
        Expect<InvalidOperationException>(() => Complete(owner));
        foreach (string name in methods.Keys)
            Expect<InvalidOperationException>(() => typeof(EmittedProxyConstructionRuntime).GetProperty(name)!.SetValue(owner, methods[name]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScopedFactoriesUseOnlySuppliedMetadataRegardlessOfGlobalProxySelection(bool selected)
    {
        var helper = typeof(RuntimeEmitter).GetMethod("EmitProxyMethods", Members)!;
        var inputType = typeof(RuntimeEmitter).GetNestedType("ProxyConstructionInputs", BindingFlags.NonPublic)!;
        Assert.Equal(new[] { typeof(TypeBuilder), typeof(EmittedProxyConstructionRuntime), inputType }, helper.GetParameters().Select(p => p.ParameterType));
        var constructor = Assert.Single(inputType.GetConstructors());
        Assert.Equal(new[] { typeof(Type), typeof(Type), typeof(FieldInfo), typeof(MethodInfo), typeof(ConstructorInfo) }, constructor.GetParameters().Select(p => p.ParameterType));
        var builder = NewAssembly(); var type = builder.DefineDynamicModule("main").DefineType("Probe", TypeAttributes.Public);
        var undefinedField = type.DefineField("Undefined", typeof(object), FieldAttributes.Public | FieldAttributes.Static);
        var calls = type.DefineField("Calls", typeof(int), FieldAttributes.Public | FieldAttributes.Static);
        var wrapper = type.DefineMethod("Wrap", MethodAttributes.Public | MethodAttributes.Static, typeof(Exception), [typeof(object)]);
        var il = wrapper.GetILGenerator();
        il.Emit(OpCodes.Ldsfld, calls); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stsfld, calls);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, typeof(Exception)); il.Emit(OpCodes.Ret);
        var input = constructor.Invoke([typeof(Uri), typeof(Version), undefinedField, wrapper, typeof(ArgumentException).GetConstructor([typeof(string)])!]);
        var runtime = new EmittedRuntime(); Begin(runtime); var owner = runtime.RequireProxyConstruction();
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        typeof(RuntimeEmitter).GetField("_features", Members)!.SetValue(emitter, new RuntimeFeatureSet { UsesProxy = selected });
        helper.Invoke(emitter, [type, owner, input]);
        Assert.Same(type, owner.Create.DeclaringType); Assert.Same(type, owner.CreateRevocable.DeclaringType);
        Assert.False(owner.IsComplete); Assert.Empty(runtime.RequiredSharpTSRuntimeReasons);
        type.CreateType(); Complete(owner);
        var loaded = SaveVerifyLoad(builder).GetType("Probe")!;
        var undefined = new object(); loaded.GetField("Undefined")!.SetValue(null, undefined);
        var target = new Dictionary<string, object?>(); var handler = new Dictionary<string, object?>();
        foreach (string name in new[] { "CreateProxy", "CreateRevocableProxy" })
        {
            var method = loaded.GetMethod(name)!;
            foreach (object? invalid in new object?[] { null, new Uri("https://example.test"), new Version(1, 2) })
            foreach (int argument in new[] { 0, 1 })
            {
                object?[] arguments = [target, handler]; arguments[argument] = invalid;
                var error = Assert.IsType<ArgumentException>(Assert.Throws<TargetInvocationException>(() => method.Invoke(null, arguments)).InnerException);
                Assert.Equal("Cannot create proxy with a non-object as " + (argument == 0 ? "target" : "handler"), error.Message);
            }
        }
        Assert.Equal(12, loaded.GetField("Calls")!.GetValue(null));
        object proxy = loaded.GetMethod("CreateProxy")!.Invoke(null, [target, handler])!;
        Assert.Same(target, proxy.GetType().GetProperty("Target")!.GetValue(proxy));
        var pair = Assert.IsAssignableFrom<IDictionary>(loaded.GetMethod("CreateRevocableProxy")!.Invoke(null, [target, handler]));
        Assert.Equal(2, pair.Count); var revoke = Assert.IsAssignableFrom<Delegate>(pair["revoke"]);
        for (int i = 0; i < 2; i++) Assert.Same(undefined, revoke.DynamicInvoke(new object?[] { Array.Empty<object>() }));
        Assert.Equal(true, pair["proxy"]!.GetType().GetProperty("IsRevoked")!.GetValue(pair["proxy"]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedEmitterPreservesOptionalFactoriesAndSoftRuntimeRequirements(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        var owners = new HashSet<EmittedProxyConstructionRuntime>(); var handles = new HashSet<MethodBuilder>();
        foreach (string source in new[] { "const n=1;", "new Proxy({},{});", "const n=2;", "Proxy.revocable({},{});" })
        {
            var builder = NewAssembly(); var features = new RuntimeFeatureDetector().Detect(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
            var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"), features);
            Assert.Equal(features.UsesProxy, runtime.ProxyConstruction is not null);
            Assert.Equal(features.UsesProxy, runtime.RequiredSharpTSRuntimeReasons.Contains("Proxy"));
            Assert.Equal(features.UsesProxy, runtime.RequiredSharpTSRuntimeRequirements.HasFlag(SharpTSRuntimeRequirements.RuntimeAssembly));
            var loaded = SaveVerifyLoad(builder); var type = loaded.GetType("$Runtime")!;
            if (features.UsesProxy)
            {
                var owner = runtime.RequireProxyConstruction(); Assert.True(owners.Add(owner)); Assert.True(owner.IsComplete);
                foreach (var handle in new[] { owner.Create, owner.CreateRevocable })
                {
                    Assert.True(handles.Add(handle)); Assert.Same(builder, handle.Module.Assembly); Assert.Same(runtime.RuntimeClass.Type, handle.DeclaringType);
                    var method = type.GetMethod(handle.Name)!;
                    Assert.True(method.IsPublic && method.IsStatic); Assert.Equal(typeof(object), method.ReturnType);
                    Assert.Equal(new[] { typeof(object), typeof(object) }, method.GetParameters().Select(p => p.ParameterType));
                }
                Assert.True(type.GetMethod("CreateProxy")!.MetadataToken < type.GetMethod("CreateRevocableProxy")!.MetadataToken);
            }
            else
            {
                Assert.Throws<InvalidOperationException>(runtime.RequireProxyConstruction);
                Assert.Null(type.GetMethod("CreateProxy")); Assert.Null(type.GetMethod("CreateRevocableProxy"));
            }
            Assert.DoesNotContain(loaded.GetReferencedAssemblies(), a => a.Name == "SharpTS");
            Assert.Equal(hosted, loaded.GetReferencedAssemblies().Any(a => a.Name == "SharpTS.Hosting.Abstractions"));
        }
    }

    private static void Begin(EmittedRuntime runtime) => typeof(EmittedRuntime).GetMethod("BeginProxyConstructionEmission", Members)!.Invoke(runtime, null);
    private static void Complete(EmittedProxyConstructionRuntime owner) => typeof(EmittedProxyConstructionRuntime).GetMethod("CompleteEmission", Members)!.Invoke(owner, null);
    private static void Expect<T>(Action action) where T : Exception => Assert.IsType<T>(Assert.Throws<TargetInvocationException>(action).InnerException);
    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName($"proxy_construction_{Guid.NewGuid():N}"), typeof(object).Assembly);
    private static Assembly SaveVerifyLoad(PersistedAssemblyBuilder builder)
    {
        using var bytes = new MemoryStream(); builder.Save(bytes); bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        var errors = verifier.Verify(bytes); Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        return Assembly.Load(bytes.ToArray());
    }
}

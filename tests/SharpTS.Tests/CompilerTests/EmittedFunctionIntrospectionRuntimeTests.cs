using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedFunctionIntrospectionRuntimeTests
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    [Theory]
    [InlineData("GetProperty")]
    [InlineData("IsConstructor")]
    public void MetadataRequiresDeclarationAndRejectsChangesAfterCompletion(string name)
    {
        var owner = new EmittedFunctionIntrospectionRuntime();
        var property = typeof(EmittedFunctionIntrospectionRuntime).GetProperty(name)!;
        Expect<InvalidOperationException>(() => property.GetValue(owner));
        Expect<ArgumentNullException>(() => property.SetValue(owner, null));
        var builder = NewAssembly(); var type = builder.DefineDynamicModule("main").DefineType("Handles");
        var get = type.DefineMethod("GetProperty", MethodAttributes.Public | MethodAttributes.Static, typeof(object), [typeof(object), typeof(string)]);
        get.GetILGenerator().Emit(OpCodes.Ldnull); get.GetILGenerator().Emit(OpCodes.Ret);
        var construct = type.DefineMethod("IsConstructor", MethodAttributes.Public | MethodAttributes.Static, typeof(bool), [typeof(object)]);
        construct.GetILGenerator().Emit(OpCodes.Ldc_I4_0); construct.GetILGenerator().Emit(OpCodes.Ret);
        if (name == "GetProperty") owner.IsConstructor = construct; else owner.GetProperty = get;
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission); Assert.False(owner.IsComplete);
        var value = name == "GetProperty" ? get : construct;
        property.SetValue(owner, value); Assert.Same(value, property.GetValue(owner));
        Expect<InvalidOperationException>(() => property.SetValue(owner, value));
        owner.CompleteEmission(); Assert.True(owner.IsComplete);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
        Expect<InvalidOperationException>(() => property.SetValue(owner, value));
    }

    [Fact]
    public void PropertyLookupIsAvailableBeforeConstructorCapabilityIsDeclared()
    {
        var builder = NewAssembly(); var type = builder.DefineDynamicModule("main").DefineType("EarlyLookup");
        var owner = new EmittedFunctionIntrospectionRuntime();
        var method = type.DefineMethod("GetProperty", MethodAttributes.Public | MethodAttributes.Static, typeof(object), [typeof(object), typeof(string)]);
        method.GetILGenerator().Emit(OpCodes.Ldnull); method.GetILGenerator().Emit(OpCodes.Ret);
        owner.GetProperty = method;
        Assert.Same(method, owner.GetProperty); Assert.False(owner.IsComplete);
        Assert.Throws<InvalidOperationException>(() => owner.IsConstructor);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
        Assert.Same(method, owner.GetProperty);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedEmitterKeepsFunctionInspectionAndPrototypeCachesIndependent(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        var owners = new HashSet<object>(); var wrappers = new HashSet<object>(); var prototypes = new HashSet<object>();
        foreach (string code in new[] { "const n=1;", "function f(a:number){return a;} f.bind(null,1)();", "const n=2;" })
        {
            var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
            var runtime = emitter.EmitAll(module, Detect(code)); var owner = runtime.FunctionIntrospection;
            Assert.True(owners.Add(owner)); Assert.True(owner.IsComplete);
            Assert.Same(builder, owner.GetProperty.Module.Assembly); Assert.Same(builder, owner.IsConstructor.Module.Assembly);
            var probe = module.DefineType("UserFunctions", TypeAttributes.Public);
            var target = probe.DefineMethod("Sample", MethodAttributes.Public | MethodAttributes.Static, typeof(object), [typeof(object)]);
            target.GetILGenerator().Emit(OpCodes.Ldarg_0); target.GetILGenerator().Emit(OpCodes.Ret); probe.CreateType();
            var loaded = SaveVerifyLoad(builder); var runtimeType = loaded.GetType("$Runtime")!;
            var functionType = loaded.GetType("$TSFunction")!;
            var wrapper = functionType.GetMethod("GetOrCreate")!.Invoke(null, [loaded.GetType("UserFunctions")!.GetMethod("Sample"), "sample", 1])!;
            Assert.True(wrappers.Add(wrapper));
            object? Get(object value, string key) => runtimeType.GetMethod(owner.GetProperty.Name)!.Invoke(null, [value, key]);
            bool IsConstructor(object? value) => (bool)runtimeType.GetMethod(owner.IsConstructor.Name)!.Invoke(null, [value])!;
            var undefined = loaded.GetType("$Undefined")!.GetField("Instance")!.GetValue(null)!;
            Assert.Equal("sample", Get(wrapper, "name")); Assert.Equal(1.0, Convert.ToDouble(Get(wrapper, "length")));
            Assert.Same(undefined, Get(wrapper, "missing"));
            var prototype = Get(wrapper, "prototype")!; Assert.True(prototypes.Add(prototype));
            Assert.Same(prototype, Get(wrapper, "prototype"));
            Assert.Same(wrapper, runtimeType.GetMethod(runtime.ObjectRead.Property.Name)!.Invoke(null, [prototype, "constructor"]));
            Assert.True(IsConstructor(wrapper)); Assert.True(IsConstructor(functionType));
            Assert.False(IsConstructor(null)); Assert.False(IsConstructor(undefined)); Assert.False(IsConstructor(new object()));
            var intrinsic = (IDictionary)runtimeType.GetField(runtime.FunctionPrototypes.Prototype.Name)!.GetValue(null)!;
            var call = intrinsic["call"]!; Assert.False(IsConstructor(call));
            var boundType = loaded.GetType("$BoundTSFunction")!;
            var bound = Activator.CreateInstance(boundType, [wrapper, null, Array.Empty<object>()])!;
            var boundIntrinsic = Activator.CreateInstance(boundType, [call, null, Array.Empty<object>()])!;
            Assert.True(IsConstructor(bound)); Assert.False(IsConstructor(boundIntrinsic));
            Assert.DoesNotContain(loaded.GetReferencedAssemblies(), a => a.Name == "SharpTS");
            Assert.Equal(hosted, loaded.GetReferencedAssemblies().Any(a => a.Name == "SharpTS.Hosting.Abstractions"));
        }
    }

    [Fact]
    public void OwnerAndHelpersUseExplicitDependencies()
    {
        var first = new EmittedRuntime(); var second = new EmittedRuntime();
        Assert.NotSame(first.FunctionIntrospection, second.FunctionIntrospection);
        Assert.Null(typeof(EmittedRuntime).GetProperty("FunctionIntrospection")!.SetMethod);
        foreach (string old in new[] { "GetFunctionMethod", "IsConstructorMethod" }) Assert.Null(typeof(EmittedRuntime).GetProperty(old));
        foreach (string name in new[] { "EmitGetFunctionMethod", "EmitIsConstructor" })
        {
            var method = typeof(RuntimeEmitter).GetMethod(name, Members)!;
            Assert.DoesNotContain(method.GetParameters(), p => p.ParameterType == typeof(EmittedRuntime) || p.ParameterType == typeof(RuntimeFeatureSet));
            foreach (var parameter in method.GetParameters().Where(p => p.ParameterType.IsNestedPrivate))
                Assert.DoesNotContain(parameter.ParameterType.GetFields(Members), f => f.FieldType == typeof(EmittedRuntime) || f.FieldType == typeof(RuntimeFeatureSet));
        }
    }

    private static void Expect<T>(Action action) where T : Exception => Assert.IsType<T>(Assert.Throws<TargetInvocationException>(action).InnerException);
    private static RuntimeFeatureSet Detect(string code) => new RuntimeFeatureDetector().Detect(new Parser(new Lexer(code).ScanTokens()).ParseOrThrow());
    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName($"function_introspection_{Guid.NewGuid():N}"), typeof(object).Assembly);
    private static Assembly SaveVerifyLoad(PersistedAssemblyBuilder builder)
    {
        using var bytes = new MemoryStream(); builder.Save(bytes); bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes)); return Assembly.Load(bytes.ToArray());
    }
}

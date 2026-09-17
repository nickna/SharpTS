using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedFunctionValueRuntimeTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Type[] Owners = [typeof(EmittedFunctionValueRuntime), typeof(EmittedArgumentsRuntime)];
    public static IEnumerable<object[]> Handles => Owners.SelectMany(t => Metadata(t).Select(p => new object[] { t, p.Name }));

    [Theory]
    [MemberData(nameof(Handles))]
    public void HandlesRequireDeclarationAndFreezeAfterCompletion(Type type, string name)
    {
        var owner = Activator.CreateInstance(type, nonPublic: true)!;
        var property = type.GetProperty(name)!;
        Expect<InvalidOperationException>(() => property.GetValue(owner));
        Expect<ArgumentNullException>(() => property.SetValue(owner, null));
        var builder = NewAssembly(); var probe = builder.DefineDynamicModule("main").DefineType("Handles");
        object Handle(PropertyInfo p) => p.PropertyType == typeof(TypeBuilder) ? probe
            : p.PropertyType == typeof(FieldBuilder) ? probe.DefineField(p.Name, typeof(object), FieldAttributes.Public)
            : p.PropertyType == typeof(ConstructorBuilder) ? probe.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes)
            : probe.DefineMethod(p.Name, MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        foreach (var p in Metadata(type).Where(p => p.Name != name)) p.SetValue(owner, Handle(p));
        Expect<InvalidOperationException>(() => Complete(owner)); Assert.False(IsComplete(owner));
        var value = Handle(property); property.SetValue(owner, value); Assert.Same(value, property.GetValue(owner));
        Expect<InvalidOperationException>(() => property.SetValue(owner, value));
        Complete(owner); Assert.True(IsComplete(owner));
        Expect<InvalidOperationException>(() => Complete(owner));
        Expect<InvalidOperationException>(() => property.SetValue(owner, value));
        Assert.Same(value, property.GetValue(owner));
    }

    [Fact]
    public void ArgumentContextIsAvailableBeforeTheBrandedObjectAndCompletesLater()
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
        var emitter = new RuntimeEmitter(TypeProvider.Runtime); var owner = new EmittedArgumentsRuntime();
        typeof(RuntimeEmitter).GetMethod("EmitArgumentsContextClass", Members)!.Invoke(emitter, [module, owner]);
        var field = owner.CurrentField;
        Assert.Equal("$ArgumentsContext", field.DeclaringType!.Name);
        Assert.True(((TypeBuilder)field.DeclaringType).IsCreated());
        Assert.False(owner.IsComplete); Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
        emitter.EmitArgumentsTypeDefinition(module, owner); owner.CompleteEmission();
        Assert.Same(field, owner.CurrentField); Assert.True(owner.Type.IsCreated());
        var loaded = SaveVerifyLoad(builder);
        Assert.NotNull(loaded.GetType("$ArgumentsContext")!.GetField(field.Name)!.GetCustomAttribute<ThreadStaticAttribute>());
        AssertArguments(loaded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedEmissionHasFreshOwnersHandlesAndCaches(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        var owners = new HashSet<object>(); var caches = new HashSet<object>();
        foreach (string code in new[] { "const n=1;", "function f(...xs:number[]){return xs.length;}f(1,2);", "const n=2;" })
        {
            var builder = NewAssembly(); var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"), Detect(code));
            foreach (object owner in new object[] { runtime.FunctionValues, runtime.Arguments })
            {
                Assert.True(owners.Add(owner)); Assert.True(IsComplete(owner));
                foreach (var p in Metadata(owner.GetType()))
                {
                    var member = Assert.IsAssignableFrom<MemberInfo>(p.GetValue(owner));
                    Assert.Same(builder, member.Module.Assembly);
                    if (member is TypeBuilder type) Assert.True(type.IsCreated());
                }
            }
            var loaded = SaveVerifyLoad(builder); var function = loaded.GetType("$TSFunction")!;
            foreach (string name in new[] { "_instanceCache", "_prototypeCache", "_invokerCache", "_thisFieldCache" })
            {
                var cache = function.GetField(name, Members)!.GetValue(null)!;
                Assert.True(caches.Add(cache));
                Assert.All(((IEnumerable)cache).Cast<object>(), entry => Assert.Same(loaded, entry.GetType().GetProperty("Key")!.GetValue(entry) switch
                {
                    MethodInfo method => method.Module.Assembly,
                    Type type => type.Assembly,
                    object tuple => ((MethodInfo)tuple.GetType().GetField("Item1")!.GetValue(tuple)!).Module.Assembly,
                    _ => throw new InvalidOperationException("Missing cache key")
                }));
            }
            AssertArguments(loaded);
            Assert.DoesNotContain(loaded.GetReferencedAssemblies(), a => a.Name == "SharpTS");
            Assert.Equal(hosted, loaded.GetReferencedAssemblies().Any(a => a.Name == "SharpTS.Hosting.Abstractions"));
        }
    }

    [Fact]
    public void FunctionIdentityUsesMethodNameAndLengthWhileInvokersAreShared()
    {
        var (assembly, _) = EmitProbe(); var function = assembly.GetType("$TSFunction")!;
        var method = assembly.GetType("Probe")!.GetMethod("ReadThis")!;
        object Get(string name, int length) => function.GetMethod("GetOrCreate")!.Invoke(null, [method, name, length])!;
        var first = Get("first", 0); Assert.Same(first, Get("first", 0));
        var renamed = Get("alias", 0); var resized = Get("first", 2);
        Assert.NotSame(first, renamed); Assert.NotSame(first, resized);
        Assert.Equal("first", function.GetProperty("Name")!.GetValue(first));
        Assert.Equal("alias", function.GetProperty("Name")!.GetValue(renamed));
        Assert.Equal(2, function.GetProperty("Length")!.GetValue(resized));
        var invoker = function.GetField("_invoker", Members)!;
        Assert.Same(invoker.GetValue(first), invoker.GetValue(renamed));
        Assert.Same(invoker.GetValue(first), invoker.GetValue(resized));
        Assert.Same(method, function.GetMethod("GetMethodInfo")!.Invoke(first, null));
        var uncached = Activator.CreateInstance(function, new object?[] { null, method })!;
        Assert.NotSame(first, uncached); Assert.Same(invoker.GetValue(first), invoker.GetValue(uncached));
        var prototypes = Assert.IsAssignableFrom<IDictionary>(function.GetField("_prototypeCache")!.GetValue(null));
        var prototype = new object(); prototypes[method] = prototype; Assert.Same(prototype, prototypes[method]);
    }

    [Theory]
    [InlineData("ReadThis", false)]
    [InlineData("ReadThis", true)]
    [InlineData("Fail", false)]
    [InlineData("Fail", true)]
    [InlineData("Echo", false)]
    [InlineData("Echo", true)]
    public void InvocationPreservesReceiverAndRestoresAmbientState(string methodName, bool zeroArguments)
    {
        var (assembly, fieldName) = EmitProbe(); var function = assembly.GetType("$TSFunction")!;
        var method = assembly.GetType("Probe")!.GetMethod(methodName)!;
        var wrapper = Activator.CreateInstance(function, new object?[] { null, method })!;
        var current = function.GetField(fieldName)!; var previous = new object(); var receiver = new object(); current.SetValue(null, previous);
        var invoke = function.GetMethod(zeroArguments ? "InvokeWithThis0" : "InvokeWithThis")!;
        object? Call() => invoke.Invoke(wrapper, zeroArguments ? [receiver] : [receiver, Array.Empty<object>()]);
        if (methodName == "Fail") Expect<InvalidOperationException>(() => Call());
        else Assert.Same(receiver, Call());
        Assert.Same(previous, current.GetValue(null));
        Assert.NotNull(current.GetCustomAttribute<ThreadStaticAttribute>());
    }

    [Fact]
    public void OwnerAndHelperBoundariesAreExplicit()
    {
        var first = new EmittedRuntime(); var second = new EmittedRuntime();
        Assert.NotSame(first.FunctionValues, second.FunctionValues); Assert.NotSame(first.Arguments, second.Arguments);
        foreach (string name in new[] { "FunctionValues", "Arguments" }) Assert.Null(typeof(EmittedRuntime).GetProperty(name)!.SetMethod);
        foreach (string name in new[] { "EmitArgumentsContextClass", "EmitArgumentsTypeDefinition", "EmitTSFunctionClass", "EmitTSFunctionAdjustArgsHelper", "EmitTSFunctionConvertArgsHelper", "EmitTSFunctionCoercePrimitivesHelper", "EmitFunctionConstructor", "EmitTSFunctionInvokeWithThis0" })
        {
            var method = typeof(RuntimeEmitter).GetMethod(name, Members)!;
            Assert.DoesNotContain(method.GetParameters(), p => p.ParameterType == typeof(EmittedRuntime) || p.ParameterType == typeof(RuntimeFeatureSet));
            foreach (var p in method.GetParameters().Where(p => p.ParameterType.IsNestedPrivate))
                Assert.DoesNotContain(p.ParameterType.GetFields(Members), f => f.FieldType == typeof(EmittedRuntime) || f.FieldType == typeof(RuntimeFeatureSet));
        }
    }

    private static (Assembly Assembly, string Field) EmitProbe()
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
        var runtime = new RuntimeEmitter(TypeProvider.Runtime).EmitAll(module, Detect("const n=1;"));
        var probe = module.DefineType("Probe", TypeAttributes.Public);
        var read = probe.DefineMethod("ReadThis", MethodAttributes.Public | MethodAttributes.Static, typeof(object), Type.EmptyTypes);
        read.GetILGenerator().Emit(OpCodes.Ldsfld, runtime.FunctionValues.CurrentThisField); read.GetILGenerator().Emit(OpCodes.Ret);
        var fail = probe.DefineMethod("Fail", MethodAttributes.Public | MethodAttributes.Static, typeof(object), Type.EmptyTypes);
        fail.GetILGenerator().Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor(Type.EmptyTypes)!); fail.GetILGenerator().Emit(OpCodes.Throw);
        var echo = probe.DefineMethod("Echo", MethodAttributes.Public | MethodAttributes.Static, typeof(object), [typeof(object)]);
        echo.DefineParameter(1, ParameterAttributes.None, "__this"); echo.GetILGenerator().Emit(OpCodes.Ldarg_0); echo.GetILGenerator().Emit(OpCodes.Ret);
        probe.CreateType(); return (SaveVerifyLoad(builder), runtime.FunctionValues.CurrentThisField.Name);
    }

    private static void AssertArguments(Assembly loaded)
    {
        var type = loaded.GetType("$Arguments")!;
        var empty = Assert.IsAssignableFrom<IList>(Activator.CreateInstance(type));
        Assert.Empty(empty); Assert.Equal(0, type.GetField("_length")!.GetValue(empty));
        object[] source = [1.0, "two"];
        var copied = Assert.IsAssignableFrom<IList>(Activator.CreateInstance(type, [source]));
        Assert.Equal(source, copied.Cast<object>()); source[0] = 9.0; Assert.Equal(1.0, copied[0]);
        copied.Add(3.0); Assert.Equal(3, copied.Count); Assert.Equal(2, type.GetField("_length")!.GetValue(copied));
    }
    private static IEnumerable<PropertyInfo> Metadata(Type type) => type.GetProperties().Where(p => typeof(MemberInfo).IsAssignableFrom(p.PropertyType));
    private static bool IsComplete(object owner) => (bool)owner.GetType().GetProperty("IsComplete")!.GetValue(owner)!;
    private static void Complete(object owner) => owner.GetType().GetMethod("CompleteEmission", Members)!.Invoke(owner, null);
    private static void Expect<T>(Action action) where T : Exception => Assert.IsType<T>(Assert.Throws<TargetInvocationException>(action).InnerException);
    private static RuntimeFeatureSet Detect(string code) => new RuntimeFeatureDetector().Detect(new Parser(new Lexer(code).ScanTokens()).ParseOrThrow());
    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName($"function_values_{Guid.NewGuid():N}"), typeof(object).Assembly);
    private static Assembly SaveVerifyLoad(PersistedAssemblyBuilder builder)
    {
        using var bytes = new MemoryStream(); builder.Save(bytes); bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes)); return Assembly.Load(bytes.ToArray());
    }
}

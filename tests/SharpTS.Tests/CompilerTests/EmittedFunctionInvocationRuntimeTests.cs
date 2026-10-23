using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedFunctionInvocationRuntimeTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Type[] Owners = [typeof(EmittedFunctionValueRuntime), typeof(EmittedArgumentsRuntime), typeof(EmittedFunctionBindingRuntime)];
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
        foreach (string code in new[] { "const n=1;", "function f(...xs:number[]){return xs.length;} f.bind(null,1)(2); new Map(); new Set();", "const n=2;" })
        {
            var builder = NewAssembly(); var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"), Detect(code));
            foreach (object owner in new object[] { runtime.FunctionValues, runtime.Arguments, runtime.FunctionBindings })
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
        Assert.NotSame(first.FunctionValues, second.FunctionValues); Assert.NotSame(first.Arguments, second.Arguments); Assert.NotSame(first.FunctionBindings, second.FunctionBindings);
        foreach (string name in new[] { "FunctionValues", "Arguments", "FunctionBindings" }) Assert.Null(typeof(EmittedRuntime).GetProperty(name)!.SetMethod);
        foreach (string name in new[] { "EmitArgumentsContextClass", "EmitArgumentsTypeDefinition", "EmitTSFunctionClass", "EmitTSFunctionAdjustArgsHelper", "EmitTSFunctionConvertArgsHelper", "EmitTSFunctionCoercePrimitivesHelper", "EmitFunctionConstructor", "EmitTSFunctionInvokeWithThis0", "DefineBoundFunctionMetadata", "EmitBoundFunctionName", "EmitBoundFunctionPrototype", "EmitBoundTSFunctionClass", "EmitBoundAnyFunctionClass", "EmitFunctionBindWrapperClass", "EmitFunctionCallWrapperClass", "EmitFunctionApplyWrapperClass", "EmitDispatchToTarget" })
        {
            var method = typeof(RuntimeEmitter).GetMethod(name, Members)!;
            Assert.DoesNotContain(method.GetParameters(), p => p.ParameterType == typeof(EmittedRuntime) || p.ParameterType == typeof(RuntimeFeatureSet));
            foreach (var p in method.GetParameters().Where(p => p.ParameterType.IsNestedPrivate))
                Assert.DoesNotContain(p.ParameterType.GetFields(Members), f => f.FieldType == typeof(EmittedRuntime) || f.FieldType == typeof(RuntimeFeatureSet));
        }
    }

    [Fact]
    public void BoundFunctionsAreDeclaredBeforeTheRemainingWrappers()
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
        var function = module.DefineType("FunctionValue", TypeAttributes.Public);
        var invoke = function.DefineMethod("InvokeWithThis", MethodAttributes.Public, typeof(object), [typeof(object), typeof(object[])]);
        invoke.GetILGenerator().Emit(OpCodes.Ldarg_1); invoke.GetILGenerator().Emit(OpCodes.Ret); function.CreateType();
        var values = new EmittedFunctionValueRuntime { Type = function, InvokeWithThis = invoke };
        var captureType = module.DefineType("BindingName", TypeAttributes.Public);
        var captureName = captureType.DefineMethod("CaptureName", MethodAttributes.Public | MethodAttributes.Static,
            typeof(string), [typeof(object)]);
        captureName.GetILGenerator().Emit(OpCodes.Ldstr, "bound ");
        captureName.GetILGenerator().Emit(OpCodes.Ret);
        var capturePrototype = captureType.DefineMethod("CapturePrototype", MethodAttributes.Public | MethodAttributes.Static,
            typeof(void), [typeof(object), typeof(object)]);
        capturePrototype.GetILGenerator().Emit(OpCodes.Ret);
        captureType.CreateType();
        var bindings = new EmittedFunctionBindingRuntime { CaptureName = captureName, CapturePrototype = capturePrototype };
        typeof(RuntimeEmitter).GetMethod("EmitBoundTSFunctionClass", Members)!.Invoke(new RuntimeEmitter(TypeProvider.Runtime), [module, bindings, values]);
        Assert.True(bindings.BoundType.IsCreated()); Assert.NotNull(bindings.BoundInvoke); Assert.NotNull(bindings.BoundInvokeWithThis);
        Assert.False(bindings.IsComplete); Assert.Throws<InvalidOperationException>(bindings.CompleteEmission);
        Assert.Throws<InvalidOperationException>(() => bindings.AnyType); Assert.Throws<InvalidOperationException>(() => bindings.ApplyInvoke);
        var loaded = SaveVerifyLoad(builder); var receiver = new object();
        var target = Activator.CreateInstance(loaded.GetType("FunctionValue")!)!;
        var bound = Activator.CreateInstance(loaded.GetType("$BoundTSFunction")!, [target, receiver, Array.Empty<object>()])!;
        Assert.Same(receiver, bound.GetType().GetMethod("InvokeWithThis")!.Invoke(bound, [new object(), Array.Empty<object>()]));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void DispatchUsesSuppliedCollectionMetadataDespiteOppositeFeatureFlags(bool mapPresent, bool setPresent)
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
        (TypeBuilder Type, MethodBuilder Invoke) Callable(string name)
        {
            var type = module.DefineType(name, TypeAttributes.Public);
            var invoke = type.DefineMethod("Invoke", MethodAttributes.Public, typeof(object), [typeof(object[])]);
            invoke.GetILGenerator().Emit(OpCodes.Ldstr, name); invoke.GetILGenerator().Emit(OpCodes.Ret); type.CreateType();
            return (type, invoke);
        }
        var function = Callable("Function"); var bound = Callable("Bound"); var any = Callable("Any");
        var array = Callable("Array"); var map = Callable("Map"); var set = Callable("Set");
        var bindings = new EmittedFunctionBindingRuntime { BoundType = bound.Type, BoundInvoke = bound.Invoke, AnyType = any.Type, AnyInvoke = any.Invoke };
        var values = new EmittedFunctionValueRuntime { Type = function.Type, Invoke = function.Invoke };
        var arrays = new EmittedArrayOperationsRuntime { BoundMethodType = array.Type, BoundMethodInvoke = array.Invoke };
        var maps = mapPresent ? new EmittedMapRuntime { BoundMethodType = map.Type, BoundMethodInvoke = map.Invoke } : null;
        var sets = setPresent ? new EmittedSetRuntime { BoundMethodType = set.Type, BoundMethodInvoke = set.Invoke } : null;
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        typeof(RuntimeEmitter).GetField("_features", Members)!.SetValue(emitter, new RuntimeFeatureSet { UsesMap = !mapPresent, UsesSet = !setPresent });
        var inputs = typeof(RuntimeEmitter).GetNestedType("DispatchToTargetInputs", BindingFlags.NonPublic)!;
        var fallbackType = module.DefineType("Fallback", TypeAttributes.Public);
        var fallback = fallbackType.DefineMethod("InvokeMethodValue", MethodAttributes.Public | MethodAttributes.Static, typeof(object), [typeof(object), typeof(object), typeof(object[])]);
        fallback.GetILGenerator().Emit(OpCodes.Ldnull); fallback.GetILGenerator().Emit(OpCodes.Ret); fallbackType.CreateType();
        var invocation = new EmittedInvocationRuntime { Method = fallback };
        var peers = Activator.CreateInstance(inputs, Members, null, [values, arrays, maps, sets, invocation], null)!;
        var dispatcher = module.DefineType("Dispatch", TypeAttributes.Public);
        var targetField = dispatcher.DefineField("Target", typeof(object), FieldAttributes.Public);
        var method = dispatcher.DefineMethod("Invoke", MethodAttributes.Public, typeof(object), [typeof(object[])]);
        var il = method.GetILGenerator(); var args = il.DeclareLocal(typeof(object[]));
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stloc, args);
        typeof(RuntimeEmitter).GetMethod("EmitDispatchToTarget", Members)!.Invoke(emitter, [il, bindings, peers, targetField, args, null]);
        il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ret); dispatcher.CreateType();
        var loaded = SaveVerifyLoad(builder); var dispatchType = loaded.GetType("Dispatch")!; var instance = Activator.CreateInstance(dispatchType)!;
        foreach (var (name, present) in new[] { ("Function", true), ("Bound", true), ("Any", true), ("Array", true), ("Map", mapPresent), ("Set", setPresent) })
        {
            dispatchType.GetField("Target")!.SetValue(instance, Activator.CreateInstance(loaded.GetType(name)!));
            Assert.Equal(present ? name : null, dispatchType.GetMethod("Invoke")!.Invoke(instance, [Array.Empty<object>()]));
        }
    }

    [Theory]
    [InlineData("Invoke", "Void")]
    [InlineData("Invoke", "Null")]
    [InlineData("Invoke", "Number")]
    [InlineData("InvokeWithThis", "Void")]
    [InlineData("InvokeWithThis", "Null")]
    [InlineData("InvokeWithThis", "Number")]
    [InlineData("InvokeWithThis0", "Void")]
    [InlineData("InvokeWithThis0", "Null")]
    [InlineData("InvokeWithThis0", "Number")]
    public void InvocationDistinguishesClrVoidFromObjectNullAndNumericResults(
        string invocationStyle, string resultKind)
    {
        var (assembly, ambientField, undefinedType, undefinedField) = EmitReturnProbe();
        var function = assembly.GetType("$TSFunction")!;
        var probe = assembly.GetType("ReturnProbe")!;
        var method = probe.GetMethod("Return" + resultKind)!;
        // Exercise both constructor paths that cache the actual CLR return type.
        var wrapper = invocationStyle == "InvokeWithThis"
            ? Activator.CreateInstance(function, new object?[] { null, method, "completion", 0 })!
            : Activator.CreateInstance(function, new object?[] { null, method })!;
        var current = function.GetField(ambientField)!;
        var previous = new object();
        var receiver = new object();
        current.SetValue(null, previous);

        object? actual = invocationStyle switch
        {
            "Invoke" => function.GetMethod("Invoke")!.Invoke(wrapper, [new object[] { receiver }]),
            "InvokeWithThis" => function.GetMethod("InvokeWithThis")!.Invoke(wrapper, [receiver, Array.Empty<object>()]),
            "InvokeWithThis0" => function.GetMethod("InvokeWithThis0")!.Invoke(wrapper, [receiver]),
            _ => throw new InvalidOperationException("Unknown invocation style")
        };

        switch (resultKind)
        {
            case "Void":
                var undefined = assembly.GetType(undefinedType)!.GetField(undefinedField, Members)!.GetValue(null);
                Assert.NotNull(undefined);
                Assert.Same(undefined, actual);
                break;
            case "Null":
                Assert.Null(actual);
                break;
            case "Number":
                Assert.Equal(42d, Assert.IsType<double>(actual));
                break;
            default:
                throw new InvalidOperationException("Unknown result kind");
        }
        Assert.Equal(1, probe.GetField("InvocationCount")!.GetValue(null));
        Assert.Same(receiver, probe.GetField("LastReceiver")!.GetValue(null));
        Assert.Same(previous, current.GetValue(null));
    }

    private static (Assembly Assembly, string AmbientField, string UndefinedType, string UndefinedField) EmitReturnProbe()
    {
        var builder = NewAssembly();
        var module = builder.DefineDynamicModule("main");
        var runtime = new RuntimeEmitter(TypeProvider.Runtime).EmitAll(module, Detect("const n=1;"));
        var probe = module.DefineType("ReturnProbe", TypeAttributes.Public);
        var count = probe.DefineField("InvocationCount", typeof(int), FieldAttributes.Public | FieldAttributes.Static);
        var receiver = probe.DefineField("LastReceiver", typeof(object), FieldAttributes.Public | FieldAttributes.Static);
        void Target(string kind, Type returnType)
        {
            var target = probe.DefineMethod("Return" + kind,
                MethodAttributes.Public | MethodAttributes.Static, returnType, [typeof(object)]);
            target.DefineParameter(1, ParameterAttributes.None, "__this");
            var il = target.GetILGenerator();
            il.Emit(OpCodes.Ldsfld, count);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stsfld, count);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Stsfld, receiver);
            if (returnType == typeof(object)) il.Emit(OpCodes.Ldnull);
            else if (returnType == typeof(int)) il.Emit(OpCodes.Ldc_I4, 42);
            il.Emit(OpCodes.Ret);
        }
        Target("Void", typeof(void));
        Target("Null", typeof(object));
        Target("Number", typeof(int));
        probe.CreateType();
        var undefined = runtime.Sentinels.UndefinedInstance;
        return (SaveVerifyLoad(builder), runtime.FunctionValues.CurrentThisField.Name,
            undefined.DeclaringType!.FullName!, undefined.Name);
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

using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedObjectPrototypeRuntimeTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Type[] OwnerTypes = [typeof(EmittedObjectPrototypeRuntime), typeof(EmittedClassPrototypeRuntime)];
    private static readonly string[] Stages = ["MarkGetPrototypeOfBodyEmitted", "MarkPopulateBodyEmitted"];
    private static PropertyInfo[] Handles(Type type) => type.GetProperties().Where(p => typeof(MemberInfo).IsAssignableFrom(p.PropertyType)).ToArray();
    public static IEnumerable<object[]> Declarations => OwnerTypes.SelectMany(type => Handles(type).Select(p => new object[] { type, p.Name }));

    [Theory]
    [MemberData(nameof(Declarations))]
    public void MissingDeclarationIsRepairableAndCompletionFreezesMetadata(Type type, string missing)
    {
        var owner = Activator.CreateInstance(type, nonPublic: true)!; var property = type.GetProperty(missing)!;
        Expect<InvalidOperationException>(() => property.GetValue(owner));
        Expect<ArgumentNullException>(() => property.SetValue(owner, null));
        Fill(owner, missing); MarkBodies(owner);
        Expect<InvalidOperationException>(() => Invoke(owner, "CompleteEmission")); Assert.False(IsComplete(owner));
        var handle = Handle(property); property.SetValue(owner, handle); Assert.Same(handle, property.GetValue(owner));
        Expect<InvalidOperationException>(() => property.SetValue(owner, Handle(property)));
        Invoke(owner, "CompleteEmission"); AssertFrozen(owner);
    }

    [Theory]
    [InlineData("MarkGetPrototypeOfBodyEmitted")]
    [InlineData("MarkPopulateBodyEmitted")]
    public void ForwardDeclarationsRemainUsableUntilBothBodiesAreEmitted(string missing)
    {
        var owner = new EmittedObjectPrototypeRuntime(); Fill(owner);
        var lookup = owner.GetPrototypeOf; var populate = owner.Populate;
        foreach (var stage in Stages.Where(s => s != missing)) Invoke(owner, stage);
        Expect<InvalidOperationException>(() => Invoke(owner, "CompleteEmission")); Assert.False(owner.IsComplete);
        Assert.Same(lookup, owner.GetPrototypeOf); Assert.Same(populate, owner.Populate);
        Invoke(owner, missing); Expect<InvalidOperationException>(() => Invoke(owner, missing));
        owner.CompleteEmission(); AssertFrozen(owner);
        foreach (var stage in Stages) Expect<InvalidOperationException>(() => Invoke(owner, stage));
    }

    [Fact]
    public void RequiredOwnersBelongToOneCompilation()
    {
        var first = new EmittedRuntime(); var second = new EmittedRuntime();
        Assert.NotSame(first.ObjectPrototypes, second.ObjectPrototypes); Assert.NotSame(first.ClassPrototypes, second.ClassPrototypes);
        Assert.False(first.ObjectPrototypes.IsComplete); Assert.False(first.ClassPrototypes.IsComplete);
        Assert.Equal(10, Handles(typeof(EmittedObjectPrototypeRuntime)).Length); Assert.Equal(3, Handles(typeof(EmittedClassPrototypeRuntime)).Length);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.ObjectPrototypes))!.SetMethod);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.ClassPrototypes))!.SetMethod);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedEmissionPreservesPrototypeAndClassIdentityWithoutSharingGuestState(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted); var owners = new HashSet<object>(); var prototypes = new HashSet<object>();
        foreach (bool optional in new[] { false, true, false, true })
        {
            var builder = NewAssembly(); var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"), Detect(optional));
            foreach (object owner in new object[] { runtime.ObjectPrototypes, runtime.ClassPrototypes })
            {
                Assert.True(owners.Add(owner)); AssertFrozen(owner);
                foreach (var property in Handles(owner.GetType())) Assert.Same(builder, Assert.IsAssignableFrom<MemberInfo>(property.GetValue(owner)).Module.Assembly);
            }
            var loaded = SaveVerifyLoad(builder); var references = loaded.GetReferencedAssemblies().Select(r => r.Name).ToArray();
            Assert.DoesNotContain("SharpTS", references); Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
            Assert.DoesNotContain(references, name => name!.StartsWith("object_prototype_runtime_", StringComparison.Ordinal));
            var type = loaded.GetType("$Runtime")!;
            Assert.True(type.GetMethod("ObjectGetPrototypeOf")!.MetadataToken < type.GetMethod("IsPrototypeOfHelper")!.MetadataToken);
            Assert.True(type.GetMethod("_ObjectPrototypePopulate")!.MetadataToken < type.GetMethod("ObjectProtoToString")!.MetadataToken);
            var marker = loaded.GetType("$IClassPrototypeMarker")!; Assert.True(marker.IsInterface);
            Assert.True(loaded.GetType("$Undefined")!.MetadataToken < marker.MetadataToken && marker.MetadataToken < type.MetadataToken);
            var undefined = loaded.GetType("$Undefined")!.GetField("Instance")!.GetValue(null);
            var objectPrototype = Assert.IsType<Dictionary<string, object?>>(type.GetField("_objectPrototype")!.GetValue(null));
            Assert.True(prototypes.Add(objectPrototype)); Assert.Same(typeof(object), objectPrototype["constructor"]);
            objectPrototype["mutation"] = 3d; Call(type, "_ObjectPrototypePopulate"); Assert.Equal(3d, objectPrototype["mutation"]);
            var proto = new Dictionary<string, object?> { ["inherited"] = 7d };
            var target = Assert.IsType<Dictionary<string, object?>>(Call(type, "ObjectCreate", proto, undefined));
            Assert.Empty(target); Assert.Same(proto, Call(type, "ObjectGetPrototypeOf", target)); Assert.Equal(7d, Call(type, "GetProperty", target, "inherited"));
            Assert.Equal(true, Call(type, "IsPrototypeOfHelper", proto, target));
            var valueForm = Call(type, "ObjectCreateValueForm", proto, null); Assert.Same(proto, Call(type, "ObjectGetPrototypeOf", valueForm));
            var nullProto = Call(type, "ObjectCreate", null, undefined); Assert.Null(Call(type, "ObjectGetPrototypeOf", nullProto));
            var next = new Dictionary<string, object?>(); Assert.Same(target, Call(type, "ObjectSetPrototypeOf", target, next)); Assert.Same(next, Call(type, "ObjectGetPrototypeOf", target));
            var error = Assert.Throws<TargetInvocationException>(() => Call(type, "ObjectSetPrototypeOf", next, target));
            Assert.Equal(loaded.GetType("$TypeError"), Call(type, "WrapException", error.InnerException!)!.GetType());
            Assert.Equal("[object Null]", Call(type, "ObjectProtoToString", (object?)null));
            Assert.Equal("[object Undefined]", Call(type, "ObjectProtoToString", undefined)); Assert.Same(target, Call(type, "ObjectProtoValueOf", target));
            var baseProto = new Dictionary<string, object?>(); var derivedProto = new Dictionary<string, object?>();
            Call(type, "RegisterClassPrototype", typeof(NativeBase), baseProto, null);
            Call(type, "RegisterClassPrototype", typeof(NativeDerived), derivedProto, typeof(NativeBase));
            Assert.Same(baseProto, Call(type, "GetClassPrototype", typeof(NativeBase))); Assert.Same(derivedProto, Call(type, "GetClassPrototype", typeof(NativeDerived)));
            Assert.Same(baseProto, Call(type, "ObjectGetPrototypeOf", derivedProto)); Assert.Same(objectPrototype, Call(type, "ObjectGetPrototypeOf", baseProto));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PromiseAndCallbackPrototypesFollowSuppliedMetadata(bool selected, bool supplied)
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main"); var features = Detect(selected);
        var emitter = new RuntimeEmitter(TypeProvider.Runtime); var runtime = emitter.EmitAll(module, features);
        var probe = runtime.RuntimeType.DefineNestedType("PrototypePromiseProbe", TypeAttributes.NestedPublic);
        var promiseType = SimpleReceiver(probe, "SuppliedPromise"); var resolve = SimpleReceiver(probe, "SuppliedResolve"); var reject = SimpleReceiver(probe, "SuppliedReject");
        var store = probe.DefineField("Store", typeof(ConditionalWeakTable<object, object>), FieldAttributes.Public | FieldAttributes.Static);
        var prototype = probe.DefineField("Prototype", typeof(Dictionary<string, object>), FieldAttributes.Public | FieldAttributes.Static);
        var initialize = probe.DefineTypeInitializer().GetILGenerator();
        initialize.Emit(OpCodes.Newobj, typeof(ConditionalWeakTable<object, object>).GetConstructor(Type.EmptyTypes)!); initialize.Emit(OpCodes.Stsfld, store);
        initialize.Emit(OpCodes.Newobj, typeof(Dictionary<string, object>).GetConstructor(Type.EmptyTypes)!); initialize.Emit(OpCodes.Stsfld, prototype); initialize.Emit(OpCodes.Ret);
        var populate = probe.DefineMethod("Populate", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes); populate.GetILGenerator().Emit(OpCodes.Ret);
        var promise = supplied ? new EmittedPromiseRuntime { Type = promiseType, ResolveCallbackType = resolve, RejectCallbackType = reject, PrototypeField = prototype, PrototypePopulateMethod = populate } : null;
        var owner = new EmittedObjectPrototypeRuntime { Prototype = runtime.ObjectPrototypes.Prototype };
        typeof(RuntimeEmitter).GetMethod("DefineObjectGetPrototypeOfShell", Members)!.Invoke(emitter, [probe, owner]);
        var helper = typeof(RuntimeEmitter).GetMethod("EmitObjectGetPrototypeOf", Members)!;
        var inputType = helper.GetParameters().Single(p => p.ParameterType.IsNestedPrivate).ParameterType;
        var inputs = MakeInputs(inputType, runtime, new Dictionary<string, object?> { ["Promise"] = promise });
        helper.Invoke(emitter, [runtime.ClassPrototypes, owner, inputs, store]); probe.CreateType();
        var loaded = SaveVerifyLoad(builder); var saved = loaded.GetType(probe.FullName!)!; var getter = saved.GetMethod("ObjectGetPrototypeOf")!;
        var expectedPromise = supplied ? saved.GetField("Prototype")!.GetValue(null) : null;
        Assert.Same(expectedPromise, getter.Invoke(null, [Activator.CreateInstance(loaded.GetType(promiseType.FullName!)!)]));
        Assert.Same(expectedPromise, getter.Invoke(null, [Task.FromResult<object>(1d)]));
        var expectedFunction = supplied ? loaded.GetType("$Runtime")!.GetField(runtime.FunctionPrototypes.Prototype.Name)!.GetValue(null) : null;
        foreach (var receiver in new[] { resolve, reject }) Assert.Same(expectedFunction, getter.Invoke(null, [Activator.CreateInstance(loaded.GetType(receiver.FullName!)!)]));
        var operands = ReadTypeOperands(getter).Where(p => p.OpCode == OpCodes.Isinst).Select(p => p.Type).ToArray();
        foreach (var receiver in new[] { promiseType, resolve, reject }) Assert.Equal(supplied, operands.Contains(loaded.GetType(receiver.FullName!)!));
        Assert.Equal(supplied, operands.Contains(typeof(Task<object>)));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CallableProxyBrandFollowsExplicitSelection(bool globalFlag, bool selected)
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main"); var features = Detect(globalFlag);
        var emitter = new RuntimeEmitter(TypeProvider.Runtime); var runtime = emitter.EmitAll(module, features);
        var proxy = module.DefineType("SharpTS.Runtime.Types.SharpTSProxy", TypeAttributes.Public); proxy.DefineDefaultConstructor(MethodAttributes.Public);
        var calls = proxy.DefineField("Calls", typeof(int), FieldAttributes.Public | FieldAttributes.Static);
        var isCallable = proxy.DefineMethod("get_IsCallable", MethodAttributes.Public, typeof(bool), Type.EmptyTypes); var il = isCallable.GetILGenerator();
        il.Emit(OpCodes.Ldsfld, calls); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stsfld, calls); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ret); proxy.CreateType();
        var probe = runtime.RuntimeType.DefineNestedType("ProxyBrandProbe", TypeAttributes.NestedPublic);
        var getIndex = probe.DefineMethod("SuppliedGetIndex", MethodAttributes.Public | MethodAttributes.Static, typeof(object), [typeof(object), typeof(object)]);
        il = getIndex.GetILGenerator(); il.Emit(OpCodes.Ldsfld, runtime.Sentinels.UndefinedInstance); il.Emit(OpCodes.Ret);
        var helper = typeof(RuntimeEmitter).GetMethod("EmitObjectProtoToStringHelper", Members)!;
        var inputs = MakeInputs(helper.GetParameters()[1].ParameterType, runtime, new Dictionary<string, object?> { ["ProxySelected"] = selected, ["GetIndex"] = getIndex });
        var method = Assert.IsAssignableFrom<MethodBuilder>(helper.Invoke(emitter, [probe, inputs])); probe.CreateType();
        var loaded = SaveVerifyLoad(builder); var savedProxy = loaded.GetType(proxy.FullName!)!; var savedProbe = loaded.GetType(probe.FullName!)!;
        Assert.Equal(selected ? "[object Function]" : "[object Object]", Call(savedProbe, method.Name, Activator.CreateInstance(savedProxy)));
        Assert.Equal(selected ? 1 : 0, savedProxy.GetField("Calls")!.GetValue(null));
        Assert.DoesNotContain("SharpTS", loaded.GetReferencedAssemblies().Select(a => a.Name));
    }

    [Fact]
    public void ScopedHelpersDoNotRetainTheWholeRuntimeHolderOrFlatAliases()
    {
        foreach (var name in ScopedMethods)
        {
            var method = typeof(RuntimeEmitter).GetMethod(name, Members)!;
            Assert.DoesNotContain(method.GetParameters(), p => p.ParameterType == typeof(EmittedRuntime) || p.ParameterType == typeof(RuntimeFeatureSet));
            foreach (var parameter in method.GetParameters().Where(p => p.ParameterType.IsNestedPrivate))
                Assert.DoesNotContain(parameter.ParameterType.GetFields(Members), f => f.FieldType == typeof(EmittedRuntime) || f.FieldType == typeof(RuntimeFeatureSet));
        }
        foreach (var name in FormerHandles) Assert.Null(typeof(EmittedRuntime).GetProperty(name));
    }

    private static readonly string[] ScopedMethods = ["DefineObjectPrototypePopulateShell", "EmitObjectPrototypePopulate", "EmitIsPrototypeOfHelper", "EmitObjectProtoToStringHelper", "EmitObjectProtoValueOfHelper", "EmitObjectProtoToLocaleStringHelper", "EmitObjectCreate", "EmitObjectCreateValueForm", "DefineObjectGetPrototypeOfShell", "EmitObjectGetPrototypeOf", "EmitObjectSetPrototypeOf", "EmitClassPrototypeMarkerInterface", "EmitClassPrototypeSupport"];
    private static readonly string[] FormerHandles = ["ObjectPrototypeField", "ObjectPrototypePopulateMethod", "ObjectProtoToStringHelper", "ObjectProtoValueOfHelper", "ObjectProtoToLocaleStringHelper", "IsPrototypeOfHelperMethod", "ObjectCreate", "ObjectCreateValueForm", "ObjectGetPrototypeOf", "ObjectSetPrototypeOf", "ClassPrototypeMarkerType", "GetClassPrototypeMethod", "RegisterClassPrototypeMethod"];
    private static object MakeInputs(Type type, EmittedRuntime runtime, IReadOnlyDictionary<string, object?> overrides)
    {
        var ctor = Assert.Single(type.GetConstructors());
        return ctor.Invoke(ctor.GetParameters().Select(p => overrides.TryGetValue(p.Name!, out var value) ? value
            : p.Name == "GetProperty" ? runtime.ObjectRead.Property
            : (p.Name switch { "TSFunctionGetOrCreate" => runtime.FunctionConstruction.GetOrCreate, _ => (p.Name switch { "ArgumentsType" => runtime.Arguments.Type, "BoundAnyFunctionType" => runtime.FunctionBindings.AnyType, "BoundTSFunctionType" => runtime.FunctionBindings.BoundType, "FunctionApplyWrapperType" => runtime.FunctionBindings.ApplyType, "FunctionBindWrapperType" => runtime.FunctionBindings.BindType, "FunctionCallWrapperType" => runtime.FunctionBindings.CallType, "TSFunctionType" => runtime.FunctionValues.Type, _ => (p.Name switch { "FunctionPrototypeField" => runtime.FunctionPrototypes.Prototype, "FunctionPrototypePopulateMethod" => runtime.FunctionPrototypes.Populate, _ => (p.Name switch { "InvokeMethodUnwrapped" => runtime.ReflectedMethods.InvokeUnwrapped, _ => (p.Name switch { "InvokeValue" => runtime.Invocation.Value, "InvokeMethodValue" => runtime.Invocation.Method, "InvokeMethodValue0" => runtime.Invocation.Method0, _ => (p.Name! switch { "UndefinedType" => runtime.Sentinels.UndefinedType, "UndefinedInstance" => runtime.Sentinels.UndefinedInstance, _ => typeof(EmittedRuntime).GetProperty(p.Name!)!.GetValue(runtime) }) }) }) }) }) })).ToArray());
    }
    private static void Fill(object owner, string? missing = null)
    {
        foreach (var property in Handles(owner.GetType()).Where(p => p.Name != missing)) property.SetValue(owner, Handle(property));
    }
    private static object Handle(PropertyInfo property)
    {
        if (property.PropertyType == typeof(Type)) return typeof(object);
        var type = NewAssembly().DefineDynamicModule("main").DefineType("Declaration");
        return property.PropertyType == typeof(FieldBuilder) ? type.DefineField("Value", typeof(object), FieldAttributes.Public)
            : type.DefineMethod("Invoke", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
    }
    private static void MarkBodies(object owner)
    {
        if (owner is EmittedObjectPrototypeRuntime) foreach (var stage in Stages) Invoke(owner, stage);
    }
    private static bool IsComplete(object owner) => (bool)owner.GetType().GetProperty("IsComplete")!.GetValue(owner)!;
    private static object? Invoke(object owner, string method) => owner.GetType().GetMethod(method, Members)!.Invoke(owner, null);
    private static void AssertFrozen(object owner)
    {
        Assert.True(IsComplete(owner)); Expect<InvalidOperationException>(() => Invoke(owner, "CompleteEmission"));
        foreach (var property in Handles(owner.GetType()))
        {
            Assert.False(property.SetMethod!.IsPublic); Expect<InvalidOperationException>(() => property.SetValue(owner, property.GetValue(owner)));
        }
    }
    private static void Expect<T>(Action action) where T : Exception => Assert.IsType<T>(Assert.Throws<TargetInvocationException>(action).InnerException);
    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName($"object_prototype_runtime_{Guid.NewGuid():N}"), typeof(object).Assembly);
    private static RuntimeFeatureSet Detect(bool optional) => new RuntimeFeatureDetector().Detect(new Parser(new Lexer(optional
        ? "Promise.resolve(1); JSON.stringify({a:1}); new Date(0); /x/.test('x'); new Map(); new Set(); 1n; new Proxy({}, {});" : "const value = 1;").ScanTokens()).ParseOrThrow());
    private static TypeBuilder SimpleReceiver(TypeBuilder parent, string name)
    {
        var type = parent.DefineNestedType(name, TypeAttributes.NestedPublic); type.DefineDefaultConstructor(MethodAttributes.Public); type.CreateType(); return type;
    }
    private static object? Call(Type type, string name, params object?[] values) => type.GetMethod(name, StaticMembers)!.Invoke(null, values);
    private static Assembly SaveVerifyLoad(PersistedAssemblyBuilder builder)
    {
        using var bytes = new MemoryStream(); builder.Save(bytes); bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]); var errors = verifier.Verify(bytes);
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors)); return Assembly.Load(bytes.ToArray());
    }
    private static IEnumerable<(OpCode OpCode, Type Type)> ReadTypeOperands(MethodInfo method)
    {
        byte[] il = method.GetMethodBody()!.GetILAsByteArray()!;
        for (int offset = 0; offset < il.Length;)
        {
            byte first = il[offset++];
            short value = first == 0xfe ? unchecked((short)(0xfe00 | il[offset++])) : first;
            OpCode opCode = OpCodeByValue[value];
            if (opCode.OperandType == OperandType.InlineType)
                yield return (opCode, method.Module.ResolveType(BitConverter.ToInt32(il, offset)));
            offset += opCode.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI or OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineMethod or
                    OperandType.InlineSig or OperandType.InlineString or OperandType.InlineTok or OperandType.InlineType or OperandType.ShortInlineR => 4,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, offset),
                _ => throw new InvalidOperationException($"Unsupported IL operand type {opCode.OperandType}.")
            };
        }
    }

    private static readonly IReadOnlyDictionary<short, OpCode> OpCodeByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static).Where(field => field.FieldType == typeof(OpCode))
        .Select(field => (OpCode)field.GetValue(null)!).ToDictionary(opCode => opCode.Value);
    private class NativeBase { }
    private sealed class NativeDerived : NativeBase { }
}

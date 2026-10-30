using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private readonly record struct GeneratorPrototypeGraphInputs(
        EmittedDescriptorStorageRuntime Descriptors,
        EmittedObjectStorageRuntime Objects,
        EmittedFunctionValueRuntime Functions,
        EmittedFunctionConstructionRuntime Construction,
        EmittedFunctionAttributesRuntime Attributes,
        EmittedFunctionPrototypeRuntime FunctionPrototypes,
        EmittedObjectPrototypeRuntime ObjectPrototypes,
        EmittedSymbolRuntime Symbols);

    private readonly record struct GeneratorPrototypeProtocolInputs(
        EmittedGeneratorRuntime Sync,
        EmittedAsyncGeneratorRuntime? Async,
        EmittedErrorRuntime Errors,
        MethodInfo? CoerceAwaitableToTask);

    private readonly record struct GeneratorPrototypeAccessInputs(
        EmittedDescriptorStorageRuntime Descriptors,
        EmittedObjectStorageRuntime Objects,
        EmittedFunctionValueRuntime Functions,
        EmittedFunctionConstructionRuntime Construction,
        MethodInfo GetPrototypeOf,
        MethodInfo ReflectGet,
        MethodInfo InvokeMethod,
        Type SymbolType,
        Type UndefinedType,
        FieldInfo UndefinedInstance);

    private void EmitGeneratorPrototypeBodies(EmittedGeneratorPrototypeRuntime prototypes,
        GeneratorPrototypeGraphInputs graph, GeneratorPrototypeProtocolInputs protocol,
        GeneratorPrototypeAccessInputs access)
    {
        EmitGeneratorPrototypeKind(prototypes);
        EmitGeneratorPrototypeGraph(prototypes, graph);
        EmitGeneratorPrototypeProtocol(prototypes, prototypes.Sync, protocol.Sync.Type,
            protocol.Sync.Next, protocol.Sync.Return, protocol.Sync.Throw, false, graph.Attributes, protocol.Errors);
        EmitEmptyGeneratorConstructor(prototypes, prototypes.Sync, protocol.Sync.Type,
            protocol.Sync.Next, protocol.Sync.Return, protocol.Sync.Throw, false, graph, protocol.Errors, access.UndefinedInstance, null);
        if (prototypes.Async != null)
        {
            var asynchronous = protocol.Async ?? throw new InvalidOperationException("Async generator protocol is required for its prototypes.");
            EmitGeneratorPrototypeProtocol(prototypes, prototypes.RequireAsync(), asynchronous.Type,
                asynchronous.Next, asynchronous.Return, asynchronous.Throw, true, graph.Attributes, protocol.Errors);
            EmitEmptyGeneratorConstructor(prototypes, prototypes.RequireAsync(), asynchronous.Type,
                asynchronous.Next, asynchronous.Return, asynchronous.Throw, true, graph, protocol.Errors, access.UndefinedInstance,
                protocol.CoerceAwaitableToTask ?? throw new InvalidOperationException("Async generator constructors require await coercion."));
        }
        EmitGeneratorPrototypeAccess(prototypes, access);
    }

    private void EmitGeneratorPrototypeKind(EmittedGeneratorPrototypeRuntime prototypes)
    {
        // Method metadata is immutable. Keep reflection out of repeated iterator
        // creation and property reads after classifying an output-owned method.
        var cacheType = _types.MakeGenericType(_types.ConcurrentDictionaryOpen, _types.MethodInfo, _types.Int32);
        var cache = prototypes.Type.DefineField("_kinds", cacheType,
            FieldAttributes.Private | FieldAttributes.Static | FieldAttributes.InitOnly);
        var initialize = prototypes.Type.DefineTypeInitializer().GetILGenerator();
        initialize.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(cacheType));
        initialize.Emit(OpCodes.Stsfld, cache); initialize.Emit(OpCodes.Ret);

        var il = prototypes.Kind.GetILGenerator();
        var ordinary = il.DefineLabel();
        var classify = il.DefineLabel();
        var publish = il.DefineLabel();
        var owner = il.DeclareLocal(_types.Type);
        var result = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Brfalse, ordinary);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(typeof(MemberInfo), "DeclaringType")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, owner);
        il.Emit(OpCodes.Ldloc, owner); il.Emit(OpCodes.Brfalse, ordinary);
        il.Emit(OpCodes.Ldloc, owner);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "Assembly")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldtoken, prototypes.Type); il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "Assembly")!.GetGetMethod()!);
        il.Emit(OpCodes.Bne_Un, ordinary);
        il.Emit(OpCodes.Ldsfld, cache); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloca, result);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(cacheType, "TryGetValue", _types.MethodInfo, _types.Int32.MakeByRefType()));
        il.Emit(OpCodes.Brfalse, classify); il.Emit(OpCodes.Ldloc, result); il.Emit(OpCodes.Ret);
        il.MarkLabel(classify);
        if (prototypes.Async != null) EmitMarker(typeof(AsyncIteratorStateMachineAttribute), 2);
        EmitMarker(typeof(IteratorStateMachineAttribute), 1);
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, result);
        il.MarkLabel(publish);
        il.Emit(OpCodes.Ldsfld, cache); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(cacheType, "GetOrAdd", _types.MethodInfo, _types.Int32));
        il.Emit(OpCodes.Ret);
        il.MarkLabel(ordinary); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret);
        prototypes.MarkBodyEmitted(prototypes.Kind);

        void EmitMarker(Type marker, int kind)
        {
            var next = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldtoken, marker);
            il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle); il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "IsDefined", _types.Type, _types.Boolean));
            il.Emit(OpCodes.Brfalse, next); il.Emit(OpCodes.Ldc_I4, kind);
            il.Emit(OpCodes.Stloc, result); il.Emit(OpCodes.Br, publish);
            il.MarkLabel(next);
        }
    }

    private void EmitGeneratorPrototypeGraph(EmittedGeneratorPrototypeRuntime prototypes, GeneratorPrototypeGraphInputs inputs)
    {
        var initialized = prototypes.Type.DefineField("_initialized", _types.Boolean, FieldAttributes.Private | FieldAttributes.Static);
        var il = prototypes.Populate.GetILGenerator();
        var populate = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, initialized); il.Emit(OpCodes.Brfalse, populate); il.Emit(OpCodes.Ret);
        il.MarkLabel(populate);
        il.Emit(OpCodes.Call, inputs.FunctionPrototypes.Populate);
        var kinds = prototypes.Async is { } asynchronous
            ? new[] { (prototypes.Sync, false), (asynchronous, true) }
            : new[] { (prototypes.Sync, false) };
        foreach (var (kind, _) in kinds)
        {
            foreach (var field in new[] { kind.FunctionPrototype, kind.GeneratorPrototype, kind.IteratorPrototype })
            {
                il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
                il.Emit(OpCodes.Newobj, inputs.Objects.Constructor); il.Emit(OpCodes.Stsfld, field);
            }
            _types.EmitLoadMethodInfo(il, kind.ConstructorMethod);
            il.Emit(OpCodes.Ldstr, kind == prototypes.Sync ? "GeneratorFunction" : "AsyncGeneratorFunction");
            il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Call, inputs.Construction.GetOrCreate);
            il.Emit(OpCodes.Stsfld, kind.Constructor);
        }
        // Publish every node before cyclic constructor/prototype edges are installed.
        il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Stsfld, initialized);
        var descriptor = il.DeclareLocal(inputs.Descriptors.DescriptorType);
        foreach (var (kind, isAsync) in kinds)
        {
            Parent(kind.FunctionPrototype, () => il.Emit(OpCodes.Ldsfld, inputs.FunctionPrototypes.Prototype));
            Parent(kind.GeneratorPrototype, () => il.Emit(OpCodes.Ldsfld, kind.IteratorPrototype));
            Parent(kind.IteratorPrototype, () => il.Emit(OpCodes.Ldsfld, inputs.ObjectPrototypes.Prototype));
            Parent(kind.Constructor, () => EmitRuntimeConstructorValue(il, inputs.Functions.Type));
            Named(kind.FunctionPrototype, "prototype", () => il.Emit(OpCodes.Ldsfld, kind.GeneratorPrototype), false, true);
            Named(kind.FunctionPrototype, "constructor", () => il.Emit(OpCodes.Ldsfld, kind.Constructor), false, true);
            Named(kind.GeneratorPrototype, "constructor", () => il.Emit(OpCodes.Ldsfld, kind.FunctionPrototype), false, true);
            Named(kind.Constructor, "prototype", () => il.Emit(OpCodes.Ldsfld, kind.FunctionPrototype), false, false);
            Symbol(kind.FunctionPrototype, inputs.Symbols.ToStringTag,
                () => il.Emit(OpCodes.Ldstr, isAsync ? "AsyncGeneratorFunction" : "GeneratorFunction"), false);
            Symbol(kind.GeneratorPrototype, inputs.Symbols.ToStringTag,
                () => il.Emit(OpCodes.Ldstr, isAsync ? "AsyncGenerator" : "Generator"), false);
            foreach (var (name, method) in new[] { ("next", kind.Next), ("return", kind.Return), ("throw", kind.Throw) })
                Named(kind.GeneratorPrototype, name, () => Callable(method, name, 1), true, true);
            Symbol(kind.IteratorPrototype, isAsync ? inputs.Symbols.AsyncIterator : inputs.Symbols.Iterator,
                () => Callable(kind.Identity, isAsync ? "[Symbol.asyncIterator]" : "[Symbol.iterator]", 0), true);
        }
        il.Emit(OpCodes.Ret); prototypes.MarkBodyEmitted(prototypes.Populate);

        var functionIl = prototypes.FunctionPrototype.GetILGenerator();
        functionIl.Emit(OpCodes.Call, prototypes.Populate);
        if (prototypes.Async != null)
        {
            var sync = functionIl.DefineLabel();
            functionIl.Emit(OpCodes.Ldarg_0); functionIl.Emit(OpCodes.Ldc_I4_2); functionIl.Emit(OpCodes.Bne_Un, sync);
            functionIl.Emit(OpCodes.Ldsfld, prototypes.RequireAsync().FunctionPrototype); functionIl.Emit(OpCodes.Ret);
            functionIl.MarkLabel(sync);
        }
        functionIl.Emit(OpCodes.Ldsfld, prototypes.Sync.FunctionPrototype); functionIl.Emit(OpCodes.Ret);
        prototypes.MarkBodyEmitted(prototypes.FunctionPrototype);

        void Parent(FieldInfo field, Action value)
        {
            il.Emit(OpCodes.Ldsfld, field); value(); il.Emit(OpCodes.Call, inputs.Descriptors.SetPrototype);
        }
        void Callable(MethodInfo method, string name, int length)
        {
            _types.EmitLoadMethodInfo(il, method); il.Emit(OpCodes.Ldstr, name); il.Emit(OpCodes.Ldc_I4, length);
            il.Emit(OpCodes.Call, inputs.Construction.GetOrCreate);
        }
        void Descriptor(Action value, bool writable, bool configurable)
        {
            il.Emit(OpCodes.Newobj, inputs.Descriptors.DescriptorConstructor); il.Emit(OpCodes.Stloc, descriptor);
            il.Emit(OpCodes.Ldloc, descriptor); value();
            il.Emit(OpCodes.Callvirt, inputs.Descriptors.DescriptorValue.GetSetMethod()!);
            foreach (var (property, flag) in new[] { (inputs.Descriptors.DescriptorWritable, writable),
                         (inputs.Descriptors.DescriptorEnumerable, false), (inputs.Descriptors.DescriptorConfigurable, configurable) })
            {
                il.Emit(OpCodes.Ldloc, descriptor); il.Emit(flag ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Callvirt, property.GetSetMethod()!);
            }
        }
        void Named(FieldInfo field, string name, Action value, bool writable, bool configurable)
        {
            Descriptor(value, writable, configurable);
            il.Emit(OpCodes.Ldsfld, field); il.Emit(OpCodes.Ldstr, name); il.Emit(OpCodes.Ldloc, descriptor);
            il.Emit(OpCodes.Call, inputs.Descriptors.DefineProperty); il.Emit(OpCodes.Pop);
        }
        void Symbol(FieldInfo field, FieldInfo symbol, Action value, bool writable)
        {
            Descriptor(value, writable, true);
            il.Emit(OpCodes.Ldsfld, field); il.Emit(OpCodes.Call, inputs.Symbols.GetStorage);
            il.Emit(OpCodes.Ldsfld, symbol); il.Emit(OpCodes.Ldloc, descriptor);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "set_Item", _types.Object, _types.Object));
        }
    }

    private void EmitGeneratorPrototypeProtocol(EmittedGeneratorPrototypeRuntime prototypes,
        EmittedGeneratorPrototypeKindRuntime kind, Type generatorType, MethodInfo next, MethodInfo @return, MethodInfo @throw,
        bool isAsync, EmittedFunctionAttributesRuntime attributes, EmittedErrorRuntime errors)
    {
        foreach (var (helper, target) in new[] { (kind.Next, next), (kind.Return, @return), (kind.Throw, @throw) })
        {
            MarkProtocol(helper);
            var il = helper.GetILGenerator();
            var valid = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Isinst, generatorType); il.Emit(OpCodes.Brtrue, valid);
            if (isAsync)
            {
                il.Emit(OpCodes.Ldstr, "Async generator method called on incompatible receiver");
                il.Emit(OpCodes.Newobj, errors.TypeErrorConstructor); il.Emit(OpCodes.Call, errors.CreateException);
                var fromException = _types.GetMethods(_types.Task).Single(method => method.Name == "FromException" && method.IsGenericMethodDefinition);
                il.Emit(OpCodes.Call, EmitGenerics.MakeGenericMethod(fromException, _types.Object)); il.Emit(OpCodes.Ret);
            }
            else GuestErrorEmitter.ThrowError(il, errors.CreateException, errors.TypeErrorConstructor, "Generator method called on incompatible receiver");
            il.MarkLabel(valid); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, generatorType);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Callvirt, target); il.Emit(OpCodes.Ret);
            prototypes.MarkBodyEmitted(helper);
        }
        MarkProtocol(kind.Identity);
        var identity = kind.Identity.GetILGenerator(); identity.Emit(OpCodes.Ldarg_0); identity.Emit(OpCodes.Ret);
        prototypes.MarkBodyEmitted(kind.Identity);
        void MarkProtocol(MethodBuilder method)
        {
            method.SetCustomAttribute(attributes.ExpectsThisCtor, CustomAttributeEncoder.EmptyBlob);
            method.SetCustomAttribute(attributes.NonConstructibleCtor, CustomAttributeEncoder.EmptyBlob);
            method.SetCustomAttribute(attributes.PadUndefinedCtor, CustomAttributeEncoder.EmptyBlob);
        }
    }
}

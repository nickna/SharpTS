using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the $PropertyDescriptorStore class and supporting types into the compiled assembly.
/// This makes compiled DLLs fully standalone without any runtime dependency on SharpTS.dll.
/// </summary>
public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits all property descriptor types: $FrozenSealedState, $PrototypeInfo,
    /// $CompiledPropertyDescriptor, and $PropertyDescriptorStore.
    /// </summary>
    private void EmitPropertyDescriptorTypes(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Phase 1: Define all types first (for cross-references)
        EmitFrozenSealedStateClass(moduleBuilder, runtime);
        EmitPrototypeInfoClass(moduleBuilder, runtime);
        EmitCompiledPropertyDescriptorClass(moduleBuilder, runtime);

        // Phase 2: Define $PropertyDescriptorStore (references the above types)
        EmitPropertyDescriptorStoreClass(moduleBuilder, runtime);
    }

    /// <summary>
    /// Emits: internal class $FrozenSealedState { bool IsFrozen, IsSealed, IsExtensible = true }
    /// </summary>
    private void EmitFrozenSealedStateClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$FrozenSealedState",
            TypeAttributes.NotPublic | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
            _types.Object
        );

        // Fields
        var isFrozenField = typeBuilder.DefineField("_isFrozen", _types.Boolean, FieldAttributes.Private);
        var isSealedField = typeBuilder.DefineField("_isSealed", _types.Boolean, FieldAttributes.Private);
        var isExtensibleField = typeBuilder.DefineField("_isExtensible", _types.Boolean, FieldAttributes.Private);

        // Constructor - sets IsExtensible = true by default
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            Type.EmptyTypes
        );
        var ctorIl = ctor.GetILGenerator();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, _types.GetConstructor(_types.Object, Type.EmptyTypes)!);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldc_I4_1); // true
        ctorIl.Emit(OpCodes.Stfld, isExtensibleField);
        ctorIl.Emit(OpCodes.Ret);

        // Property: IsFrozen
        EmitAutoProperty(typeBuilder, "IsFrozen", _types.Boolean, isFrozenField);

        // Property: IsSealed
        EmitAutoProperty(typeBuilder, "IsSealed", _types.Boolean, isSealedField);

        // Property: IsExtensible
        EmitAutoProperty(typeBuilder, "IsExtensible", _types.Boolean, isExtensibleField);

        var type = typeBuilder.CreateType()!;
        runtime.FrozenSealedStateType = type;
        _ = ctor;
        runtime.FrozenSealedStateIsFrozen = type.GetProperty("IsFrozen")!;
        runtime.FrozenSealedStateIsSealed = type.GetProperty("IsSealed")!;
        runtime.FrozenSealedStateIsExtensible = type.GetProperty("IsExtensible")!;
    }

    /// <summary>
    /// Emits: internal class $PrototypeInfo { object? Prototype }
    /// </summary>
    private void EmitPrototypeInfoClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$PrototypeInfo",
            TypeAttributes.NotPublic | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
            _types.Object
        );

        // Field
        var prototypeField = typeBuilder.DefineField("_prototype", _types.Object, FieldAttributes.Private);

        // Constructor
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            Type.EmptyTypes
        );
        var ctorIl = ctor.GetILGenerator();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, _types.GetConstructor(_types.Object, Type.EmptyTypes)!);
        ctorIl.Emit(OpCodes.Ret);

        // Property: Prototype
        EmitAutoProperty(typeBuilder, "Prototype", _types.Object, prototypeField);

        var type = typeBuilder.CreateType()!;
        runtime.PrototypeInfoType = type;
        _ = ctor;
        runtime.PrototypeInfoPrototype = type.GetProperty("Prototype")!;
    }

    /// <summary>
    /// Emits: public class $CompiledPropertyDescriptor { Value, Getter, Setter, Writable, Enumerable, Configurable }
    /// </summary>
    private void EmitCompiledPropertyDescriptorClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$CompiledPropertyDescriptor",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
            _types.Object
        );

        // Fields
        var valueField = typeBuilder.DefineField("_value", _types.Object, FieldAttributes.Private);
        var getterField = typeBuilder.DefineField("_getter", _types.Object, FieldAttributes.Private);
        var setterField = typeBuilder.DefineField("_setter", _types.Object, FieldAttributes.Private);
        var writableField = typeBuilder.DefineField("_writable", _types.Boolean, FieldAttributes.Private);
        var enumerableField = typeBuilder.DefineField("_enumerable", _types.Boolean, FieldAttributes.Private);
        var configurableField = typeBuilder.DefineField("_configurable", _types.Boolean, FieldAttributes.Private);

        // Constructor - sets defaults: Writable=true, Enumerable=true, Configurable=true
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            Type.EmptyTypes
        );
        var ctorIl = ctor.GetILGenerator();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, _types.GetConstructor(_types.Object, Type.EmptyTypes)!);
        // Writable = true
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldc_I4_1);
        ctorIl.Emit(OpCodes.Stfld, writableField);
        // Enumerable = true
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldc_I4_1);
        ctorIl.Emit(OpCodes.Stfld, enumerableField);
        // Configurable = true
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldc_I4_1);
        ctorIl.Emit(OpCodes.Stfld, configurableField);
        ctorIl.Emit(OpCodes.Ret);

        // Properties
        EmitAutoProperty(typeBuilder, "Value", _types.Object, valueField);
        EmitAutoProperty(typeBuilder, "Getter", _types.Object, getterField);
        EmitAutoProperty(typeBuilder, "Setter", _types.Object, setterField);
        EmitAutoProperty(typeBuilder, "Writable", _types.Boolean, writableField);
        EmitAutoProperty(typeBuilder, "Enumerable", _types.Boolean, enumerableField);
        EmitAutoProperty(typeBuilder, "Configurable", _types.Boolean, configurableField);

        var type = typeBuilder.CreateType()!;
        runtime.CompiledPropertyDescriptorType = type;
        runtime.CompiledPropertyDescriptorCtor = ctor;
        runtime.CompiledPropertyDescriptorValue = type.GetProperty("Value")!;
        runtime.CompiledPropertyDescriptorGetter = type.GetProperty("Getter")!;
        runtime.CompiledPropertyDescriptorSetter = type.GetProperty("Setter")!;
        runtime.CompiledPropertyDescriptorWritable = type.GetProperty("Writable")!;
        runtime.CompiledPropertyDescriptorEnumerable = type.GetProperty("Enumerable")!;
        runtime.CompiledPropertyDescriptorConfigurable = type.GetProperty("Configurable")!;
    }

    /// <summary>
    /// Emits: public static class $PropertyDescriptorStore
    /// with ConditionalWeakTable fields and all methods.
    /// </summary>
    private void EmitPropertyDescriptorStoreClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$PropertyDescriptorStore",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );

        // Get ConditionalWeakTable types
        var cwtDescriptors = _types.MakeGenericType(typeof(ConditionalWeakTable<,>),
            _types.Object,
            _types.MakeGenericType(typeof(Dictionary<,>), _types.String, runtime.CompiledPropertyDescriptorType)
        );
        var cwtFrozenSealed = _types.MakeGenericType(typeof(ConditionalWeakTable<,>), _types.Object, runtime.FrozenSealedStateType);
        var cwtSymbols = _types.MakeGenericType(typeof(ConditionalWeakTable<,>),
            _types.Object,
            _types.MakeGenericType(typeof(Dictionary<,>), _types.Object, _types.Object)
        );
        var cwtPrototype = _types.MakeGenericType(typeof(ConditionalWeakTable<,>), _types.Object, runtime.PrototypeInfoType);

        // Static fields
        var descriptorsField = typeBuilder.DefineField(
            "_descriptors",
            cwtDescriptors,
            FieldAttributes.Private | FieldAttributes.Static | FieldAttributes.InitOnly
        );
        var frozenSealedField = typeBuilder.DefineField(
            "_frozenSealedState",
            cwtFrozenSealed,
            FieldAttributes.Private | FieldAttributes.Static | FieldAttributes.InitOnly
        );
        var symbolStorageField = typeBuilder.DefineField(
            "_symbolStorage",
            cwtSymbols,
            FieldAttributes.Private | FieldAttributes.Static | FieldAttributes.InitOnly
        );
        var prototypeStoreField = typeBuilder.DefineField(
            "_prototypeStore",
            cwtPrototype,
            FieldAttributes.Private | FieldAttributes.Static | FieldAttributes.InitOnly
        );

        // Static constructor to initialize fields
        var cctor = typeBuilder.DefineTypeInitializer();
        var cctorIl = cctor.GetILGenerator();

        // Get the open generic constructor for ConditionalWeakTable<,>
        var cwtOpenCtor = typeof(ConditionalWeakTable<,>).GetConstructor(Type.EmptyTypes)!;

        // Use TypeBuilder.GetConstructor for generic types containing TypeBuilder-created types
        // _descriptors = new ConditionalWeakTable<...>()
        cctorIl.Emit(OpCodes.Newobj, EmitterTypeHelpers.ResolveConstructor(cwtDescriptors, cwtOpenCtor));
        cctorIl.Emit(OpCodes.Stsfld, descriptorsField);

        // _frozenSealedState = new ConditionalWeakTable<...>()
        cctorIl.Emit(OpCodes.Newobj, EmitterTypeHelpers.ResolveConstructor(cwtFrozenSealed, cwtOpenCtor));
        cctorIl.Emit(OpCodes.Stsfld, frozenSealedField);

        // _symbolStorage = new ConditionalWeakTable<...>()
        cctorIl.Emit(OpCodes.Newobj, cwtSymbols.GetConstructor(Type.EmptyTypes)!);
        cctorIl.Emit(OpCodes.Stsfld, symbolStorageField);

        // _prototypeStore = new ConditionalWeakTable<...>()
        cctorIl.Emit(OpCodes.Newobj, EmitterTypeHelpers.ResolveConstructor(cwtPrototype, cwtOpenCtor));
        cctorIl.Emit(OpCodes.Stsfld, prototypeStoreField);

        cctorIl.Emit(OpCodes.Ret);

        // Store field references for method emission
        _ = descriptorsField;
        _ = frozenSealedField;
        _ = symbolStorageField;
        _ = prototypeStoreField;

        // Get methods from open generic types for use with TypeBuilder.GetMethod
        var cwtOpenType = typeof(ConditionalWeakTable<,>);
        var cwtGetOrCreateValue = cwtOpenType.GetMethod("GetOrCreateValue")!;
        var cwtTryGetValue = cwtOpenType.GetMethod("TryGetValue")!;

        // Get closed methods for each ConditionalWeakTable type
        var frozenSealedGetOrCreate = EmitterTypeHelpers.ResolveMethod(cwtFrozenSealed, cwtGetOrCreateValue);
        var frozenSealedTryGet = EmitterTypeHelpers.ResolveMethod(cwtFrozenSealed, cwtTryGetValue);
        var prototypeGetOrCreate = EmitterTypeHelpers.ResolveMethod(cwtPrototype, cwtGetOrCreateValue);
        var prototypeTryGet = EmitterTypeHelpers.ResolveMethod(cwtPrototype, cwtTryGetValue);
        var descriptorsTryGet = EmitterTypeHelpers.ResolveMethod(cwtDescriptors, cwtTryGetValue);

        // Get Dictionary<string, CompiledPropertyDescriptor> type and methods
        // Must use TypeBuilder.GetMethod since CompiledPropertyDescriptorType is TypeBuilder-created
        var descriptorsDictType = _types.MakeGenericType(typeof(Dictionary<,>), _types.String, runtime.CompiledPropertyDescriptorType);
        var dictOpenType = typeof(Dictionary<,>);
        var dictOpenContainsKey = dictOpenType.GetMethod("ContainsKey")!;
        var dictOpenTryGetValue = dictOpenType.GetMethod("TryGetValue")!;
        var dictOpenSetItem = dictOpenType.GetMethod("set_Item")!;
        var descriptorsDictContainsKey = EmitterTypeHelpers.ResolveMethod(descriptorsDictType, dictOpenContainsKey);
        var descriptorsDictTryGetValue = EmitterTypeHelpers.ResolveMethod(descriptorsDictType, dictOpenTryGetValue);
        var descriptorsDictSetItem = EmitterTypeHelpers.ResolveMethod(descriptorsDictType, dictOpenSetItem);

        // Get _descriptors.GetOrCreateValue method for DefineProperty
        var descriptorsGetOrCreate = EmitterTypeHelpers.ResolveMethod(cwtDescriptors, cwtGetOrCreateValue);

        // Emit all methods
        EmitPDSFreeze(typeBuilder, runtime, frozenSealedField, frozenSealedGetOrCreate);
        EmitPDSSeal(typeBuilder, runtime, frozenSealedField, frozenSealedGetOrCreate);
        EmitPDSPreventExtensions(typeBuilder, runtime, frozenSealedField, frozenSealedGetOrCreate);
        EmitPDSIsExtensible(typeBuilder, runtime, frozenSealedField, frozenSealedTryGet);
        EmitPDSIsFrozen(typeBuilder, runtime, frozenSealedField, frozenSealedTryGet);
        EmitPDSIsSealed(typeBuilder, runtime, frozenSealedField, frozenSealedTryGet);
        EmitPDSCanAddProperty(typeBuilder, runtime, frozenSealedField, frozenSealedTryGet, descriptorsField, descriptorsTryGet, descriptorsDictType, descriptorsDictContainsKey, descriptorsDictTryGetValue);
        EmitPDSTryGetGetter(typeBuilder, runtime, descriptorsField, descriptorsTryGet, descriptorsDictType, descriptorsDictTryGetValue);
        EmitPDSTryGetSetter(typeBuilder, runtime, descriptorsField, descriptorsTryGet, descriptorsDictType, descriptorsDictTryGetValue);
        EmitPDSIsWritable(typeBuilder, runtime, frozenSealedField, frozenSealedTryGet, descriptorsField, descriptorsTryGet, descriptorsDictType, descriptorsDictTryGetValue);
        EmitPDSSetPrototype(typeBuilder, runtime, prototypeStoreField, prototypeGetOrCreate);
        EmitPDSGetPrototype(typeBuilder, runtime, prototypeStoreField, prototypeTryGet);
        EmitPDSHasPrototypeEntry(typeBuilder, runtime, prototypeStoreField, prototypeTryGet);
        EmitPDSDefineProperty(typeBuilder, runtime, descriptorsField, descriptorsGetOrCreate, descriptorsDictType, descriptorsDictSetItem);
        EmitPDSDeleteProperty(typeBuilder, runtime, descriptorsField, descriptorsTryGet, descriptorsDictType, descriptorsDictContainsKey);
        EmitPDSGetPropertyDescriptor(typeBuilder, runtime, descriptorsField, descriptorsTryGet, descriptorsDictType, descriptorsDictTryGetValue);
        EmitPDSGetStaticShadow(typeBuilder, runtime);
        EmitPDSGetEnumerableExtraKeys(typeBuilder, runtime, descriptorsField, descriptorsTryGet, descriptorsDictType, descriptorsDictTryGetValue);
        EmitPDSGetAllExtraKeys(typeBuilder, runtime, descriptorsField, descriptorsTryGet, descriptorsDictType, descriptorsDictTryGetValue);

        var type = typeBuilder.CreateType()!;
        _ = type;
    }

    /// <summary>
    /// Emits: public static void Freeze(object obj)
    /// </summary>
    private void EmitPDSFreeze(TypeBuilder typeBuilder, EmittedRuntime runtime, FieldBuilder frozenSealedField, MethodInfo getOrCreateValue)
    {
        var method = typeBuilder.DefineMethod(
            "Freeze",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object]
        );
        runtime.PDSFreeze = method;

        var il = method.GetILGenerator();
        var stateLocal = il.DeclareLocal(runtime.FrozenSealedStateType);

        // var state = _frozenSealedState.GetOrCreateValue(obj);
        il.Emit(OpCodes.Ldsfld, frozenSealedField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, getOrCreateValue);
        il.Emit(OpCodes.Stloc, stateLocal);

        // state.IsFrozen = true
        il.Emit(OpCodes.Ldloc, stateLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, runtime.FrozenSealedStateIsFrozen.GetSetMethod()!);

        // state.IsSealed = true
        il.Emit(OpCodes.Ldloc, stateLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, runtime.FrozenSealedStateIsSealed.GetSetMethod()!);

        // state.IsExtensible = false
        il.Emit(OpCodes.Ldloc, stateLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, runtime.FrozenSealedStateIsExtensible.GetSetMethod()!);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void Seal(object obj)
    /// </summary>
    private void EmitPDSSeal(TypeBuilder typeBuilder, EmittedRuntime runtime, FieldBuilder frozenSealedField, MethodInfo getOrCreateValue)
    {
        var method = typeBuilder.DefineMethod(
            "Seal",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object]
        );
        runtime.PDSSeal = method;

        var il = method.GetILGenerator();
        var stateLocal = il.DeclareLocal(runtime.FrozenSealedStateType);

        // var state = _frozenSealedState.GetOrCreateValue(obj);
        il.Emit(OpCodes.Ldsfld, frozenSealedField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, getOrCreateValue);
        il.Emit(OpCodes.Stloc, stateLocal);

        // state.IsSealed = true
        il.Emit(OpCodes.Ldloc, stateLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, runtime.FrozenSealedStateIsSealed.GetSetMethod()!);

        // state.IsExtensible = false
        il.Emit(OpCodes.Ldloc, stateLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, runtime.FrozenSealedStateIsExtensible.GetSetMethod()!);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void PreventExtensions(object obj)
    /// </summary>
    private void EmitPDSPreventExtensions(TypeBuilder typeBuilder, EmittedRuntime runtime, FieldBuilder frozenSealedField, MethodInfo getOrCreateValue)
    {
        var method = typeBuilder.DefineMethod(
            "PreventExtensions",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object]
        );
        runtime.PDSPreventExtensions = method;

        var il = method.GetILGenerator();
        var stateLocal = il.DeclareLocal(runtime.FrozenSealedStateType);

        // var state = _frozenSealedState.GetOrCreateValue(obj);
        il.Emit(OpCodes.Ldsfld, frozenSealedField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, getOrCreateValue);
        il.Emit(OpCodes.Stloc, stateLocal);

        // state.IsExtensible = false
        il.Emit(OpCodes.Ldloc, stateLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, runtime.FrozenSealedStateIsExtensible.GetSetMethod()!);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static bool IsExtensible(object obj)
    /// </summary>
    private void EmitPDSIsExtensible(TypeBuilder typeBuilder, EmittedRuntime runtime, FieldBuilder frozenSealedField, MethodInfo tryGetValue)
    {
        var method = typeBuilder.DefineMethod(
            "IsExtensible",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]
        );
        runtime.PDSIsExtensible = method;

        var il = method.GetILGenerator();
        var stateLocal = il.DeclareLocal(runtime.FrozenSealedStateType);
        var returnTrueLabel = il.DefineLabel();

        // if (_frozenSealedState.TryGetValue(obj, out var state))
        il.Emit(OpCodes.Ldsfld, frozenSealedField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, stateLocal);
        il.Emit(OpCodes.Callvirt, tryGetValue);
        il.Emit(OpCodes.Brfalse, returnTrueLabel);

        // return state.IsExtensible
        il.Emit(OpCodes.Ldloc, stateLocal);
        il.Emit(OpCodes.Callvirt, runtime.FrozenSealedStateIsExtensible.GetGetMethod()!);
        il.Emit(OpCodes.Ret);

        // return true (default)
        il.MarkLabel(returnTrueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static bool IsFrozen(object obj)
    /// </summary>
    private void EmitPDSIsFrozen(TypeBuilder typeBuilder, EmittedRuntime runtime, FieldBuilder frozenSealedField, MethodInfo tryGetValue)
    {
        var method = typeBuilder.DefineMethod(
            "IsFrozen",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]
        );
        runtime.PDSIsFrozen = method;

        var il = method.GetILGenerator();
        var stateLocal = il.DeclareLocal(runtime.FrozenSealedStateType);
        var returnFalseLabel = il.DefineLabel();

        // if (_frozenSealedState.TryGetValue(obj, out var state) && state.IsFrozen)
        il.Emit(OpCodes.Ldsfld, frozenSealedField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, stateLocal);
        il.Emit(OpCodes.Callvirt, tryGetValue);
        il.Emit(OpCodes.Brfalse, returnFalseLabel);

        il.Emit(OpCodes.Ldloc, stateLocal);
        il.Emit(OpCodes.Callvirt, runtime.FrozenSealedStateIsFrozen.GetGetMethod()!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnFalseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static bool IsSealed(object obj)
    /// </summary>
    private void EmitPDSIsSealed(TypeBuilder typeBuilder, EmittedRuntime runtime, FieldBuilder frozenSealedField, MethodInfo tryGetValue)
    {
        var method = typeBuilder.DefineMethod(
            "IsSealed",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]
        );
        runtime.PDSIsSealed = method;

        var il = method.GetILGenerator();
        var stateLocal = il.DeclareLocal(runtime.FrozenSealedStateType);
        var returnFalseLabel = il.DefineLabel();

        // if (_frozenSealedState.TryGetValue(obj, out var state) && state.IsSealed)
        il.Emit(OpCodes.Ldsfld, frozenSealedField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, stateLocal);
        il.Emit(OpCodes.Callvirt, tryGetValue);
        il.Emit(OpCodes.Brfalse, returnFalseLabel);

        il.Emit(OpCodes.Ldloc, stateLocal);
        il.Emit(OpCodes.Callvirt, runtime.FrozenSealedStateIsSealed.GetGetMethod()!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnFalseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static bool CanAddProperty(object obj, string propertyKey)
    /// </summary>
    private void EmitPDSCanAddProperty(TypeBuilder typeBuilder, EmittedRuntime runtime,
        FieldBuilder frozenSealedField, MethodInfo frozenSealedTryGet,
        FieldBuilder descriptorsField, MethodInfo descriptorsTryGet,
        Type descriptorsDictType, MethodInfo descriptorsDictContainsKey, MethodInfo descriptorsDictTryGetValue)
    {
        var method = typeBuilder.DefineMethod(
            "CanAddProperty",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.String]
        );
        runtime.PDSCanAddProperty = method;

        var il = method.GetILGenerator();
        var stateLocal = il.DeclareLocal(runtime.FrozenSealedStateType);
        var returnTrueLabel = il.DefineLabel();
        var checkDictLabel = il.DefineLabel();
        var checkListLabel = il.DefineLabel();
        var checkDescriptorsLabel = il.DefineLabel();
        var returnFalseLabel = il.DefineLabel();

        // if (!_frozenSealedState.TryGetValue(obj, out var state)) return true
        il.Emit(OpCodes.Ldsfld, frozenSealedField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, stateLocal);
        il.Emit(OpCodes.Callvirt, frozenSealedTryGet);
        il.Emit(OpCodes.Brfalse, returnTrueLabel);

        // if (state.IsExtensible) return true
        il.Emit(OpCodes.Ldloc, stateLocal);
        il.Emit(OpCodes.Callvirt, runtime.FrozenSealedStateIsExtensible.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, returnTrueLabel);

        // Not extensible - check if property already exists
        // if (obj is Dictionary<string, object?> dict) return dict.ContainsKey(propertyKey)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brfalse, checkListLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "ContainsKey", _types.String));
        il.Emit(OpCodes.Ret);

        // if (obj is List<object?> list)
        il.MarkLabel(checkListLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brfalse, checkDescriptorsLabel);

        // For arrays, check if numeric index within bounds
        var indexLocal = il.DeclareLocal(_types.Int32);
        var parseSuccessLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, indexLocal);
        il.Emit(OpCodes.Call, typeof(int).GetMethod("TryParse", [typeof(string), typeof(int).MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, returnFalseLabel);

        // index >= 0 && index < list.Count
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, returnFalseLabel);

        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Count"));
        il.Emit(OpCodes.Bge, returnFalseLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        // Check descriptors dictionary
        il.MarkLabel(checkDescriptorsLabel);
        var descriptorsDictLocal = il.DeclareLocal(descriptorsDictType);

        il.Emit(OpCodes.Ldsfld, descriptorsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, descriptorsDictLocal);
        il.Emit(OpCodes.Callvirt, descriptorsTryGet);
        il.Emit(OpCodes.Brfalse, returnFalseLabel);

        il.Emit(OpCodes.Ldloc, descriptorsDictLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, descriptorsDictContainsKey);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnFalseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnTrueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static bool TryGetGetter(object obj, string propertyKey, out object? getter)
    /// </summary>
    private void EmitPDSTryGetGetter(TypeBuilder typeBuilder, EmittedRuntime runtime,
        FieldBuilder descriptorsField, MethodInfo descriptorsTryGet,
        Type descriptorsDictType, MethodInfo descriptorsDictTryGetValue)
    {
        var method = typeBuilder.DefineMethod(
            "TryGetGetter",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.String, _types.Object.MakeByRefType()]
        );
        runtime.PDSTryGetGetter = method;

        var il = method.GetILGenerator();
        var descriptorsDictLocal = il.DeclareLocal(descriptorsDictType);
        var descriptorLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        var returnFalseLabel = il.DefineLabel();
        var keyLocal = il.DeclareLocal(_types.Object);
        EmitNormalizePDSKey(il, runtime, keyLocal);

        // if (!_descriptors.TryGetValue(key, out var descriptors)) goto returnFalse
        il.Emit(OpCodes.Ldsfld, descriptorsField);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Ldloca, descriptorsDictLocal);
        il.Emit(OpCodes.Callvirt, descriptorsTryGet);
        il.Emit(OpCodes.Brfalse, returnFalseLabel);

        // if (!descriptors.TryGetValue(propertyKey, out var descriptor)) goto returnFalse
        il.Emit(OpCodes.Ldloc, descriptorsDictLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, descriptorLocal);
        il.Emit(OpCodes.Callvirt, descriptorsDictTryGetValue);
        il.Emit(OpCodes.Brfalse, returnFalseLabel);

        // if (descriptor.Getter == null) goto returnFalse
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorGetter.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, returnFalseLabel);

        // A getter slot holding JS-undefined marks an accessor descriptor whose
        // `get` is explicitly `undefined` (e.g. `Object.defineProperty(o, "p",
        // { set() {} })`): the slot is non-null only so the descriptor stays
        // classified as an accessor. It is NOT an invokable getter — reading the
        // property must yield undefined, never Invoke() the sentinel. Treat it as
        // "no getter" so callers fall through to the descriptor's undefined value
        // instead of throwing "undefined is not a function".
        // (Test262 Object.{defineProperty,defineProperties,create} 15.2.3.6-3-215..217 et al.)
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorGetter.GetGetMethod()!);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, returnFalseLabel);

        // getter = descriptor.Getter; return true
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorGetter.GetGetMethod()!);
        il.Emit(OpCodes.Stind_Ref);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        // getter = null; return false
        il.MarkLabel(returnFalseLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stind_Ref);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static bool TryGetSetter(object obj, string propertyKey, out object? setter)
    /// </summary>
    private void EmitPDSTryGetSetter(TypeBuilder typeBuilder, EmittedRuntime runtime,
        FieldBuilder descriptorsField, MethodInfo descriptorsTryGet,
        Type descriptorsDictType, MethodInfo descriptorsDictTryGetValue)
    {
        var method = typeBuilder.DefineMethod(
            "TryGetSetter",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.String, _types.Object.MakeByRefType()]
        );
        runtime.PDSTryGetSetter = method;

        var il = method.GetILGenerator();
        var descriptorsDictLocal = il.DeclareLocal(descriptorsDictType);
        var descriptorLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        var returnFalseLabel = il.DefineLabel();
        var keyLocal = il.DeclareLocal(_types.Object);
        EmitNormalizePDSKey(il, runtime, keyLocal);

        // if (!_descriptors.TryGetValue(key, out var descriptors)) goto returnFalse
        il.Emit(OpCodes.Ldsfld, descriptorsField);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Ldloca, descriptorsDictLocal);
        il.Emit(OpCodes.Callvirt, descriptorsTryGet);
        il.Emit(OpCodes.Brfalse, returnFalseLabel);

        // if (!descriptors.TryGetValue(propertyKey, out var descriptor)) goto returnFalse
        il.Emit(OpCodes.Ldloc, descriptorsDictLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, descriptorLocal);
        il.Emit(OpCodes.Callvirt, descriptorsDictTryGetValue);
        il.Emit(OpCodes.Brfalse, returnFalseLabel);

        // if (descriptor.Setter == null) goto returnFalse
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorSetter.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, returnFalseLabel);

        // setter = descriptor.Setter; return true
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorSetter.GetGetMethod()!);
        il.Emit(OpCodes.Stind_Ref);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        // setter = null; return false
        il.MarkLabel(returnFalseLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stind_Ref);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static bool IsWritable(object obj, string propertyKey)
    /// </summary>
    private void EmitPDSIsWritable(TypeBuilder typeBuilder, EmittedRuntime runtime,
        FieldBuilder frozenSealedField, MethodInfo frozenSealedTryGet,
        FieldBuilder descriptorsField, MethodInfo descriptorsTryGet,
        Type descriptorsDictType, MethodInfo descriptorsDictTryGetValue)
    {
        var method = typeBuilder.DefineMethod(
            "IsWritable",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.String]
        );
        runtime.PDSIsWritable = method;

        var il = method.GetILGenerator();
        var stateLocal = il.DeclareLocal(runtime.FrozenSealedStateType);
        var descriptorsDictLocal = il.DeclareLocal(descriptorsDictType);
        var descriptorLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        var returnTrueLabel = il.DefineLabel();
        var returnFalseLabel = il.DefineLabel();
        var checkDescriptorsLabel = il.DefineLabel();

        // Frozen objects are never writable
        il.Emit(OpCodes.Ldsfld, frozenSealedField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, stateLocal);
        il.Emit(OpCodes.Callvirt, frozenSealedTryGet);
        il.Emit(OpCodes.Brfalse, checkDescriptorsLabel);

        il.Emit(OpCodes.Ldloc, stateLocal);
        il.Emit(OpCodes.Callvirt, runtime.FrozenSealedStateIsFrozen.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, returnFalseLabel);

        // Check descriptors
        il.MarkLabel(checkDescriptorsLabel);
        var keyLocal = il.DeclareLocal(_types.Object);
        EmitNormalizePDSKey(il, runtime, keyLocal);
        il.Emit(OpCodes.Ldsfld, descriptorsField);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Ldloca, descriptorsDictLocal);
        il.Emit(OpCodes.Callvirt, descriptorsTryGet);
        il.Emit(OpCodes.Brfalse, returnTrueLabel);

        il.Emit(OpCodes.Ldloc, descriptorsDictLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, descriptorLocal);
        il.Emit(OpCodes.Callvirt, descriptorsDictTryGetValue);
        il.Emit(OpCodes.Brfalse, returnTrueLabel);

        // return descriptor.Writable
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorWritable.GetGetMethod()!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnFalseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnTrueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void SetPrototype(object obj, object? proto)
    /// </summary>
    private void EmitPDSSetPrototype(TypeBuilder typeBuilder, EmittedRuntime runtime,
        FieldBuilder prototypeStoreField, MethodInfo getOrCreateValue)
    {
        var method = typeBuilder.DefineMethod(
            "SetPrototype",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object]
        );
        runtime.PDSSetPrototype = method;

        var il = method.GetILGenerator();
        var infoLocal = il.DeclareLocal(runtime.PrototypeInfoType);

        // var info = _prototypeStore.GetOrCreateValue(obj);
        il.Emit(OpCodes.Ldsfld, prototypeStoreField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, getOrCreateValue);
        il.Emit(OpCodes.Stloc, infoLocal);

        // info.Prototype = proto;
        il.Emit(OpCodes.Ldloc, infoLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, runtime.PrototypeInfoPrototype.GetSetMethod()!);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object? GetPrototype(object obj)
    /// </summary>
    private void EmitPDSGetPrototype(TypeBuilder typeBuilder, EmittedRuntime runtime,
        FieldBuilder prototypeStoreField, MethodInfo tryGetValue)
    {
        var method = typeBuilder.DefineMethod(
            "GetPrototype",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.PDSGetPrototype = method;

        var il = method.GetILGenerator();
        var infoLocal = il.DeclareLocal(runtime.PrototypeInfoType);
        var returnNullLabel = il.DefineLabel();

        // if (!_prototypeStore.TryGetValue(obj, out var info)) return null
        il.Emit(OpCodes.Ldsfld, prototypeStoreField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, infoLocal);
        il.Emit(OpCodes.Callvirt, tryGetValue);
        il.Emit(OpCodes.Brfalse, returnNullLabel);

        // return info.Prototype
        il.Emit(OpCodes.Ldloc, infoLocal);
        il.Emit(OpCodes.Callvirt, runtime.PrototypeInfoPrototype.GetGetMethod()!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnNullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static bool HasPrototypeEntry(object obj). Returns true
    /// iff the prototype store contains an entry for <paramref name="obj"/>,
    /// regardless of whether the stored value is null. Used by
    /// Object.getPrototypeOf to distinguish "no entry" (fall through to default
    /// prototype fallback) from "entry exists with null value" (e.g.
    /// Object.create(null) — must return null, not Object.prototype).
    /// </summary>
    private void EmitPDSHasPrototypeEntry(TypeBuilder typeBuilder, EmittedRuntime runtime,
        FieldBuilder prototypeStoreField, MethodInfo tryGetValue)
    {
        var method = typeBuilder.DefineMethod(
            "HasPrototypeEntry",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]
        );
        runtime.PDSHasPrototypeEntry = method;

        var il = method.GetILGenerator();
        var infoLocal = il.DeclareLocal(runtime.PrototypeInfoType);

        il.Emit(OpCodes.Ldsfld, prototypeStoreField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, infoLocal);
        il.Emit(OpCodes.Callvirt, tryGetValue);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static bool DefineProperty(object obj, string propertyKey, $CompiledPropertyDescriptor descriptor)
    /// Returns true if property was defined successfully, false if object is not extensible and property doesn't exist.
    /// </summary>
    private void EmitPDSDefineProperty(TypeBuilder typeBuilder, EmittedRuntime runtime,
        FieldBuilder descriptorsField, MethodInfo descriptorsGetOrCreate,
        Type descriptorsDictType, MethodInfo descriptorsDictSetItem)
    {
        var method = typeBuilder.DefineMethod(
            "DefineProperty",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.String, runtime.CompiledPropertyDescriptorType]
        );
        runtime.PDSDefineProperty = method;

        var il = method.GetILGenerator();
        var dictLocal = il.DeclareLocal(descriptorsDictType);
        var keyLocal = il.DeclareLocal(_types.Object);
        EmitNormalizePDSKey(il, runtime, keyLocal);

        // var descriptors = _descriptors.GetOrCreateValue(key);
        il.Emit(OpCodes.Ldsfld, descriptorsField);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Callvirt, descriptorsGetOrCreate);
        il.Emit(OpCodes.Stloc, dictLocal);

        // descriptors[propertyKey] = descriptor;
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, descriptorsDictSetItem);

        // return true
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static bool DeleteProperty(object obj, string propertyKey)
    /// Removes the property's descriptor from the per-object dict (if present).
    /// Returns true on successful removal or when the descriptor wasn't there.
    /// Non-configurable descriptors must be filtered by callers — this helper
    /// is the unconditional removal step. PDS doesn't track per-property
    /// configurability separately from the descriptor itself, so the caller
    /// reads the descriptor first via GetPropertyDescriptor for that check.
    /// </summary>
    private void EmitPDSDeleteProperty(TypeBuilder typeBuilder, EmittedRuntime runtime,
        FieldBuilder descriptorsField, MethodInfo descriptorsTryGet,
        Type descriptorsDictType, MethodInfo descriptorsDictContainsKey)
    {
        var method = typeBuilder.DefineMethod(
            "DeleteProperty",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.String]
        );
        runtime.PDSDeleteProperty = method;

        var il = method.GetILGenerator();
        var dictLocal = il.DeclareLocal(descriptorsDictType);
        var keyLocal = il.DeclareLocal(_types.Object);
        EmitNormalizePDSKey(il, runtime, keyLocal);

        // if (!_descriptors.TryGetValue(key, out dict)) return true (nothing to delete)
        il.Emit(OpCodes.Ldsfld, descriptorsField);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Ldloca, dictLocal);
        il.Emit(OpCodes.Callvirt, descriptorsTryGet);
        var hasDictLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, hasDictLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(hasDictLabel);

        // dict.Remove(propertyKey)
        var removeMethod = EmitterTypeHelpers.ResolveMethod(descriptorsDictType,
            typeof(Dictionary<,>).GetMethod("Remove", [typeof(Dictionary<,>).GetGenericArguments()[0]])!);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, removeMethod);
        // Pop the bool result; always return true (matches existing
        // DeleteProperty contract: "true on success or absent").
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static $CompiledPropertyDescriptor? GetPropertyDescriptor(object obj, string propertyKey)
    /// Returns the property descriptor if found, null otherwise.
    /// </summary>
    private void EmitPDSGetPropertyDescriptor(TypeBuilder typeBuilder, EmittedRuntime runtime,
        FieldBuilder descriptorsField, MethodInfo descriptorsTryGet,
        Type descriptorsDictType, MethodInfo descriptorsDictTryGetValue)
    {
        var method = typeBuilder.DefineMethod(
            "GetPropertyDescriptor",
            MethodAttributes.Public | MethodAttributes.Static,
            runtime.CompiledPropertyDescriptorType,
            [_types.Object, _types.String]
        );
        runtime.PDSGetPropertyDescriptor = method;

        var il = method.GetILGenerator();
        var descriptorsDictLocal = il.DeclareLocal(descriptorsDictType);
        var descriptorLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        var returnNullLabel = il.DefineLabel();
        var keyLocal = il.DeclareLocal(_types.Object);
        EmitNormalizePDSKey(il, runtime, keyLocal);

        // if (!_descriptors.TryGetValue(key, out var descriptors)) return null
        il.Emit(OpCodes.Ldsfld, descriptorsField);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Ldloca, descriptorsDictLocal);
        il.Emit(OpCodes.Callvirt, descriptorsTryGet);
        il.Emit(OpCodes.Brfalse, returnNullLabel);

        // if (!descriptors.TryGetValue(propertyKey, out var descriptor)) return null
        il.Emit(OpCodes.Ldloc, descriptorsDictLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, descriptorLocal);
        il.Emit(OpCodes.Callvirt, descriptorsDictTryGetValue);
        il.Emit(OpCodes.Brfalse, returnNullLabel);

        // return descriptor
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Ret);

        // return null
        il.MarkLabel(returnNullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static $CompiledPropertyDescriptor? GetStaticShadow(object typeObj, string propertyKey)
    /// Walks the constructor Type chain (typeObj and each <c>BaseType</c>) probing each class's PDS
    /// store, returning the nearest own-shadow descriptor or null. A subclass static-field write
    /// (<c>Sub.field = v</c>) stores a per-subclass shadow keyed by the subclass Type (the SetProperty
    /// System.Type arm); inherited static-field reads consult this before falling back to the declaring
    /// base's field so JS own-shadow semantics hold in compiled mode (issue #339). Mirrors the inline
    /// ancestor walk already used by the dynamic type-property reader (#265).
    /// </summary>
    private void EmitPDSGetStaticShadow(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetStaticShadow",
            MethodAttributes.Public | MethodAttributes.Static,
            runtime.CompiledPropertyDescriptorType,
            [_types.Object, _types.String]
        );
        runtime.PDSGetStaticShadow = method;

        var il = method.GetILGenerator();
        var walkTypeLocal = il.DeclareLocal(_types.Type);
        var descLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);

        // Type walkType = typeObj as Type;  (null when the receiver isn't a constructor → return null)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Type);
        il.Emit(OpCodes.Stloc, walkTypeLocal);

        var loopLabel = il.DefineLabel();
        var returnNullLabel = il.DefineLabel();
        il.MarkLabel(loopLabel);

        // if (walkType == null) return null;
        il.Emit(OpCodes.Ldloc, walkTypeLocal);
        il.Emit(OpCodes.Brfalse, returnNullLabel);

        // desc = GetPropertyDescriptor(walkType, propertyKey);
        il.Emit(OpCodes.Ldloc, walkTypeLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, descLocal);

        // if (desc != null) return desc;
        il.Emit(OpCodes.Ldloc, descLocal);
        var nextLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, nextLabel);
        il.Emit(OpCodes.Ldloc, descLocal);
        il.Emit(OpCodes.Ret);

        // walkType = walkType.BaseType; continue;
        il.MarkLabel(nextLabel);
        il.Emit(OpCodes.Ldloc, walkTypeLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "BaseType").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, walkTypeLocal);
        il.Emit(OpCodes.Br, loopLabel);

        il.MarkLabel(returnNullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static List&lt;object?&gt; GetEnumerableExtraKeys(object obj, Dictionary&lt;string,object?&gt; dict)
    /// Returns the set of enumerable PDS keys that are NOT already in dict.
    /// Used by GetKeys / GetValues / GetEntries to surface accessor-only own
    /// properties (created via Object.defineProperty without backing-dict
    /// writes) in ECMA-262 §10.1.11.1 OrdinaryOwnPropertyKeys order.
    /// </summary>
    private void EmitPDSGetEnumerableExtraKeys(TypeBuilder typeBuilder, EmittedRuntime runtime,
        FieldBuilder descriptorsField, MethodInfo descriptorsTryGet,
        Type descriptorsDictType, MethodInfo descriptorsDictTryGetValue)
    {
        var method = typeBuilder.DefineMethod(
            "GetEnumerableExtraKeys",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.Object, _types.DictionaryStringObject]
        );
        runtime.PDSGetEnumerableExtraKeys = method;

        var il = method.GetILGenerator();
        var resultLocal = il.DeclareLocal(_types.ListOfObject);
        var pdsDictLocal = il.DeclareLocal(descriptorsDictType);
        var keyLocal = il.DeclareLocal(_types.Object);
        EmitNormalizePDSKey(il, runtime, keyLocal);

        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ListOfObject));
        il.Emit(OpCodes.Stloc, resultLocal);

        // if (!_descriptors.TryGetValue(key, out pdsDict)) return resultLocal;
        il.Emit(OpCodes.Ldsfld, descriptorsField);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Ldloca, pdsDictLocal);
        il.Emit(OpCodes.Callvirt, descriptorsTryGet);
        var returnResultLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, returnResultLabel);

        // foreach (var kvp in pdsDict): if dict missing the key AND descriptor.Enumerable, result.Add(kvp.Key).
        // Use the dict's GetEnumerator → MoveNext → Current pattern via the
        // ResolveMethod-resolved methods (descriptorsDictType is a TypeBuilder
        // generic instantiation; direct GetMethod doesn't work).
        var kvpType = _types.MakeGenericType(typeof(KeyValuePair<,>), _types.String, runtime.CompiledPropertyDescriptorType);
        var enumeratorType = _types.MakeGenericType(typeof(Dictionary<,>.Enumerator), _types.String, runtime.CompiledPropertyDescriptorType);

        var dictOpenType = typeof(Dictionary<,>);
        var dictOpenGetEnumerator = dictOpenType.GetMethod("GetEnumerator")!;
        var descriptorsDictGetEnumerator = EmitterTypeHelpers.ResolveMethod(descriptorsDictType, dictOpenGetEnumerator);

        var enumOpenType = typeof(Dictionary<,>.Enumerator);
        var enumOpenMoveNext = enumOpenType.GetMethod("MoveNext")!;
        var enumOpenCurrent = enumOpenType.GetProperty("Current")!.GetGetMethod()!;
        var enumOpenDispose = enumOpenType.GetMethod("Dispose")!;
        var resolvedEnumMoveNext = EmitterTypeHelpers.ResolveMethod(enumeratorType, enumOpenMoveNext);
        var resolvedEnumCurrent = EmitterTypeHelpers.ResolveMethod(enumeratorType, enumOpenCurrent);
        var resolvedEnumDispose = EmitterTypeHelpers.ResolveMethod(enumeratorType, enumOpenDispose);

        var kvpOpenType = typeof(KeyValuePair<,>);
        var kvpOpenKey = kvpOpenType.GetProperty("Key")!.GetGetMethod()!;
        var kvpOpenValue = kvpOpenType.GetProperty("Value")!.GetGetMethod()!;
        var resolvedKvpKey = EmitterTypeHelpers.ResolveMethod(kvpType, kvpOpenKey);
        var resolvedKvpValue = EmitterTypeHelpers.ResolveMethod(kvpType, kvpOpenValue);

        var enumeratorLocal = il.DeclareLocal(enumeratorType);
        var kvpLocal = il.DeclareLocal(kvpType);
        il.Emit(OpCodes.Ldloc, pdsDictLocal);
        il.Emit(OpCodes.Callvirt, descriptorsDictGetEnumerator);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var currentKeyLocal = il.DeclareLocal(_types.String);
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, resolvedEnumMoveNext);
        il.Emit(OpCodes.Brfalse, loopEnd);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, resolvedEnumCurrent);
        il.Emit(OpCodes.Stloc, kvpLocal);
        // currentKey = kvp.Key
        il.Emit(OpCodes.Ldloca, kvpLocal);
        il.Emit(OpCodes.Call, resolvedKvpKey);
        il.Emit(OpCodes.Stloc, currentKeyLocal);
        // Skip if dict (arg1) already contains the key.
        il.Emit(OpCodes.Ldarg_1);
        var skipDictNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, skipDictNullLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, currentKeyLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "ContainsKey", _types.String));
        il.Emit(OpCodes.Brtrue, loopStart);
        il.MarkLabel(skipDictNullLabel);
        // Skip if descriptor.Enumerable is false.
        il.Emit(OpCodes.Ldloca, kvpLocal);
        il.Emit(OpCodes.Call, resolvedKvpValue);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorEnumerable.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, loopStart);
        // result.Add(currentKey)
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, currentKeyLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", [_types.Object])!);
        il.Emit(OpCodes.Br, loopStart);
        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, resolvedEnumDispose);

        il.MarkLabel(returnResultLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Like <see cref="EmitPDSGetEnumerableExtraKeys"/>, but does NOT filter
    /// by the Enumerable bit. Used by <c>Object.getOwnPropertyNames</c>
    /// (ECMA-262 §20.1.2.10), which returns the union of own enumerable AND
    /// non-enumerable string-keyed properties.
    /// </summary>
    private void EmitPDSGetAllExtraKeys(TypeBuilder typeBuilder, EmittedRuntime runtime,
        FieldBuilder descriptorsField, MethodInfo descriptorsTryGet,
        Type descriptorsDictType, MethodInfo descriptorsDictTryGetValue)
    {
        var method = typeBuilder.DefineMethod(
            "GetAllExtraKeys",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.Object, _types.DictionaryStringObject]
        );
        runtime.PDSGetAllExtraKeys = method;

        var il = method.GetILGenerator();
        var resultLocal = il.DeclareLocal(_types.ListOfObject);
        var pdsDictLocal = il.DeclareLocal(descriptorsDictType);
        var keyLocal = il.DeclareLocal(_types.Object);
        EmitNormalizePDSKey(il, runtime, keyLocal);

        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ListOfObject));
        il.Emit(OpCodes.Stloc, resultLocal);

        il.Emit(OpCodes.Ldsfld, descriptorsField);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Ldloca, pdsDictLocal);
        il.Emit(OpCodes.Callvirt, descriptorsTryGet);
        var returnResultLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, returnResultLabel);

        var kvpType = _types.MakeGenericType(typeof(KeyValuePair<,>), _types.String, runtime.CompiledPropertyDescriptorType);
        var enumeratorType = _types.MakeGenericType(typeof(Dictionary<,>.Enumerator), _types.String, runtime.CompiledPropertyDescriptorType);
        var dictOpenType = typeof(Dictionary<,>);
        var descriptorsDictGetEnumerator = EmitterTypeHelpers.ResolveMethod(descriptorsDictType, dictOpenType.GetMethod("GetEnumerator")!);
        var enumOpenType = typeof(Dictionary<,>.Enumerator);
        var resolvedEnumMoveNext = EmitterTypeHelpers.ResolveMethod(enumeratorType, enumOpenType.GetMethod("MoveNext")!);
        var resolvedEnumCurrent = EmitterTypeHelpers.ResolveMethod(enumeratorType, enumOpenType.GetProperty("Current")!.GetGetMethod()!);
        var resolvedEnumDispose = EmitterTypeHelpers.ResolveMethod(enumeratorType, enumOpenType.GetMethod("Dispose")!);
        var kvpOpenType = typeof(KeyValuePair<,>);
        var resolvedKvpKey = EmitterTypeHelpers.ResolveMethod(kvpType, kvpOpenType.GetProperty("Key")!.GetGetMethod()!);

        var enumeratorLocal = il.DeclareLocal(enumeratorType);
        var kvpLocal = il.DeclareLocal(kvpType);
        il.Emit(OpCodes.Ldloc, pdsDictLocal);
        il.Emit(OpCodes.Callvirt, descriptorsDictGetEnumerator);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var currentKeyLocal = il.DeclareLocal(_types.String);
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, resolvedEnumMoveNext);
        il.Emit(OpCodes.Brfalse, loopEnd);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, resolvedEnumCurrent);
        il.Emit(OpCodes.Stloc, kvpLocal);
        il.Emit(OpCodes.Ldloca, kvpLocal);
        il.Emit(OpCodes.Call, resolvedKvpKey);
        il.Emit(OpCodes.Stloc, currentKeyLocal);
        // Skip if dict (arg1) already contains the key.
        il.Emit(OpCodes.Ldarg_1);
        var skipDictNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, skipDictNullLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, currentKeyLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "ContainsKey", _types.String));
        il.Emit(OpCodes.Brtrue, loopStart);
        il.MarkLabel(skipDictNullLabel);
        // No Enumerable filter — return all PDS keys.
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, currentKeyLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", [_types.Object])!);
        il.Emit(OpCodes.Br, loopStart);
        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, resolvedEnumDispose);

        il.MarkLabel(returnResultLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Reads <c>arg0</c> (the receiver), and stores the canonical PDS key in
    /// <paramref name="keyLocal"/>: if the receiver is a <c>$TSFunction</c>,
    /// substitute its underlying <c>MethodInfo</c>; otherwise use the receiver
    /// directly. Required so <c>fn.x = v</c> followed by <c>fn.x</c> resolve
    /// to the same descriptor entry even when each <c>fn</c> reference produces
    /// a fresh wrapper instance — function declarations don't share identity
    /// across references in compiled mode (no <c>GetOrCreate</c> instance cache
    /// at the IL emit sites). MethodInfo is identity-stable across reflection
    /// reads, so it makes a reliable canonical key for ConditionalWeakTable.
    /// </summary>
    private void EmitNormalizePDSKey(ILGenerator il, EmittedRuntime runtime, LocalBuilder keyLocal)
    {
        // var key = arg0;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, keyLocal);

        // if (arg0 is $TSFunction f) key = f.GetMethodInfo() ?? arg0;
        var afterLabel = il.DefineLabel();
        var fLocal = il.DeclareLocal(runtime.TSFunctionType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Stloc, fLocal);
        il.Emit(OpCodes.Brfalse, afterLabel);

        // mi = f.GetMethodInfo()
        var miLocal = il.DeclareLocal(_types.MethodInfo);
        il.Emit(OpCodes.Ldloc, fLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionGetMethodInfo);
        il.Emit(OpCodes.Stloc, miLocal);

        // if (mi != null) key = mi
        il.Emit(OpCodes.Ldloc, miLocal);
        il.Emit(OpCodes.Brfalse, afterLabel);
        il.Emit(OpCodes.Ldloc, miLocal);
        il.Emit(OpCodes.Stloc, keyLocal);

        il.MarkLabel(afterLabel);
    }

    /// <summary>
    /// Helper to emit a simple auto-property (get/set for a backing field).
    /// </summary>
    private void EmitAutoProperty(TypeBuilder typeBuilder, string name, Type propertyType, FieldBuilder backingField)
    {
        var property = typeBuilder.DefineProperty(
            name,
            PropertyAttributes.None,
            propertyType,
            null
        );

        // Getter
        var getter = typeBuilder.DefineMethod(
            $"get_{name}",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            propertyType,
            Type.EmptyTypes
        );
        var getterIl = getter.GetILGenerator();
        getterIl.Emit(OpCodes.Ldarg_0);
        getterIl.Emit(OpCodes.Ldfld, backingField);
        getterIl.Emit(OpCodes.Ret);
        property.SetGetMethod(getter);

        // Setter
        var setter = typeBuilder.DefineMethod(
            $"set_{name}",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            null,
            [propertyType]
        );
        var setterIl = setter.GetILGenerator();
        setterIl.Emit(OpCodes.Ldarg_0);
        setterIl.Emit(OpCodes.Ldarg_1);
        setterIl.Emit(OpCodes.Stfld, backingField);
        setterIl.Emit(OpCodes.Ret);
        property.SetSetMethod(setter);
    }
}

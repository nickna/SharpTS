using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits Reflect.metadata API support: a static metadata store and access methods.
/// The store is a Dictionary keyed by (target, propertyKey) composite, values are
/// inner dictionaries mapping metadata keys to values.
/// Pure IL — no reflection back to SharpTS.dll.
/// </summary>
public partial class RuntimeEmitter
{
    // Static field on $Runtime for the metadata store
    private FieldBuilder _reflectMetadataStore = null!;

    private void EmitReflectMetadataMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Static field: Dictionary<string, Dictionary<string, object?>> _metadataStore
        // Key is target.GetHashCode() + ":" + propertyKey (composite string key)
        _reflectMetadataStore = typeBuilder.DefineField(
            "_metadataStore",
            _types.DictionaryStringObject,
            FieldAttributes.Private | FieldAttributes.Static);

        // Static constructor to initialize the store
        // (Or we init lazily in each method)

        EmitReflectDefineMetadata(typeBuilder, runtime);
        EmitReflectGetMetadata(typeBuilder, runtime);
        EmitReflectHasMetadata(typeBuilder, runtime);
        EmitReflectGetMetadataKeys(typeBuilder, runtime);
        EmitReflectDeleteMetadata(typeBuilder, runtime);
    }

    /// <summary>
    /// Helper: emits IL to compute the composite key and get/create the inner dict.
    /// Expects: target (object) and propertyKey (object, nullable) on stack.
    /// Leaves the inner Dictionary&lt;string, object?&gt; on stack.
    /// Also stores the composite key string in keyLocal for later use.
    /// </summary>
    private void EmitGetOrCreateMetadataDict(ILGenerator il, LocalBuilder keyLocal)
    {
        var targetLocal = il.DeclareLocal(_types.Object);
        var propKeyLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, propKeyLocal);
        il.Emit(OpCodes.Stloc, targetLocal);

        // Ensure store is initialized
        var storeOkLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, _reflectMetadataStore);
        il.Emit(OpCodes.Brtrue, storeOkLabel);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Stsfld, _reflectMetadataStore);
        il.MarkLabel(storeOkLabel);

        // Compute composite key: RuntimeHelpers.GetHashCode(target) + ":" + (propKey?.ToString() ?? "")
        il.Emit(OpCodes.Ldloc, targetLocal);
        il.Emit(OpCodes.Call, typeof(System.Runtime.CompilerServices.RuntimeHelpers)
            .GetMethod("GetHashCode", [typeof(object)])!);
        var intLocal = il.DeclareLocal(typeof(int));
        il.Emit(OpCodes.Stloc, intLocal);
        il.Emit(OpCodes.Ldloca, intLocal);
        il.Emit(OpCodes.Call, typeof(int).GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ldstr, ":");

        // propertyKey part
        var propNullLabel = il.DefineLabel();
        var afterPropLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, propKeyLocal);
        il.Emit(OpCodes.Brfalse, propNullLabel);
        il.Emit(OpCodes.Ldloc, propKeyLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Br, afterPropLabel);
        il.MarkLabel(propNullLabel);
        il.Emit(OpCodes.Ldstr, "");
        il.MarkLabel(afterPropLabel);

        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [_types.String, _types.String, _types.String])!);
        il.Emit(OpCodes.Stloc, keyLocal);

        // Get or create inner dict
        var innerLocal = il.DeclareLocal(_types.Object);
        var foundLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldsfld, _reflectMetadataStore);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Ldloca, innerLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue",
            [_types.String, _types.Object.MakeByRefType()])!);
        il.Emit(OpCodes.Brtrue, foundLabel);

        // Not found — create new inner dict
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Stloc, innerLocal);
        il.Emit(OpCodes.Ldsfld, _reflectMetadataStore);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Ldloc, innerLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object));

        il.MarkLabel(foundLabel);
        il.Emit(OpCodes.Ldloc, innerLocal);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
    }

    /// <summary>
    /// Reflect.defineMetadata(metadataKey, metadataValue, target[, propertyKey])
    /// </summary>
    private void EmitReflectDefineMetadata(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ReflectDefineMetadata",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object, _types.Object, _types.Object]); // key, value, target, propKey
        runtime.ReflectDefineMetadata = method;

        var il = method.GetILGenerator();
        var compositeKeyLocal = il.DeclareLocal(_types.String);

        // Get or create inner dict for (target, propertyKey)
        il.Emit(OpCodes.Ldarg_2); // target
        il.Emit(OpCodes.Ldarg_3); // propertyKey
        EmitGetOrCreateMetadataDict(il, compositeKeyLocal);

        // innerDict[metadataKey.ToString()] = metadataValue
        il.Emit(OpCodes.Ldarg_0); // metadataKey
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Ldarg_1); // metadataValue
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object));

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Reflect.getMetadata(metadataKey, target[, propertyKey]) → value or undefined
    /// </summary>
    private void EmitReflectGetMetadata(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ReflectGetMetadata",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object, _types.Object]); // key, target, propKey
        runtime.ReflectGetMetadata = method;

        var il = method.GetILGenerator();

        // If store is null, return null
        var storeExistsLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, _reflectMetadataStore);
        il.Emit(OpCodes.Brtrue, storeExistsLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(storeExistsLabel);

        var compositeKeyLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_1); // target
        il.Emit(OpCodes.Ldarg_2); // propertyKey
        EmitGetOrCreateMetadataDict(il, compositeKeyLocal);

        // Try to get value from inner dict
        var resultLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0); // metadataKey
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Ldloca, resultLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue",
            [_types.String, _types.Object.MakeByRefType()])!);

        var foundLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, foundLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(foundLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Reflect.hasMetadata(metadataKey, target[, propertyKey]) → bool
    /// </summary>
    private void EmitReflectHasMetadata(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ReflectHasMetadata",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object, _types.Object]); // key, target, propKey
        runtime.ReflectHasMetadata = method;

        var il = method.GetILGenerator();

        var storeExistsLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, _reflectMetadataStore);
        il.Emit(OpCodes.Brtrue, storeExistsLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(storeExistsLabel);

        var compositeKeyLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        EmitGetOrCreateMetadataDict(il, compositeKeyLocal);

        // Check if inner dict contains key
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "ContainsKey", [_types.String])!);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Reflect.getMetadataKeys(target[, propertyKey]) → string[]
    /// </summary>
    private void EmitReflectGetMetadataKeys(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ReflectGetMetadataKeys",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]); // target, propKey
        runtime.ReflectGetMetadataKeys = method;

        var il = method.GetILGenerator();

        var storeExistsLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, _reflectMetadataStore);
        il.Emit(OpCodes.Brtrue, storeExistsLabel);
        // Return empty array
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, Type.EmptyTypes)!);
        il.Emit(OpCodes.Newobj, runtime.TSArrayCtor);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(storeExistsLabel);

        var compositeKeyLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_0); // target
        il.Emit(OpCodes.Ldarg_1); // propertyKey
        EmitGetOrCreateMetadataDict(il, compositeKeyLocal);

        // Get keys from inner dict, convert to List<object?>
        var innerDictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, innerDictLocal);

        // new List<object?>(innerDict.Keys)
        // Actually, iterate and build list
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, Type.EmptyTypes)!);
        var listLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Stloc, listLocal);

        // Get the Keys collection and iterate
        il.Emit(OpCodes.Ldloc, innerDictLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.DictionaryStringObject, "Keys")!.GetGetMethod()!);
        var keysLocal = il.DeclareLocal(typeof(Dictionary<string, object>.KeyCollection));
        il.Emit(OpCodes.Stloc, keysLocal);

        // Get enumerator
        il.Emit(OpCodes.Ldloc, keysLocal);
        var getEnumerator = typeof(Dictionary<string, object>.KeyCollection).GetMethod("GetEnumerator")!;
        il.Emit(OpCodes.Callvirt, getEnumerator);
        var enumType = typeof(Dictionary<string, object>.KeyCollection.Enumerator);
        var enumLocal = il.DeclareLocal(enumType);
        il.Emit(OpCodes.Stloc, enumLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloca, enumLocal);
        il.Emit(OpCodes.Call, enumType.GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, loopEnd);

        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloca, enumLocal);
        il.Emit(OpCodes.Call, enumType.GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add")!);

        il.Emit(OpCodes.Br, loopStart);
        il.MarkLabel(loopEnd);

        // Wrap in TSArray
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Newobj, runtime.TSArrayCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Reflect.deleteMetadata(metadataKey, target[, propertyKey]) → bool
    /// </summary>
    private void EmitReflectDeleteMetadata(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ReflectDeleteMetadata",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object, _types.Object]); // key, target, propKey
        runtime.ReflectDeleteMetadata = method;

        var il = method.GetILGenerator();

        var storeExistsLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, _reflectMetadataStore);
        il.Emit(OpCodes.Brtrue, storeExistsLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(storeExistsLabel);

        var compositeKeyLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        EmitGetOrCreateMetadataDict(il, compositeKeyLocal);

        // Remove key from inner dict
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "Remove", [_types.String])!);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits $ReflectMetadataDecorator: a closure class that captures (key, value)
    /// and returns a decorator function (target) => { defineMetadata(key, value, target); return target; }.
    /// </summary>
    private void EmitReflectMetadataDecoratorClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = moduleBuilder.DefineType(
            "$ReflectMetadataDecorator",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit
        );

        // Fields: _key and _value
        var keyField = typeBuilder.DefineField("_key", _types.Object, FieldAttributes.Public);
        var valueField = typeBuilder.DefineField("_value", _types.Object, FieldAttributes.Public);

        // Constructor(object key, object value)
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Object, _types.Object]
        );
        runtime.ReflectMetadataDecoratorCtor = ctor;
        {
            var il = ctor.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stfld, keyField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Stfld, valueField);
            il.Emit(OpCodes.Ret);
        }

        // Invoke(object[] args) → object
        // args[0] = target
        // Calls: ReflectDefineMetadata(_key, _value, target, null)
        // Returns: target
        var invoke = typeBuilder.DefineMethod(
            "Invoke",
            MethodAttributes.Public,
            _types.Object,
            [_types.ObjectArray]
        );
        runtime.ReflectMetadataDecoratorInvoke = invoke;
        {
            var il = invoke.GetILGenerator();

            // Load args for ReflectDefineMetadata(key, value, target, propertyKey)
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, keyField);   // key
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, valueField);  // value
            il.Emit(OpCodes.Ldarg_1);            // args
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_Ref);         // target = args[0]
            il.Emit(OpCodes.Ldnull);             // propertyKey = null
            il.Emit(OpCodes.Call, runtime.ReflectDefineMetadata);
            // ReflectDefineMetadata returns void, nothing to pop

            // Return target
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Ret);
        }

        typeBuilder.CreateType();
    }
}

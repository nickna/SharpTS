using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitGetIndex(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetIndex",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );
        runtime.GetIndex = method;

        var il = method.GetILGenerator();
        var listLabel = il.DefineLabel();
        var arrayLabel = il.DefineLabel();
        var stringLabel = il.DefineLabel();
        var dictLabel = il.DefineLabel();
        var dictStringKeyLabel = il.DefineLabel();
        var dictNumericKeyLabel = il.DefineLabel();
        var symbolKeyLabel = il.DefineLabel();
        var classInstanceLabel = il.DefineLabel();
        var typedArrayLabel = il.DefineLabel();
        var nullLabel = il.DefineLabel();

        // null check on obj
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // Check if index is a symbol first (symbols work on any object type)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.IsSymbolMethod);
        il.Emit(OpCodes.Brtrue, symbolKeyLabel);

        // TypedArray (check before List since TypedArray is more specific)
        // Use type name check to avoid hard dependency on SharpTS.dll
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.IsTypedArrayMethod);
        il.Emit(OpCodes.Brtrue, typedArrayLabel);

        // List
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brtrue, listLabel);

        // Native .NET Array (e.g., string[] from command line args)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ArrayType);
        il.Emit(OpCodes.Brtrue, arrayLabel);

        // String
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, stringLabel);

        // Dict with string key
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, dictLabel);

        // Class instance: check if index is string, then use GetFieldsProperty
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, classInstanceLabel);

        // Fallthrough: return null
        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        // Symbol key handler: use GetSymbolDict(obj).TryGetValue(index, out value)
        il.MarkLabel(symbolKeyLabel);
        var symbolDictLocal = il.DeclareLocal(_types.DictionaryObjectObject);
        var symbolValueLocal = il.DeclareLocal(_types.Object);
        // var symbolDict = GetSymbolDict(obj);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.GetSymbolDictMethod);
        il.Emit(OpCodes.Stloc, symbolDictLocal);
        // if (symbolDict.TryGetValue(index, out value)) return value; else return undefined;
        il.Emit(OpCodes.Ldloc, symbolDictLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, symbolValueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "TryGetValue"));
        var symbolFoundLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, symbolFoundLabel);
        // Return undefined for missing symbol properties (JavaScript semantics)
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(symbolFoundLabel);
        il.Emit(OpCodes.Ldloc, symbolValueLocal);
        il.Emit(OpCodes.Ret);

        // TypedArray handler: use helper to get element (avoids hard dependency on SharpTS.dll)
        il.MarkLabel(typedArrayLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToInt32", _types.Object));
        il.Emit(OpCodes.Call, runtime.GetTypedArrayElementMethod);
        il.Emit(OpCodes.Ret);

        // Class instance handler: use GetFieldsProperty(obj, index as string)
        il.MarkLabel(classInstanceLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Call, runtime.GetFieldsProperty);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(listLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToInt32", _types.Object));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", _types.Int32));
        il.Emit(OpCodes.Ret);

        // Native .NET Array handler (e.g., string[] from command line args)
        il.MarkLabel(arrayLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ArrayType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToInt32", _types.Object));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ArrayType, "GetValue", _types.Int32));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(stringLabel);
        var charLocal = il.DeclareLocal(_types.Char);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToInt32", _types.Object));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", _types.Int32));
        il.Emit(OpCodes.Stloc, charLocal);
        il.Emit(OpCodes.Ldloca, charLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Char, "ToString"));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(dictLabel);
        // Check if index is string
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, dictStringKeyLabel);
        // Check if index is double (numeric key - convert to string)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, dictNumericKeyLabel);
        // Otherwise return null (non-string, non-numeric, non-symbol keys not supported)
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        var valueLocal = il.DeclareLocal(_types.Object);

        il.MarkLabel(dictStringKeyLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue"));
        var foundLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, foundLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(foundLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(dictNumericKeyLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString"));
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue"));
        var foundNumLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, foundNumLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(foundNumLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitSetIndex(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetIndex",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object, _types.Object]
        );
        runtime.SetIndex = method;

        var il = method.GetILGenerator();
        var listLabel = il.DefineLabel();
        var dictLabel = il.DefineLabel();
        var dictStringKeyLabel = il.DefineLabel();
        var dictNumericKeyLabel = il.DefineLabel();
        var symbolKeyLabel = il.DefineLabel();
        var classInstanceLabel = il.DefineLabel();
        var typedArraySetLabel = il.DefineLabel();
        var nullLabel = il.DefineLabel();

        // null check on obj
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // Check if index is a symbol first (symbols work on any object type)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.IsSymbolMethod);
        il.Emit(OpCodes.Brtrue, symbolKeyLabel);

        // TypedArray (check before List since TypedArray is more specific)
        // Use type name check to avoid hard dependency on SharpTS.dll
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.IsTypedArrayMethod);
        il.Emit(OpCodes.Brtrue, typedArraySetLabel);

        // List
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brtrue, listLabel);

        // Dict
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, dictLabel);

        // Class instance: check if index is string, then use SetFieldsProperty
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, classInstanceLabel);

        // Fallthrough: return (ignore)
        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ret);

        // Symbol key handler: GetSymbolDict(obj)[index] = value
        il.MarkLabel(symbolKeyLabel);
        // GetSymbolDict(obj)[index] = value
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.GetSymbolDictMethod);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "set_Item"));
        il.Emit(OpCodes.Ret);

        // TypedArray handler: use helper to set element (avoids hard dependency on SharpTS.dll)
        il.MarkLabel(typedArraySetLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToInt32", _types.Object));
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.SetTypedArrayElementMethod);
        il.Emit(OpCodes.Ret);

        // Class instance handler: use SetFieldsProperty(obj, index as string, value)
        il.MarkLabel(classInstanceLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.SetFieldsProperty);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(listLabel);
        // Check if list is frozen (silently ignore in non-strict mode)
        var listSetLabel = il.DefineLabel();
        var listFrozenCheckLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldsfld, runtime.FrozenObjectsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, listFrozenCheckLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue", _types.Object, _types.Object.MakeByRefType()));
        il.Emit(OpCodes.Brtrue, nullLabel); // Frozen - silently return
        // Check if sealed (sealed allows modifications but not additions - for array index, allow it)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToInt32", _types.Object));
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "set_Item", _types.Int32, _types.Object));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(dictLabel);
        // Check if index is string
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, dictStringKeyLabel);
        // Check if index is double (numeric key - convert to string)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, dictNumericKeyLabel);
        // Otherwise ignore (non-string, non-numeric, non-symbol keys not supported)
        il.Emit(OpCodes.Ret);

        il.MarkLabel(dictStringKeyLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(dictNumericKeyLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits DeleteIndex(object obj, object key) -> bool
    /// Handles both symbol keys and string keys for delete operations.
    /// </summary>
    private void EmitDeleteIndex(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DeleteIndex",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.Object]
        );
        runtime.DeleteIndex = method;

        var il = method.GetILGenerator();
        var dictLabel = il.DefineLabel();
        var dictStringKeyLabel = il.DefineLabel();
        var dictNumericKeyLabel = il.DefineLabel();
        var symbolKeyLabel = il.DefineLabel();
        var trueLabel = il.DefineLabel();

        // null check on obj - return true (deleting from null is allowed in JS)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, trueLabel);

        // Check if index is a symbol first
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.IsSymbolMethod);
        il.Emit(OpCodes.Brtrue, symbolKeyLabel);

        // Dict<string, object>
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, dictLabel);

        // Other types (arrays, strings, etc.) - cannot delete, return true
        il.Emit(OpCodes.Br, trueLabel);

        // Symbol key handler: GetSymbolDict(obj).Remove(key)
        il.MarkLabel(symbolKeyLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.GetSymbolDictMethod);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "Remove", _types.Object));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(dictLabel);
        // Check if frozen
        var valueLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldsfld, runtime.FrozenObjectsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue", _types.Object, _types.Object.MakeByRefType()));
        var notFrozenLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notFrozenLabel);
        // Frozen - return false
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        // Check if sealed
        il.MarkLabel(notFrozenLabel);
        il.Emit(OpCodes.Ldsfld, runtime.SealedObjectsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue", _types.Object, _types.Object.MakeByRefType()));
        var notSealedLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notSealedLabel);
        // Sealed - return false
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        // Check if index is string
        il.MarkLabel(notSealedLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, dictStringKeyLabel);
        // Check if index is double (numeric key - convert to string)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, dictNumericKeyLabel);
        // Other key types - return true
        il.Emit(OpCodes.Br, trueLabel);

        il.MarkLabel(dictStringKeyLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "Remove", _types.String));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(dictNumericKeyLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "Remove", _types.String));
        il.Emit(OpCodes.Ret);

        // Return true (default)
        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
    }
}


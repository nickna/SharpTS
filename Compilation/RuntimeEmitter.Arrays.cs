using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitCreateArray(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CreateArray",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.ObjectArray]
        );
        runtime.CreateArray = method;

        var il = method.GetILGenerator();
        // new List<object>(elements)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.IEnumerableOfObject));
        il.Emit(OpCodes.Ret);
    }

    private void EmitGetLength(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetLength",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Int32,
            [_types.Object]
        );
        runtime.GetLength = method;

        var il = method.GetILGenerator();
        var listLabel = il.DefineLabel();
        var stringLabel = il.DefineLabel();
        var defaultLabel = il.DefineLabel();

        // List
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brtrue, listLabel);

        // String
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, stringLabel);

        // Default
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(listLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(stringLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitGetElement(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetElement",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Int32]
        );
        runtime.GetElement = method;

        var il = method.GetILGenerator();
        var listLabel = il.DefineLabel();
        var stringLabel = il.DefineLabel();

        // List
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brtrue, listLabel);

        // String
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, stringLabel);

        // Default
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(listLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", _types.Int32));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(stringLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", _types.Int32));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Char, "ToString", _types.EmptyTypes));
        il.Emit(OpCodes.Ret);
    }

    private void EmitGetKeys(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetKeys",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.Object]
        );
        runtime.GetKeys = method;

        var il = method.GetILGenerator();
        var dictType = _types.DictionaryStringObject;
        var listType = _types.ListOfObject;

        var resultLocal = il.DeclareLocal(listType);
        var dictLocal = il.DeclareLocal(dictType);
        var listLocal = il.DeclareLocal(listType);
        var typeLocal = il.DeclareLocal(_types.Type);
        var fieldsLocal = il.DeclareLocal(_types.FieldInfoArray);
        var indexLocal = il.DeclareLocal(_types.Int32);
        var fieldLocal = il.DeclareLocal(_types.FieldInfo);
        var fieldNameLocal = il.DeclareLocal(_types.String);
        var fieldsFieldLocal = il.DeclareLocal(_types.FieldInfo);
        var fieldsDictLocal = il.DeclareLocal(dictType);

        var checkListLabel = il.DefineLabel();
        var reflectionLabel = il.DefineLabel();
        var returnEmptyLabel = il.DefineLabel();
        var fieldLoopStartLabel = il.DefineLabel();
        var fieldLoopEndLabel = il.DefineLabel();
        var skipFieldLabel = il.DefineLabel();
        var checkFieldsDictLabel = il.DefineLabel();
        var fieldsLoopStartLabel = il.DefineLabel();
        var fieldsLoopEndLabel = il.DefineLabel();
        var skipFieldsLabel = il.DefineLabel();
        var returnResultLabel = il.DefineLabel();

        // if (obj is Dictionary<string, object?> dict)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, dictType);
        il.Emit(OpCodes.Stloc, dictLocal);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Brfalse, checkListLabel);

        // return dict.Keys.Select(k => (object?)k).ToList();
        // Simplified: iterate keys and add to list
        il.Emit(OpCodes.Newobj, listType.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);

        // Use KeyCollection and iterate
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(dictType, "Keys").GetGetMethod()!);
        var keysType = _types.MakeGenericType(typeof(Dictionary<,>.KeyCollection).GetGenericTypeDefinition(), _types.String, _types.Object);
        var keysEnumeratorType = _types.MakeGenericType(typeof(Dictionary<,>.KeyCollection.Enumerator).GetGenericTypeDefinition(), _types.String, _types.Object);
        var keysEnumeratorLocal = il.DeclareLocal(keysEnumeratorType);
        il.Emit(OpCodes.Callvirt, keysType.GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Stloc, keysEnumeratorLocal);

        var keysLoopStart = il.DefineLabel();
        var keysLoopEnd = il.DefineLabel();
        il.MarkLabel(keysLoopStart);
        il.Emit(OpCodes.Ldloca, keysEnumeratorLocal);
        il.Emit(OpCodes.Call, keysEnumeratorType.GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, keysLoopEnd);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloca, keysEnumeratorLocal);
        il.Emit(OpCodes.Call, keysEnumeratorType.GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, listType.GetMethod("Add")!);
        il.Emit(OpCodes.Br, keysLoopStart);

        il.MarkLabel(keysLoopEnd);
        il.Emit(OpCodes.Ldloca, keysEnumeratorLocal);
        il.Emit(OpCodes.Call, keysEnumeratorType.GetMethod("Dispose")!);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        // Check if obj is List<object?>
        il.MarkLabel(checkListLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, listType);
        il.Emit(OpCodes.Stloc, listLocal);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Brfalse, reflectionLabel);

        // Return indices as strings: Enumerable.Range(0, list.Count).Select(i => (object?)i.ToString()).ToList()
        il.Emit(OpCodes.Newobj, listType.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var listLoopStart = il.DefineLabel();
        var listLoopEnd = il.DefineLabel();
        il.MarkLabel(listLoopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(listType, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, listLoopEnd);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloca, indexLocal);
        il.Emit(OpCodes.Call, _types.Int32.GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Callvirt, listType.GetMethod("Add")!);

        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, listLoopStart);

        il.MarkLabel(listLoopEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        // Reflection for class instances
        il.MarkLabel(reflectionLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, returnEmptyLabel);

        // var type = obj.GetType();
        il.Emit(OpCodes.Newobj, listType.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("GetType")!);
        il.Emit(OpCodes.Stloc, typeLocal);

        // var fields = type.GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Ldc_I4, (int)(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
        il.Emit(OpCodes.Callvirt, _types.Type.GetMethod("GetFields", [_types.BindingFlags])!);
        il.Emit(OpCodes.Stloc, fieldsLocal);

        // foreach (var field in fields) if (field.Name.StartsWith("__")) keys.Add(field.Name.Substring(2));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.MarkLabel(fieldLoopStartLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, fieldsLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, fieldLoopEndLabel);

        il.Emit(OpCodes.Ldloc, fieldsLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, fieldLocal);

        // field.Name
        il.Emit(OpCodes.Ldloc, fieldLocal);
        il.Emit(OpCodes.Callvirt, _types.FieldInfo.GetProperty("Name")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, fieldNameLocal);

        // if (field.Name.StartsWith("__"))
        il.Emit(OpCodes.Ldloc, fieldNameLocal);
        il.Emit(OpCodes.Ldstr, "__");
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("StartsWith", [_types.String])!);
        il.Emit(OpCodes.Brfalse, skipFieldLabel);

        // keys.Add(field.Name.Substring(2));
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, fieldNameLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("Substring", [_types.Int32])!);
        il.Emit(OpCodes.Callvirt, listType.GetMethod("Add")!);

        il.MarkLabel(skipFieldLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, fieldLoopStartLabel);

        il.MarkLabel(fieldLoopEndLabel);

        // Get _fields dictionary and add its keys
        // var fieldsField = type.GetField("_fields", BindingFlags.NonPublic | BindingFlags.Instance);
        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Ldstr, "_fields");
        il.Emit(OpCodes.Ldc_I4, (int)(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
        il.Emit(OpCodes.Callvirt, _types.Type.GetMethod("GetField", [_types.String, _types.BindingFlags])!);
        il.Emit(OpCodes.Stloc, fieldsFieldLocal);

        // if (fieldsField != null && fieldsField.GetValue(obj) is Dictionary<string, object?> fieldsDict)
        il.Emit(OpCodes.Ldloc, fieldsFieldLocal);
        il.Emit(OpCodes.Brfalse, returnResultLabel);

        il.Emit(OpCodes.Ldloc, fieldsFieldLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.FieldInfo.GetMethod("GetValue", [_types.Object])!);
        il.Emit(OpCodes.Isinst, dictType);
        il.Emit(OpCodes.Stloc, fieldsDictLocal);
        il.Emit(OpCodes.Ldloc, fieldsDictLocal);
        il.Emit(OpCodes.Brfalse, returnResultLabel);

        // Add keys from _fields dictionary
        il.Emit(OpCodes.Ldloc, fieldsDictLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(dictType, "Keys").GetGetMethod()!);
        var keysEnumeratorLocal2 = il.DeclareLocal(keysEnumeratorType);
        il.Emit(OpCodes.Callvirt, keysType.GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Stloc, keysEnumeratorLocal2);

        var fieldsKeysLoopStart = il.DefineLabel();
        var fieldsKeysLoopEnd = il.DefineLabel();
        var keyLocal = il.DeclareLocal(_types.String);

        il.MarkLabel(fieldsKeysLoopStart);
        il.Emit(OpCodes.Ldloca, keysEnumeratorLocal2);
        il.Emit(OpCodes.Call, keysEnumeratorType.GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, fieldsKeysLoopEnd);

        il.Emit(OpCodes.Ldloca, keysEnumeratorLocal2);
        il.Emit(OpCodes.Call, keysEnumeratorType.GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, keyLocal);

        // Only add if not already in result (skip duplicates)
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Callvirt, listType.GetMethod("Contains")!);
        var skipDuplicateLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, skipDuplicateLabel);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Callvirt, listType.GetMethod("Add")!);

        il.MarkLabel(skipDuplicateLabel);
        il.Emit(OpCodes.Br, fieldsKeysLoopStart);

        il.MarkLabel(fieldsKeysLoopEnd);
        il.Emit(OpCodes.Ldloca, keysEnumeratorLocal2);
        il.Emit(OpCodes.Call, keysEnumeratorType.GetMethod("Dispose")!);

        il.MarkLabel(returnResultLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        // Return empty list
        il.MarkLabel(returnEmptyLabel);
        il.Emit(OpCodes.Newobj, listType.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitSpreadArray(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SpreadArray",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.Object]
        );
        runtime.SpreadArray = method;

        var il = method.GetILGenerator();
        var listLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brtrue, listLabel);

        // Not a list - return empty
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.EmptyTypes));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(listLabel);
        // Return new list with same elements
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.IEnumerableOfObject));
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits ConcatArrays: concatenates multiple iterables into a single List&lt;object&gt;.
    /// Supports arrays, strings, and custom iterables with Symbol.iterator.
    /// Signature: List&lt;object&gt; ConcatArrays(object[] arrays, $TSSymbol iteratorSymbol, Type runtimeType)
    /// </summary>
    private void EmitConcatArrays(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ConcatArrays",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.ObjectArray, runtime.TSSymbolType, _types.Type]  // Added iteratorSymbol and runtimeType
        );
        runtime.ConcatArrays = method;

        var il = method.GetILGenerator();
        // var result = new List<object>();
        // foreach (var element in arrays) result.AddRange(IterateToList(element, iteratorSymbol, runtimeType));
        // return result;
        var resultLocal = il.DeclareLocal(_types.ListOfObject);
        var indexLocal = il.DeclareLocal(_types.Int32);
        var iteratedLocal = il.DeclareLocal(_types.ListOfObject);  // Result of IterateToList
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.EmptyTypes));
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, loopEnd);

        // Call IterateToList(arrays[index], iteratorSymbol, runtimeType)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldarg_1);  // iteratorSymbol
        il.Emit(OpCodes.Ldarg_2);  // runtimeType
        il.Emit(OpCodes.Call, runtime.IterateToList);
        il.Emit(OpCodes.Stloc, iteratedLocal);

        // result.AddRange(iterated)
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, iteratedLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "AddRange", _types.IEnumerableOfObject));

        // index++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits ExpandCallArgs: expands function call arguments with spread support.
    /// Supports arrays, strings, and custom iterables with Symbol.iterator.
    /// Signature: object[] ExpandCallArgs(object[] args, bool[] isSpread, $TSSymbol iteratorSymbol, Type runtimeType)
    /// </summary>
    private void EmitExpandCallArgs(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ExpandCallArgs",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ObjectArray,
            [_types.ObjectArray, _types.BoolArray, runtime.TSSymbolType, _types.Type]  // Added iteratorSymbol and runtimeType
        );
        runtime.ExpandCallArgs = method;

        var il = method.GetILGenerator();
        // Create result list, iterate args, expand spreads using IterateToList
        var resultLocal = il.DeclareLocal(_types.ListOfObject);
        var indexLocal = il.DeclareLocal(_types.Int32);
        var iteratedLocal = il.DeclareLocal(_types.ListOfObject);  // Result of IterateToList
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.EmptyTypes));
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, loopEnd);

        // Check if this is a spread
        var notSpreadLabel = il.DefineLabel();
        var continueLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_I1);
        il.Emit(OpCodes.Brfalse, notSpreadLabel);

        // Is spread - use IterateToList to handle any iterable
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldarg_2);  // iteratorSymbol
        il.Emit(OpCodes.Ldarg_3);  // runtimeType
        il.Emit(OpCodes.Call, runtime.IterateToList);
        il.Emit(OpCodes.Stloc, iteratedLocal);

        // result.AddRange(iterated)
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, iteratedLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "AddRange", _types.IEnumerableOfObject));
        il.Emit(OpCodes.Br, continueLabel);

        // Not spread - add single element
        il.MarkLabel(notSpreadLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

        il.MarkLabel(continueLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "ToArray", _types.EmptyTypes));
        il.Emit(OpCodes.Ret);
    }
}


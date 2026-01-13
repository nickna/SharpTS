using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitCreateObject(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CreateObject",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.DictionaryStringObject,
            [_types.DictionaryStringObject]
        );
        runtime.CreateObject = method;

        var il = method.GetILGenerator();
        // Just return the dictionary as-is (it's already created)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitMergeIntoObject(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "MergeIntoObject",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.DictionaryStringObject, _types.Object]
        );
        runtime.MergeIntoObject = method;

        var il = method.GetILGenerator();
        var dictLabel = il.DefineLabel();

        // Check if source is dict
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, dictLabel);

        // Not a dict - do nothing
        il.Emit(OpCodes.Ret);

        il.MarkLabel(dictLabel);
        // Iterate and copy
        // We need the Enumerator type for Dictionary<string, object>
        // Since TypeProvider might not expose nested types directly, we resolve it from the Dictionary type
        var dictType = _types.DictionaryStringObject;
        var enumeratorType = typeof(Dictionary<string, object>.Enumerator);
        var keyValuePairType = _types.KeyValuePairStringObject;

        var enumeratorLocal = il.DeclareLocal(enumeratorType);
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, dictType);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(dictType, "GetEnumerator"));
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(enumeratorType, "MoveNext"));
        il.Emit(OpCodes.Brfalse, loopEnd);

        // Get current and add to target
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(enumeratorType, "Current")!.GetGetMethod()!);
        var kvpLocal = il.DeclareLocal(keyValuePairType);
        il.Emit(OpCodes.Stloc, kvpLocal);
        il.Emit(OpCodes.Ldloca, kvpLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(keyValuePairType, "Key")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloca, kvpLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(keyValuePairType, "Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "set_Item", _types.String, _types.Object));

        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ret);
    }

    private void EmitRandom(TypeBuilder typeBuilder, EmittedRuntime runtime, FieldBuilder randomField)
    {
        var method = typeBuilder.DefineMethod(
            "Random",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            _types.EmptyTypes
        );
        runtime.Random = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldsfld, randomField);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Random, "NextDouble"));
        il.Emit(OpCodes.Ret);
    }

    private void EmitGetEnumMemberName(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetEnumMemberName",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.String, _types.Double, _types.DoubleArray, _types.StringArray]
        );
        runtime.GetEnumMemberName = method;

        var il = method.GetILGenerator();
        // Simple linear search through keys to find matching value
        var indexLocal = il.DeclareLocal(_types.Int32);
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, loopEnd);

        // Check if keys[i] == value
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_R8);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ceq);
        var notMatchLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notMatchLabel);

        // Found - return values[i]
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notMatchLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        // Not found - throw
        il.Emit(OpCodes.Ldstr, "Value not found in enum");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);
    }

    private void EmitConcatTemplate(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ConcatTemplate",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.ObjectArray]
        );
        runtime.ConcatTemplate = method;

        var il = method.GetILGenerator();

        // Use StringBuilder to concatenate stringified parts
        var sbLocal = il.DeclareLocal(_types.StringBuilder);
        var indexLocal = il.DeclareLocal(_types.Int32);
        var lengthLocal = il.DeclareLocal(_types.Int32);

        // sb = new StringBuilder()
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.StringBuilder));
        il.Emit(OpCodes.Stloc, sbLocal);

        // length = parts.Length
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, lengthLocal);

        // index = 0
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        // if (index >= length) goto end
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Bge, loopEnd);

        // sb.Append(Stringify(parts[index]))
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop); // discard StringBuilder return value

        // index++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        // return sb.ToString()
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.StringBuilder, "ToString"));
        il.Emit(OpCodes.Ret);
    }

    private void EmitObjectRest(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Accept object instead of Dictionary to support both object literals and class instances
        var method = typeBuilder.DefineMethod(
            "ObjectRest",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.DictionaryStringObject,
            [_types.Object, _types.ListOfObject]
        );
        runtime.ObjectRest = method;

        var il = method.GetILGenerator();

        var dictLabel = il.DefineLabel();
        var emptyLabel = il.DefineLabel();
        var processLabel = il.DefineLabel();

        // Locals for class instance path
        var fieldInfoLocal = il.DeclareLocal(_types.FieldInfo);
        var fieldsLocal = il.DeclareLocal(_types.Object);
        var sourceDictLocal = il.DeclareLocal(_types.DictionaryStringObject);

        // Check if arg0 is Dictionary<string, object>
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, dictLabel);

        // Check if obj is not null (for class instance path)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, emptyLabel);

        // Class instance path: get _fields via reflection
        // var fieldInfo = obj.GetType().GetField("_fields", BindingFlags.NonPublic | BindingFlags.Instance);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "GetType"));
        il.Emit(OpCodes.Ldstr, "_fields");
        il.Emit(OpCodes.Ldc_I4, (int)(BindingFlags.NonPublic | BindingFlags.Instance));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetField", _types.String, _types.BindingFlags));
        il.Emit(OpCodes.Stloc, fieldInfoLocal);

        // if (fieldInfo == null) goto empty
        il.Emit(OpCodes.Ldloc, fieldInfoLocal);
        il.Emit(OpCodes.Brfalse, emptyLabel);

        // var fields = fieldInfo.GetValue(obj);
        il.Emit(OpCodes.Ldloc, fieldInfoLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.FieldInfo, "GetValue", _types.Object));
        il.Emit(OpCodes.Stloc, fieldsLocal);

        // if (fields == null) goto empty
        il.Emit(OpCodes.Ldloc, fieldsLocal);
        il.Emit(OpCodes.Brfalse, emptyLabel);

        // var sourceDict = fields as Dictionary<string, object>;
        il.Emit(OpCodes.Ldloc, fieldsLocal);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, sourceDictLocal);

        // if (sourceDict == null) goto empty
        il.Emit(OpCodes.Ldloc, sourceDictLocal);
        il.Emit(OpCodes.Brfalse, emptyLabel);

        // sourceDict now has the dictionary from class instance
        il.Emit(OpCodes.Br, processLabel);

        // Dictionary path: cast arg0 directly
        il.MarkLabel(dictLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, sourceDictLocal);
        il.Emit(OpCodes.Br, processLabel);

        // Empty result fallback
        il.MarkLabel(emptyLabel);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Ret);

        // Process the source dictionary (now in sourceDictLocal)
        il.MarkLabel(processLabel);

        // Create result dictionary
        var resultLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Stloc, resultLocal);

        // Create HashSet<string> from excludeKeys
        var excludeSetLocal = il.DeclareLocal(_types.HashSetOfString);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.HashSetOfString));
        il.Emit(OpCodes.Stloc, excludeSetLocal);

        // Add each exclude key to the set
        var excludeIndexLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, excludeIndexLocal);

        var excludeLoopStart = il.DefineLabel();
        var excludeLoopEnd = il.DefineLabel();

        il.MarkLabel(excludeLoopStart);
        il.Emit(OpCodes.Ldloc, excludeIndexLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, excludeLoopEnd);

        // Get excludeKeys[i] and add to set if not null
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, excludeIndexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!);
        var keyLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, keyLocal);

        var skipAdd = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Brfalse, skipAdd);

        il.Emit(OpCodes.Ldloc, excludeSetLocal);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.HashSetOfString, "Add", _types.String));
        il.Emit(OpCodes.Pop); // discard bool return

        il.MarkLabel(skipAdd);
        il.Emit(OpCodes.Ldloc, excludeIndexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, excludeIndexLocal);
        il.Emit(OpCodes.Br, excludeLoopStart);

        il.MarkLabel(excludeLoopEnd);

        // Iterate over source dictionary keys using sourceDictLocal
        // We need the KeyCollection.Enumerator
        var keyCollectionType = typeof(Dictionary<string, object>.KeyCollection);
        var keysEnumType = typeof(Dictionary<string, object>.KeyCollection.Enumerator);

        var keysEnumLocal = il.DeclareLocal(keysEnumType);
        il.Emit(OpCodes.Ldloc, sourceDictLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.DictionaryStringObject, "Keys")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(keyCollectionType, "GetEnumerator"));
        il.Emit(OpCodes.Stloc, keysEnumLocal);

        var dictLoopStart = il.DefineLabel();
        var dictLoopEnd = il.DefineLabel();

        il.MarkLabel(dictLoopStart);
        // MoveNext
        il.Emit(OpCodes.Ldloca, keysEnumLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(keysEnumType, "MoveNext"));
        il.Emit(OpCodes.Brfalse, dictLoopEnd);

        // Get Current key
        il.Emit(OpCodes.Ldloca, keysEnumLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(keysEnumType, "Current")!.GetGetMethod()!);
        var currentKeyLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, currentKeyLocal);

        // Check if key is in excludeSet
        il.Emit(OpCodes.Ldloc, excludeSetLocal);
        il.Emit(OpCodes.Ldloc, currentKeyLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.HashSetOfString, "Contains", _types.String));
        var skipKey = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, skipKey);

        // Add to result: result[key] = sourceDict[key]
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, currentKeyLocal);
        il.Emit(OpCodes.Ldloc, sourceDictLocal);
        il.Emit(OpCodes.Ldloc, currentKeyLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.DictionaryStringObject, "Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.DictionaryStringObject, "Item")!.GetSetMethod()!);

        il.MarkLabel(skipKey);
        il.Emit(OpCodes.Br, dictLoopStart);

        il.MarkLabel(dictLoopEnd);

        // Dispose enumerator
        il.Emit(OpCodes.Ldloca, keysEnumLocal);
        il.Emit(OpCodes.Constrained, keysEnumType);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.IDisposable, "Dispose"));

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitGetValues(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetValues",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.Object]
        );
        runtime.GetValues = method;

        var il = method.GetILGenerator();
        var dictType = _types.DictionaryStringObject;
        var listType = _types.ListOfObject;
        var kvpType = _types.KeyValuePairStringObject;
        var enumeratorType = _types.MakeGenericType(typeof(Dictionary<,>.Enumerator).GetGenericTypeDefinition(), _types.String, _types.Object);

        var resultLocal = il.DeclareLocal(listType);
        var dictLocal = il.DeclareLocal(dictType);
        var typeLocal = il.DeclareLocal(_types.Type);
        var fieldsLocal = il.DeclareLocal(_types.FieldInfoArray);
        var indexLocal = il.DeclareLocal(_types.Int32);
        var fieldLocal = il.DeclareLocal(_types.FieldInfo);
        var fieldNameLocal = il.DeclareLocal(_types.String);
        var fieldsFieldLocal = il.DeclareLocal(_types.FieldInfo);
        var fieldsDictLocal = il.DeclareLocal(dictType);
        var seenKeysLocal = il.DeclareLocal(_types.HashSetOfString);
        var enumeratorLocal = il.DeclareLocal(enumeratorType);
        var currentLocal = il.DeclareLocal(kvpType);

        var reflectionLabel = il.DefineLabel();
        var returnEmptyLabel = il.DefineLabel();
        var fieldLoopStartLabel = il.DefineLabel();
        var fieldLoopEndLabel = il.DefineLabel();
        var skipFieldLabel = il.DefineLabel();
        var returnResultLabel = il.DefineLabel();
        var fieldsLoopStart = il.DefineLabel();
        var fieldsLoopEnd = il.DefineLabel();
        var skipDuplicateLabel = il.DefineLabel();

        // if (obj is Dictionary<string, object?> dict)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, dictType);
        il.Emit(OpCodes.Stloc, dictLocal);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Brfalse, reflectionLabel);

        // Create result list and add all values from dictionary
        il.Emit(OpCodes.Newobj, listType.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Callvirt, dictType.GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        var dictLoopStart = il.DefineLabel();
        var dictLoopEnd = il.DefineLabel();
        il.MarkLabel(dictLoopStart);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, dictLoopEnd);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, currentLocal);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, kvpType.GetProperty("Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, listType.GetMethod("Add")!);
        il.Emit(OpCodes.Br, dictLoopStart);

        il.MarkLabel(dictLoopEnd);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetMethod("Dispose")!);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        // Reflection for class instances
        il.MarkLabel(reflectionLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, returnEmptyLabel);

        // var result = new List<object?>(); var seenKeys = new HashSet<string>();
        il.Emit(OpCodes.Newobj, listType.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Newobj, _types.HashSetOfString.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, seenKeysLocal);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("GetType")!);
        il.Emit(OpCodes.Stloc, typeLocal);

        // var fields = type.GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Ldc_I4, (int)(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
        il.Emit(OpCodes.Callvirt, _types.Type.GetMethod("GetFields", [_types.BindingFlags])!);
        il.Emit(OpCodes.Stloc, fieldsLocal);

        // Iterate backing fields
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

        il.Emit(OpCodes.Ldloc, fieldLocal);
        il.Emit(OpCodes.Callvirt, _types.FieldInfo.GetProperty("Name")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, fieldNameLocal);

        // if (field.Name.StartsWith("__"))
        il.Emit(OpCodes.Ldloc, fieldNameLocal);
        il.Emit(OpCodes.Ldstr, "__");
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("StartsWith", [_types.String])!);
        il.Emit(OpCodes.Brfalse, skipFieldLabel);

        // seenKeys.Add(field.Name.Substring(2));
        il.Emit(OpCodes.Ldloc, seenKeysLocal);
        il.Emit(OpCodes.Ldloc, fieldNameLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("Substring", [_types.Int32])!);
        il.Emit(OpCodes.Callvirt, _types.HashSetOfString.GetMethod("Add")!);
        il.Emit(OpCodes.Pop);

        // values.Add(field.GetValue(obj));
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, fieldLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.FieldInfo.GetMethod("GetValue", [_types.Object])!);
        il.Emit(OpCodes.Callvirt, listType.GetMethod("Add")!);

        il.MarkLabel(skipFieldLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, fieldLoopStartLabel);

        il.MarkLabel(fieldLoopEndLabel);

        // Get _fields dictionary
        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Ldstr, "_fields");
        il.Emit(OpCodes.Ldc_I4, (int)(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
        il.Emit(OpCodes.Callvirt, _types.Type.GetMethod("GetField", [_types.String, _types.BindingFlags])!);
        il.Emit(OpCodes.Stloc, fieldsFieldLocal);

        il.Emit(OpCodes.Ldloc, fieldsFieldLocal);
        il.Emit(OpCodes.Brfalse, returnResultLabel);

        il.Emit(OpCodes.Ldloc, fieldsFieldLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.FieldInfo.GetMethod("GetValue", [_types.Object])!);
        il.Emit(OpCodes.Isinst, dictType);
        il.Emit(OpCodes.Stloc, fieldsDictLocal);
        il.Emit(OpCodes.Ldloc, fieldsDictLocal);
        il.Emit(OpCodes.Brfalse, returnResultLabel);

        // Iterate _fields and add values for keys not in seenKeys
        il.Emit(OpCodes.Ldloc, fieldsDictLocal);
        il.Emit(OpCodes.Callvirt, dictType.GetMethod("GetEnumerator")!);
        var fieldsDictEnumeratorLocal = il.DeclareLocal(enumeratorType);
        il.Emit(OpCodes.Stloc, fieldsDictEnumeratorLocal);

        il.MarkLabel(fieldsLoopStart);
        il.Emit(OpCodes.Ldloca, fieldsDictEnumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, fieldsLoopEnd);

        il.Emit(OpCodes.Ldloca, fieldsDictEnumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, currentLocal);

        // if (!seenKeys.Contains(kvp.Key))
        il.Emit(OpCodes.Ldloc, seenKeysLocal);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, kvpType.GetProperty("Key")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.HashSetOfString.GetMethod("Contains")!);
        il.Emit(OpCodes.Brtrue, skipDuplicateLabel);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, kvpType.GetProperty("Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, listType.GetMethod("Add")!);

        il.MarkLabel(skipDuplicateLabel);
        il.Emit(OpCodes.Br, fieldsLoopStart);

        il.MarkLabel(fieldsLoopEnd);
        il.Emit(OpCodes.Ldloca, fieldsDictEnumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetMethod("Dispose")!);

        il.MarkLabel(returnResultLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnEmptyLabel);
        il.Emit(OpCodes.Newobj, listType.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitGetEntries(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetEntries",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.Object]
        );
        runtime.GetEntries = method;

        var il = method.GetILGenerator();
        var dictType = _types.DictionaryStringObject;
        var listType = _types.ListOfObject;
        var kvpType = _types.KeyValuePairStringObject;
        var enumeratorType = _types.MakeGenericType(typeof(Dictionary<,>.Enumerator).GetGenericTypeDefinition(), _types.String, _types.Object);

        var resultLocal = il.DeclareLocal(listType);
        var dictLocal = il.DeclareLocal(dictType);
        var typeLocal = il.DeclareLocal(_types.Type);
        var fieldsLocal = il.DeclareLocal(_types.FieldInfoArray);
        var indexLocal = il.DeclareLocal(_types.Int32);
        var fieldLocal = il.DeclareLocal(_types.FieldInfo);
        var fieldNameLocal = il.DeclareLocal(_types.String);
        var fieldsFieldLocal = il.DeclareLocal(_types.FieldInfo);
        var fieldsDictLocal = il.DeclareLocal(dictType);
        var seenKeysLocal = il.DeclareLocal(_types.HashSetOfString);
        var enumeratorLocal = il.DeclareLocal(enumeratorType);
        var currentLocal = il.DeclareLocal(kvpType);
        var entryLocal = il.DeclareLocal(listType);
        var propNameLocal = il.DeclareLocal(_types.String);

        var reflectionLabel = il.DefineLabel();
        var returnEmptyLabel = il.DefineLabel();
        var fieldLoopStartLabel = il.DefineLabel();
        var fieldLoopEndLabel = il.DefineLabel();
        var skipFieldLabel = il.DefineLabel();
        var returnResultLabel = il.DefineLabel();
        var fieldsLoopStart = il.DefineLabel();
        var fieldsLoopEnd = il.DefineLabel();
        var skipDuplicateLabel = il.DefineLabel();

        // if (obj is Dictionary<string, object?> dict)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, dictType);
        il.Emit(OpCodes.Stloc, dictLocal);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Brfalse, reflectionLabel);

        // Create result list and add [key, value] entries
        il.Emit(OpCodes.Newobj, listType.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Callvirt, dictType.GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        var dictLoopStart = il.DefineLabel();
        var dictLoopEnd = il.DefineLabel();
        il.MarkLabel(dictLoopStart);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, dictLoopEnd);

        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, currentLocal);

        // var entry = new List<object?> { kvp.Key, kvp.Value };
        il.Emit(OpCodes.Newobj, listType.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, entryLocal);
        il.Emit(OpCodes.Ldloc, entryLocal);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, kvpType.GetProperty("Key")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, listType.GetMethod("Add")!);
        il.Emit(OpCodes.Ldloc, entryLocal);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, kvpType.GetProperty("Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, listType.GetMethod("Add")!);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, entryLocal);
        il.Emit(OpCodes.Callvirt, listType.GetMethod("Add")!);
        il.Emit(OpCodes.Br, dictLoopStart);

        il.MarkLabel(dictLoopEnd);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetMethod("Dispose")!);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        // Reflection for class instances
        il.MarkLabel(reflectionLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, returnEmptyLabel);

        il.Emit(OpCodes.Newobj, listType.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Newobj, _types.HashSetOfString.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, seenKeysLocal);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("GetType")!);
        il.Emit(OpCodes.Stloc, typeLocal);

        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Ldc_I4, (int)(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
        il.Emit(OpCodes.Callvirt, _types.Type.GetMethod("GetFields", [_types.BindingFlags])!);
        il.Emit(OpCodes.Stloc, fieldsLocal);

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

        il.Emit(OpCodes.Ldloc, fieldLocal);
        il.Emit(OpCodes.Callvirt, _types.FieldInfo.GetProperty("Name")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, fieldNameLocal);

        il.Emit(OpCodes.Ldloc, fieldNameLocal);
        il.Emit(OpCodes.Ldstr, "__");
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("StartsWith", [_types.String])!);
        il.Emit(OpCodes.Brfalse, skipFieldLabel);

        // propName = field.Name.Substring(2)
        il.Emit(OpCodes.Ldloc, fieldNameLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("Substring", [_types.Int32])!);
        il.Emit(OpCodes.Stloc, propNameLocal);

        il.Emit(OpCodes.Ldloc, seenKeysLocal);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Callvirt, _types.HashSetOfString.GetMethod("Add")!);
        il.Emit(OpCodes.Pop);

        // var entry = new List<object?> { propName, field.GetValue(obj) };
        il.Emit(OpCodes.Newobj, listType.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, entryLocal);
        il.Emit(OpCodes.Ldloc, entryLocal);
        il.Emit(OpCodes.Ldloc, propNameLocal);
        il.Emit(OpCodes.Callvirt, listType.GetMethod("Add")!);
        il.Emit(OpCodes.Ldloc, entryLocal);
        il.Emit(OpCodes.Ldloc, fieldLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.FieldInfo.GetMethod("GetValue", [_types.Object])!);
        il.Emit(OpCodes.Callvirt, listType.GetMethod("Add")!);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, entryLocal);
        il.Emit(OpCodes.Callvirt, listType.GetMethod("Add")!);

        il.MarkLabel(skipFieldLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, fieldLoopStartLabel);

        il.MarkLabel(fieldLoopEndLabel);

        // Get _fields dictionary
        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Ldstr, "_fields");
        il.Emit(OpCodes.Ldc_I4, (int)(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
        il.Emit(OpCodes.Callvirt, _types.Type.GetMethod("GetField", [_types.String, _types.BindingFlags])!);
        il.Emit(OpCodes.Stloc, fieldsFieldLocal);

        il.Emit(OpCodes.Ldloc, fieldsFieldLocal);
        il.Emit(OpCodes.Brfalse, returnResultLabel);

        il.Emit(OpCodes.Ldloc, fieldsFieldLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.FieldInfo.GetMethod("GetValue", [_types.Object])!);
        il.Emit(OpCodes.Isinst, dictType);
        il.Emit(OpCodes.Stloc, fieldsDictLocal);
        il.Emit(OpCodes.Ldloc, fieldsDictLocal);
        il.Emit(OpCodes.Brfalse, returnResultLabel);

        var fieldsDictEnumeratorLocal = il.DeclareLocal(enumeratorType);
        il.Emit(OpCodes.Ldloc, fieldsDictLocal);
        il.Emit(OpCodes.Callvirt, dictType.GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Stloc, fieldsDictEnumeratorLocal);

        il.MarkLabel(fieldsLoopStart);
        il.Emit(OpCodes.Ldloca, fieldsDictEnumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, fieldsLoopEnd);

        il.Emit(OpCodes.Ldloca, fieldsDictEnumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, currentLocal);

        il.Emit(OpCodes.Ldloc, seenKeysLocal);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, kvpType.GetProperty("Key")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.HashSetOfString.GetMethod("Contains")!);
        il.Emit(OpCodes.Brtrue, skipDuplicateLabel);

        il.Emit(OpCodes.Newobj, listType.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, entryLocal);
        il.Emit(OpCodes.Ldloc, entryLocal);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, kvpType.GetProperty("Key")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, listType.GetMethod("Add")!);
        il.Emit(OpCodes.Ldloc, entryLocal);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, kvpType.GetProperty("Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, listType.GetMethod("Add")!);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, entryLocal);
        il.Emit(OpCodes.Callvirt, listType.GetMethod("Add")!);

        il.MarkLabel(skipDuplicateLabel);
        il.Emit(OpCodes.Br, fieldsLoopStart);

        il.MarkLabel(fieldsLoopEnd);
        il.Emit(OpCodes.Ldloca, fieldsDictEnumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetMethod("Dispose")!);

        il.MarkLabel(returnResultLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnEmptyLabel);
        il.Emit(OpCodes.Newobj, listType.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitIsArray(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "IsArray",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]
        );
        runtime.IsArray = method;

        var il = method.GetILGenerator();
        var trueLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // Check if List<object>
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brtrue, trueLabel);

        // False
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Br, endLabel);

        // True
        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldc_I4_1);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }
}


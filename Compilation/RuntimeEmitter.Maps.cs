using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitMapMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitCreateMap(typeBuilder, runtime);
        EmitCreateMapFromEntries(typeBuilder, runtime);
        EmitMapSize(typeBuilder, runtime);
        EmitMapGet(typeBuilder, runtime);
        EmitMapSet(typeBuilder, runtime);
        EmitMapHas(typeBuilder, runtime);
        EmitMapDelete(typeBuilder, runtime);
        EmitMapClear(typeBuilder, runtime);
        EmitMapKeys(typeBuilder, runtime);
        EmitMapValues(typeBuilder, runtime);
        EmitMapEntries(typeBuilder, runtime);
        EmitMapForEach(typeBuilder, runtime);
    }

    private void EmitCreateMap(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CreateMap",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            _types.EmptyTypes
        );
        runtime.CreateMap = method;

        var il = method.GetILGenerator();

        // new Dictionary<object, object?>($ReferenceEqualityComparer.Instance)
        var dictType = _types.DictionaryObjectObject;
        var ctorWithComparer = dictType.GetConstructor([_types.IEqualityComparerOfObject])!;

        il.Emit(OpCodes.Ldsfld, runtime.ReferenceEqualityComparerInstance);
        il.Emit(OpCodes.Newobj, ctorWithComparer);
        il.Emit(OpCodes.Ret);
    }

    private void EmitCreateMapFromEntries(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CreateMapFromEntries",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.CreateMapFromEntries = method;

        var il = method.GetILGenerator();
        var dictType = _types.DictionaryObjectObject;
        var ctorWithComparer = dictType.GetConstructor([_types.IEqualityComparerOfObject])!;

        // Local variables
        var mapLocal = il.DeclareLocal(dictType);
        var listLocal = il.DeclareLocal(_types.ListOfObject);
        var indexLocal = il.DeclareLocal(_types.Int32);
        var entryLocal = il.DeclareLocal(_types.Object);
        var pairLocal = il.DeclareLocal(_types.ListOfObject);
        var keyLocal = il.DeclareLocal(_types.Object);

        // Labels
        var returnMapLabel = il.DefineLabel();
        var loopStartLabel = il.DefineLabel();
        var loopEndLabel = il.DefineLabel();
        var continueLabel = il.DefineLabel();

        // var map = new Dictionary<object, object?>($ReferenceEqualityComparer.Instance)
        il.Emit(OpCodes.Ldsfld, runtime.ReferenceEqualityComparerInstance);
        il.Emit(OpCodes.Newobj, ctorWithComparer);
        il.Emit(OpCodes.Stloc, mapLocal);

        // if (entries is not List<object?> list) return map;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Stloc, listLocal);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Brfalse, returnMapLabel);

        // int index = 0;
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        // Loop: while (index < list.Count)
        il.MarkLabel(loopStartLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEndLabel);

        // var entry = list[index];
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, entryLocal);

        // if (entry is not List<object?> pair) continue;
        il.Emit(OpCodes.Ldloc, entryLocal);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Stloc, pairLocal);
        il.Emit(OpCodes.Ldloc, pairLocal);
        il.Emit(OpCodes.Brfalse, continueLabel);

        // if (pair.Count < 2) continue;
        il.Emit(OpCodes.Ldloc, pairLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Blt, continueLabel);

        // var key = pair[0];
        il.Emit(OpCodes.Ldloc, pairLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, keyLocal);

        // if (key == null) continue;
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Brfalse, continueLabel);

        // map[key] = pair[1];
        il.Emit(OpCodes.Ldloc, mapLocal);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Ldloc, pairLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(dictType, "Item").GetSetMethod()!);

        // index++; goto loopStart;
        il.MarkLabel(continueLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStartLabel);

        il.MarkLabel(loopEndLabel);

        // return map;
        il.MarkLabel(returnMapLabel);
        il.Emit(OpCodes.Ldloc, mapLocal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitMapSize(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "MapSize",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object]
        );
        runtime.MapSize = method;

        var il = method.GetILGenerator();
        var dictType = _types.DictionaryObjectObject;

        var notDictLabel = il.DefineLabel();

        // if (map is Dictionary<object, object?> dict)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, dictType);
        il.Emit(OpCodes.Brfalse, notDictLabel);

        // return (double)dict.Count;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, dictType);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(dictType, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);

        // return 0;
        il.MarkLabel(notDictLabel);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitMapGet(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "MapGet",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );
        runtime.MapGet = method;

        var il = method.GetILGenerator();
        var dictType = _types.DictionaryObjectObject;
        var valueLocal = il.DeclareLocal(_types.Object);

        var returnNullLabel = il.DefineLabel();
        var tryGetLabel = il.DefineLabel();

        // if (map is not Dictionary<object, object?> dict) return null;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, dictType);
        il.Emit(OpCodes.Brfalse, returnNullLabel);

        // if (key == null) return null;
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, returnNullLabel);

        // if (dict.TryGetValue(key, out var value)) return value;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, dictType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, dictType.GetMethod("TryGetValue")!);
        il.Emit(OpCodes.Brfalse, returnNullLabel);

        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ret);

        // return null;
        il.MarkLabel(returnNullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    private void EmitMapSet(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "MapSet",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object, _types.Object]
        );
        runtime.MapSet = method;

        var il = method.GetILGenerator();
        var dictType = _types.DictionaryObjectObject;

        var returnMapLabel = il.DefineLabel();

        // if (map is not Dictionary<object, object?> dict) return map;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, dictType);
        il.Emit(OpCodes.Brfalse, returnMapLabel);

        // if (key == null) return map;
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, returnMapLabel);

        // dict[key] = value;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, dictType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(dictType, "Item").GetSetMethod()!);

        // return map;
        il.MarkLabel(returnMapLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitMapHas(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "MapHas",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.Object]
        );
        runtime.MapHas = method;

        var il = method.GetILGenerator();
        var dictType = _types.DictionaryObjectObject;

        var returnFalseLabel = il.DefineLabel();

        // if (map is not Dictionary<object, object?> dict) return false;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, dictType);
        il.Emit(OpCodes.Brfalse, returnFalseLabel);

        // if (key == null) return false;
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, returnFalseLabel);

        // return dict.ContainsKey(key);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, dictType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, dictType.GetMethod("ContainsKey")!);
        il.Emit(OpCodes.Ret);

        // return false;
        il.MarkLabel(returnFalseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitMapDelete(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "MapDelete",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.Object]
        );
        runtime.MapDelete = method;

        var il = method.GetILGenerator();
        var dictType = _types.DictionaryObjectObject;

        var returnFalseLabel = il.DefineLabel();

        // if (map is not Dictionary<object, object?> dict) return false;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, dictType);
        il.Emit(OpCodes.Brfalse, returnFalseLabel);

        // if (key == null) return false;
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, returnFalseLabel);

        // return dict.Remove(key);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, dictType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, dictType.GetMethod("Remove", [_types.Object])!);
        il.Emit(OpCodes.Ret);

        // return false;
        il.MarkLabel(returnFalseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitMapClear(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "MapClear",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object]
        );
        runtime.MapClear = method;

        var il = method.GetILGenerator();
        var dictType = _types.DictionaryObjectObject;

        var endLabel = il.DefineLabel();

        // if (map is Dictionary<object, object?> dict)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, dictType);
        il.Emit(OpCodes.Brfalse, endLabel);

        // dict.Clear();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, dictType);
        il.Emit(OpCodes.Callvirt, dictType.GetMethod("Clear")!);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }

    private void EmitMapKeys(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "MapKeys",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.Object]
        );
        runtime.MapKeys = method;

        var il = method.GetILGenerator();
        var dictType = _types.DictionaryObjectObject;
        var kvpType = _types.MakeGenericType(_types.KeyValuePairOpen, _types.Object, _types.Object);
        var enumeratorType = _types.MakeGenericType(typeof(Dictionary<,>.Enumerator).GetGenericTypeDefinition(), _types.Object, _types.Object);

        var resultLocal = il.DeclareLocal(_types.ListOfObject);
        var dictLocal = il.DeclareLocal(dictType);
        var enumeratorLocal = il.DeclareLocal(enumeratorType);
        var currentLocal = il.DeclareLocal(kvpType);

        var returnEmptyLabel = il.DefineLabel();
        var loopStartLabel = il.DefineLabel();
        var loopEndLabel = il.DefineLabel();

        // if (map is not Dictionary<object, object?> dict) return new List<object?>();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, dictType);
        il.Emit(OpCodes.Stloc, dictLocal);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Brfalse, returnEmptyLabel);

        // var result = new List<object?>();
        il.Emit(OpCodes.Newobj, _types.ListOfObject.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);

        // var enumerator = dict.GetEnumerator();
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Callvirt, dictType.GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        // while (enumerator.MoveNext())
        il.MarkLabel(loopStartLabel);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, loopEndLabel);

        // var current = enumerator.Current;
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, currentLocal);

        // result.Add(current.Key);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, kvpType.GetProperty("Key")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("Add")!);

        il.Emit(OpCodes.Br, loopStartLabel);

        // Dispose enumerator
        il.MarkLabel(loopEndLabel);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetMethod("Dispose")!);

        // return result;
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        // return new List<object?>();
        il.MarkLabel(returnEmptyLabel);
        il.Emit(OpCodes.Newobj, _types.ListOfObject.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitMapValues(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "MapValues",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.Object]
        );
        runtime.MapValues = method;

        var il = method.GetILGenerator();
        var dictType = _types.DictionaryObjectObject;
        var kvpType = _types.MakeGenericType(_types.KeyValuePairOpen, _types.Object, _types.Object);
        var enumeratorType = _types.MakeGenericType(typeof(Dictionary<,>.Enumerator).GetGenericTypeDefinition(), _types.Object, _types.Object);

        var resultLocal = il.DeclareLocal(_types.ListOfObject);
        var dictLocal = il.DeclareLocal(dictType);
        var enumeratorLocal = il.DeclareLocal(enumeratorType);
        var currentLocal = il.DeclareLocal(kvpType);

        var returnEmptyLabel = il.DefineLabel();
        var loopStartLabel = il.DefineLabel();
        var loopEndLabel = il.DefineLabel();

        // if (map is not Dictionary<object, object?> dict) return new List<object?>();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, dictType);
        il.Emit(OpCodes.Stloc, dictLocal);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Brfalse, returnEmptyLabel);

        // var result = new List<object?>();
        il.Emit(OpCodes.Newobj, _types.ListOfObject.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);

        // var enumerator = dict.GetEnumerator();
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Callvirt, dictType.GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        // while (enumerator.MoveNext())
        il.MarkLabel(loopStartLabel);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, loopEndLabel);

        // var current = enumerator.Current;
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, currentLocal);

        // result.Add(current.Value);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, kvpType.GetProperty("Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("Add")!);

        il.Emit(OpCodes.Br, loopStartLabel);

        // Dispose enumerator
        il.MarkLabel(loopEndLabel);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetMethod("Dispose")!);

        // return result;
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        // return new List<object?>();
        il.MarkLabel(returnEmptyLabel);
        il.Emit(OpCodes.Newobj, _types.ListOfObject.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitMapEntries(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "MapEntries",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.Object]
        );
        runtime.MapEntries = method;

        var il = method.GetILGenerator();
        var dictType = _types.DictionaryObjectObject;
        var kvpType = _types.MakeGenericType(_types.KeyValuePairOpen, _types.Object, _types.Object);
        var enumeratorType = _types.MakeGenericType(typeof(Dictionary<,>.Enumerator).GetGenericTypeDefinition(), _types.Object, _types.Object);

        var resultLocal = il.DeclareLocal(_types.ListOfObject);
        var dictLocal = il.DeclareLocal(dictType);
        var enumeratorLocal = il.DeclareLocal(enumeratorType);
        var pairLocal = il.DeclareLocal(_types.ListOfObject);
        var currentLocal = il.DeclareLocal(kvpType);

        var returnEmptyLabel = il.DefineLabel();
        var loopStartLabel = il.DefineLabel();
        var loopEndLabel = il.DefineLabel();

        // if (map is not Dictionary<object, object?> dict) return new List<object?>();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, dictType);
        il.Emit(OpCodes.Stloc, dictLocal);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Brfalse, returnEmptyLabel);

        // var result = new List<object?>();
        il.Emit(OpCodes.Newobj, _types.ListOfObject.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);

        // var enumerator = dict.GetEnumerator();
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Callvirt, dictType.GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        // while (enumerator.MoveNext())
        il.MarkLabel(loopStartLabel);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, loopEndLabel);

        // var current = enumerator.Current;
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, currentLocal);

        // var pair = new List<object?> { current.Key, current.Value };
        il.Emit(OpCodes.Newobj, _types.ListOfObject.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, pairLocal);

        il.Emit(OpCodes.Ldloc, pairLocal);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, kvpType.GetProperty("Key")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("Add")!);

        il.Emit(OpCodes.Ldloc, pairLocal);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, kvpType.GetProperty("Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("Add")!);

        // result.Add(pair);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, pairLocal);
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("Add")!);

        il.Emit(OpCodes.Br, loopStartLabel);

        // Dispose enumerator
        il.MarkLabel(loopEndLabel);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetMethod("Dispose")!);

        // return result;
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        // return new List<object?>();
        il.MarkLabel(returnEmptyLabel);
        il.Emit(OpCodes.Newobj, _types.ListOfObject.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitMapForEach(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "MapForEach",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object]
        );
        runtime.MapForEach = method;

        var il = method.GetILGenerator();
        var dictType = _types.DictionaryObjectObject;
        var kvpType = _types.MakeGenericType(_types.KeyValuePairOpen, _types.Object, _types.Object);
        var enumeratorType = _types.MakeGenericType(typeof(Dictionary<,>.Enumerator).GetGenericTypeDefinition(), _types.Object, _types.Object);

        var dictLocal = il.DeclareLocal(dictType);
        var enumeratorLocal = il.DeclareLocal(enumeratorType);
        var currentLocal = il.DeclareLocal(kvpType);
        var argsLocal = il.DeclareLocal(_types.ObjectArray);

        var endLabel = il.DefineLabel();
        var loopStartLabel = il.DefineLabel();
        var loopEndLabel = il.DefineLabel();

        // if (map is not Dictionary<object, object?> dict) return;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, dictType);
        il.Emit(OpCodes.Stloc, dictLocal);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Brfalse, endLabel);

        // if (callback == null) return;
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, endLabel);

        // var enumerator = dict.GetEnumerator();
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Callvirt, dictType.GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        // while (enumerator.MoveNext())
        il.MarkLabel(loopStartLabel);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, loopEndLabel);

        // var current = enumerator.Current;
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, currentLocal);

        // InvokeValue(callback, [current.Value, current.Key, map]);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, argsLocal);

        // args[0] = current.Value
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, kvpType.GetProperty("Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Stelem_Ref);

        // args[1] = current.Key
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, kvpType.GetProperty("Key")!.GetGetMethod()!);
        il.Emit(OpCodes.Stelem_Ref);

        // args[2] = map
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stelem_Ref);

        // InvokeValue(callback, args)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, runtime.InvokeValue);
        il.Emit(OpCodes.Pop);  // Discard return value

        il.Emit(OpCodes.Br, loopStartLabel);

        // Dispose enumerator
        il.MarkLabel(loopEndLabel);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, enumeratorType.GetMethod("Dispose")!);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }
}

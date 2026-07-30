using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitMapMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitNormalizeMapKey(typeBuilder, runtime);
        EmitDenormalizeMapKey(typeBuilder, runtime);
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

    /// <summary>
    /// Emits NormalizeMapKey(key): converts a CLR null key to _mapNullSentinel so it can be
    /// stored in a Dictionary (which forbids null keys). undefined ($Undefined.Instance) is
    /// left untouched — it is a distinct singleton and a valid Dictionary key, so null and
    /// undefined remain separate Map keys (matches JS and SharpTSMap; see issue #960).
    /// </summary>
    private void EmitNormalizeMapKey(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "NormalizeMapKey",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.NormalizeMapKey = method;

        var il = method.GetILGenerator();
        var returnSentinelLabel = il.DefineLabel();

        // if (key == null) return _mapNullSentinel;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, returnSentinelLabel);

        // return key;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);

        // return _mapNullSentinel;
        il.MarkLabel(returnSentinelLabel);
        il.Emit(OpCodes.Ldsfld, runtime.MapNullSentinel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits DenormalizeMapKey(key): converts _mapNullSentinel back to null.
    /// </summary>
    private void EmitDenormalizeMapKey(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DenormalizeMapKey",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.DenormalizeMapKey = method;

        var il = method.GetILGenerator();
        var returnNullLabel = il.DefineLabel();

        // if (key == _mapNullSentinel) return null;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, runtime.MapNullSentinel);
        il.Emit(OpCodes.Beq, returnNullLabel);

        // return key;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);

        // return null;
        il.MarkLabel(returnNullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
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
        var ctorWithComparer = _types.GetConstructor(dictType, [_types.IEqualityComparerOfObject])!;

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
        var ctorWithComparer = _types.GetConstructor(dictType, [_types.IEqualityComparerOfObject])!;

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

        // number[] unboxing: materialize a numeric-mode $Array entries source before reading its base list.
        EmitDeoptArgIfNumericArray(il, runtime, 0);

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

        // var key = NormalizeMapKey(pair[0]);
        il.Emit(OpCodes.Ldloc, pairLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!);
        il.Emit(OpCodes.Call, runtime.NormalizeMapKey);
        il.Emit(OpCodes.Stloc, keyLocal);

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

        // if (map is not Dictionary<object, object?> dict) return null;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, dictType);
        il.Emit(OpCodes.Brfalse, returnNullLabel);

        // if (dict.TryGetValue(NormalizeMapKey(key), out var value)) return value;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, dictType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.NormalizeMapKey);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "TryGetValue")!);
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

        // dict[NormalizeMapKey(key)] = value;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, dictType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.NormalizeMapKey);
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

        // return dict.ContainsKey(NormalizeMapKey(key));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, dictType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.NormalizeMapKey);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "ContainsKey")!);
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

        // return dict.Remove(NormalizeMapKey(key));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, dictType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.NormalizeMapKey);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "Remove", [_types.Object])!);
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
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "Clear")!);

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
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);

        // var enumerator = dict.GetEnumerator();
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "GetEnumerator")!);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        // while (enumerator.MoveNext())
        il.MarkLabel(loopStartLabel);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(enumeratorType, "MoveNext")!);
        il.Emit(OpCodes.Brfalse, loopEndLabel);

        // var current = enumerator.Current;
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(enumeratorType, "Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, currentLocal);

        // result.Add(DenormalizeMapKey(current.Key));
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(kvpType, "Key")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, runtime.DenormalizeMapKey);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add")!);

        il.Emit(OpCodes.Br, loopStartLabel);

        // Dispose enumerator
        il.MarkLabel(loopEndLabel);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(enumeratorType, "Dispose")!);

        // return result;
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        // return new List<object?>();
        il.MarkLabel(returnEmptyLabel);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, Type.EmptyTypes)!);
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
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);

        // var enumerator = dict.GetEnumerator();
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "GetEnumerator")!);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        // while (enumerator.MoveNext())
        il.MarkLabel(loopStartLabel);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(enumeratorType, "MoveNext")!);
        il.Emit(OpCodes.Brfalse, loopEndLabel);

        // var current = enumerator.Current;
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(enumeratorType, "Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, currentLocal);

        // result.Add(current.Value);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(kvpType, "Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add")!);

        il.Emit(OpCodes.Br, loopStartLabel);

        // Dispose enumerator
        il.MarkLabel(loopEndLabel);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(enumeratorType, "Dispose")!);

        // return result;
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        // return new List<object?>();
        il.MarkLabel(returnEmptyLabel);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, Type.EmptyTypes)!);
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
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);

        // var enumerator = dict.GetEnumerator();
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "GetEnumerator")!);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        // while (enumerator.MoveNext())
        il.MarkLabel(loopStartLabel);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(enumeratorType, "MoveNext")!);
        il.Emit(OpCodes.Brfalse, loopEndLabel);

        // var current = enumerator.Current;
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(enumeratorType, "Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, currentLocal);

        // var pair = new List<object?> { current.Key, current.Value };
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, pairLocal);

        // pair.Add(DenormalizeMapKey(current.Key))
        il.Emit(OpCodes.Ldloc, pairLocal);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(kvpType, "Key")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, runtime.DenormalizeMapKey);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add")!);

        il.Emit(OpCodes.Ldloc, pairLocal);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(kvpType, "Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add")!);

        // result.Add(pair);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, pairLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add")!);

        il.Emit(OpCodes.Br, loopStartLabel);

        // Dispose enumerator
        il.MarkLabel(loopEndLabel);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(enumeratorType, "Dispose")!);

        // return result;
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        // return new List<object?>();
        il.MarkLabel(returnEmptyLabel);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, Type.EmptyTypes)!);
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
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "GetEnumerator")!);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        // while (enumerator.MoveNext())
        il.MarkLabel(loopStartLabel);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(enumeratorType, "MoveNext")!);
        il.Emit(OpCodes.Brfalse, loopEndLabel);

        // var current = enumerator.Current;
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(enumeratorType, "Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, currentLocal);

        // InvokeValue(callback, [current.Value, current.Key, map]);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, argsLocal);

        // args[0] = current.Value
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(kvpType, "Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Stelem_Ref);

        // args[1] = DenormalizeMapKey(current.Key)
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(kvpType, "Key")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, runtime.DenormalizeMapKey);
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
        il.Emit(OpCodes.Call, _types.GetMethod(enumeratorType, "Dispose")!);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Phase 1: Define $BoundMapMethod type, fields, and constructor.
    /// Wraps a Dictionary<object,object> + method name pair so property access like
    /// `map.get` yields a callable that survives cross-module boundaries and reports
    /// `typeof === 'function'`. Mirrors $BoundArrayMethod.
    /// Must be called before EmitRuntimeClass so GetMapProperty can use the constructor.
    /// </summary>
    internal void EmitBoundMapMethodTypeDefinition(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$BoundMapMethod",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        runtime.BoundMapMethodType = typeBuilder;

        // Assembly visibility so GetProperty's callable-wrapper handler can read
        // `_methodName` to return the method name for `map.get.name === 'get'`.
        var mapField = typeBuilder.DefineField("_map", _types.DictionaryObjectObject, FieldAttributes.Assembly);
        var methodNameField = typeBuilder.DefineField("_methodName", _types.String, FieldAttributes.Assembly);
        runtime.BoundMapMethodMapField = mapField;
        runtime.BoundMapMethodNameField = methodNameField;

        var ctorBuilder = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.DictionaryObjectObject, _types.String]
        );
        runtime.BoundMapMethodCtor = ctorBuilder;

        var ctorIL = ctorBuilder.GetILGenerator();
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_1);
        ctorIL.Emit(OpCodes.Stfld, mapField);
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_2);
        ctorIL.Emit(OpCodes.Stfld, methodNameField);
        ctorIL.Emit(OpCodes.Ret);

        var invokeBuilder = typeBuilder.DefineMethod(
            "Invoke",
            MethodAttributes.Public,
            _types.Object,
            [_types.ObjectArray]
        );
        runtime.BoundMapMethodInvoke = invokeBuilder;
    }

    /// <summary>
    /// Phase 2: Emit Invoke body for $BoundMapMethod and create the type.
    /// Must be called after EmitRuntimeClass so Map* runtime methods exist.
    /// </summary>
    internal void EmitBoundMapMethodFinalize(EmittedRuntime runtime)
    {
        var typeBuilder = runtime.BoundMapMethodType;
        var mapField = runtime.BoundMapMethodMapField;
        var methodNameField = runtime.BoundMapMethodNameField;
        var invokeBuilder = runtime.BoundMapMethodInvoke;

        var il = invokeBuilder.GetILGenerator();
        var endLabel = il.DefineLabel();

        // Helper: emit "if (_methodName == name) { push _map; ...body...; goto endLabel; }"
        void EmitCase(string methodName, Action emitBody)
        {
            var skipLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, methodNameField);
            il.Emit(OpCodes.Ldstr, methodName);
            il.Emit(OpCodes.Call, _types.StringOpEquality);
            il.Emit(OpCodes.Brfalse, skipLabel);

            emitBody();

            il.Emit(OpCodes.Br, endLabel);
            il.MarkLabel(skipLabel);
        }

        // Helper: load args[i] or null if i >= args.Length
        void EmitArgOrNull(int index)
        {
            var elseLabel = il.DefineLabel();
            var doneLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Ldc_I4, index);
            il.Emit(OpCodes.Ble, elseLabel);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4, index);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Br, doneLabel);
            il.MarkLabel(elseLabel);
            il.Emit(OpCodes.Ldnull);
            il.MarkLabel(doneLabel);
        }

        // Each case loads _map then arg(s), calls the helper, and leaves one value on stack.

        // get(key) -> value
        EmitCase("get", () =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, mapField);
            EmitArgOrNull(0);
            il.Emit(OpCodes.Call, runtime.MapGet);
        });

        // set(key, value) -> map (chainable)
        EmitCase("set", () =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, mapField);
            EmitArgOrNull(0);
            EmitArgOrNull(1);
            il.Emit(OpCodes.Call, runtime.MapSet);
        });

        // has(key) -> boolean
        EmitCase("has", () =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, mapField);
            EmitArgOrNull(0);
            il.Emit(OpCodes.Call, runtime.MapHas);
            il.Emit(OpCodes.Box, _types.Boolean);
        });

        // delete(key) -> boolean
        EmitCase("delete", () =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, mapField);
            EmitArgOrNull(0);
            il.Emit(OpCodes.Call, runtime.MapDelete);
            il.Emit(OpCodes.Box, _types.Boolean);
        });

        // clear() -> undefined
        EmitCase("clear", () =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, mapField);
            il.Emit(OpCodes.Call, runtime.MapClear);
            il.Emit(OpCodes.Ldnull);
        });

        // keys() -> iterator
        EmitCase("keys", () =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, mapField);
            il.Emit(OpCodes.Call, runtime.MapKeys);
        });

        // values() -> iterator
        EmitCase("values", () =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, mapField);
            il.Emit(OpCodes.Call, runtime.MapValues);
        });

        // entries() -> iterator
        EmitCase("entries", () =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, mapField);
            il.Emit(OpCodes.Call, runtime.MapEntries);
        });

        // forEach(callback) -> undefined
        EmitCase("forEach", () =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, mapField);
            EmitArgOrNull(0);
            il.Emit(OpCodes.Call, runtime.MapForEach);
            il.Emit(OpCodes.Ldnull);
        });

        // Fallthrough: return null
        il.Emit(OpCodes.Ldnull);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);

        typeBuilder.CreateType();
    }
}

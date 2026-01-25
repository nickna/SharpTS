using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitArrayPop(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayPop",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.ListOfObject]
        );
        runtime.ArrayPop = method;

        var il = method.GetILGenerator();
        var emptyLabel = il.DefineLabel();

        // if (list.Count == 0) return null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Beq, emptyLabel);

        // var last = list[list.Count - 1]
        var lastLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, lastLocal);

        // list.RemoveAt(list.Count - 1)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "RemoveAt", _types.Int32));

        // return last
        il.Emit(OpCodes.Ldloc, lastLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(emptyLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    private void EmitArrayShift(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayShift",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.ListOfObject]
        );
        runtime.ArrayShift = method;

        var il = method.GetILGenerator();
        var emptyLabel = il.DefineLabel();

        // if (list.Count == 0) return null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Beq, emptyLabel);

        // var first = list[0]
        var firstLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, firstLocal);

        // list.RemoveAt(0)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "RemoveAt", _types.Int32));

        // return first
        il.Emit(OpCodes.Ldloc, firstLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(emptyLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    private void EmitArrayUnshift(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayUnshift",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.ListOfObject, _types.Object]
        );
        runtime.ArrayUnshift = method;

        var il = method.GetILGenerator();

        // list.Insert(0, element)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Insert", _types.Int32, _types.Object));

        // return (double)list.Count
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);
    }

    private void EmitArrayPush(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayPush",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.ListOfObject, _types.Object]
        );
        runtime.ArrayPush = method;

        var il = method.GetILGenerator();

        // list.Add(element)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

        // return (double)list.Count
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);
    }

    private void EmitArraySlice(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArraySlice",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.ListOfObject, _types.ObjectArray]
        );
        runtime.ArraySlice = method;

        var il = method.GetILGenerator();

        // For simplicity, call the static helper method in RuntimeTypes
        // This would require the RuntimeTypes class to be available, so instead
        // we'll emit inline IL for a basic implementation

        var startLocal = il.DeclareLocal(_types.Int32);
        var endLocal = il.DeclareLocal(_types.Int32);
        var countLocal = il.DeclareLocal(_types.Int32);

        // count = list.Count
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, countLocal);

        // start = args.Length > 0 ? (int)(double)args[0] : 0
        var noStartArg = il.DefineLabel();
        var startDone = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, noStartArg);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, startLocal);
        il.Emit(OpCodes.Br, startDone);
        il.MarkLabel(noStartArg);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, startLocal);
        il.MarkLabel(startDone);

        // end = args.Length > 1 ? (int)(double)args[1] : count
        var noEndArg = il.DefineLabel();
        var endDone = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ble, noEndArg);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, endLocal);
        il.Emit(OpCodes.Br, endDone);
        il.MarkLabel(noEndArg);
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Stloc, endLocal);
        il.MarkLabel(endDone);

        // Clamp start and end, handle negatives
        // For simplicity, we'll just call GetRange with clamped values
        // if (start < 0) start = max(0, count + start)
        var startNotNeg = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, startNotNeg);
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Max", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, startLocal);
        il.MarkLabel(startNotNeg);

        // if (end < 0) end = max(0, count + end)
        var endNotNeg = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, endNotNeg);
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Max", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, endLocal);
        il.MarkLabel(endNotNeg);

        // Clamp to count
        // if (start > count) start = count
        var startNotOver = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Ble, startNotOver);
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Stloc, startLocal);
        il.MarkLabel(startNotOver);

        // if (end > count) end = count
        var endNotOver = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Ble, endNotOver);
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Stloc, endLocal);
        il.MarkLabel(endNotOver);

        // if (end <= start) return new List<object>()
        var rangeValid = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Bgt, rangeValid);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.EmptyTypes));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(rangeValid);
        // return list.GetRange(start, end - start)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "GetRange", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Ret);
    }

    private void EmitArrayReverse(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayReverse",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.ListOfObject]
        );
        runtime.ArrayReverse = method;

        var il = method.GetILGenerator();

        // list.Reverse()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Reverse", _types.EmptyTypes));

        // return list
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitArrayFlat(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // ArrayFlat(List<object> list, object? depthArg) -> List<object>
        var method = typeBuilder.DefineMethod(
            "ArrayFlat",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.ListOfObject, _types.Object]
        );
        runtime.ArrayFlat = method;

        var il = method.GetILGenerator();

        // Parse depth: default 1, Infinity -> int.MaxValue
        var depthLocal = il.DeclareLocal(_types.Int32);
        var resultLocal = il.DeclareLocal(_types.ListOfObject);

        // if (depthArg == null) depth = 1
        var depthNotNull = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brtrue, depthNotNull);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, depthLocal);
        var depthDone = il.DefineLabel();
        il.Emit(OpCodes.Br, depthDone);

        il.MarkLabel(depthNotNull);
        // depth = (int)(double)depthArg, handle Infinity
        var notInfinity = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Call, typeof(double).GetMethod("IsPositiveInfinity", [typeof(double)])!);
        il.Emit(OpCodes.Brfalse, notInfinity);
        il.Emit(OpCodes.Ldc_I4, int.MaxValue);
        il.Emit(OpCodes.Stloc, depthLocal);
        il.Emit(OpCodes.Br, depthDone);

        il.MarkLabel(notInfinity);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, depthLocal);

        il.MarkLabel(depthDone);

        // result = new List<object>()
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.EmptyTypes));
        il.Emit(OpCodes.Stloc, resultLocal);

        // Call helper: FlattenHelper(list, result, depth)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, depthLocal);
        il.Emit(OpCodes.Call, runtime.ArrayFlatHelper);

        // return result
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitArrayFlatHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // FlattenHelper(List<object> source, List<object> result, int depth) -> void
        var method = typeBuilder.DefineMethod(
            "ArrayFlatHelper",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.ListOfObject, _types.ListOfObject, _types.Int32]
        );
        runtime.ArrayFlatHelper = method;

        var il = method.GetILGenerator();

        var iLocal = il.DeclareLocal(_types.Int32);
        var itemLocal = il.DeclareLocal(_types.Object);
        var listAsListLocal = il.DeclareLocal(_types.ListOfObject);

        // for (int i = 0; i < source.Count; i++)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var loopCondition = il.DefineLabel();

        il.Emit(OpCodes.Br, loopCondition);

        il.MarkLabel(loopStart);
        // item = source[i]
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", _types.Int32));
        il.Emit(OpCodes.Stloc, itemLocal);

        // if (depth > 0 && item is List<object> nestedList)
        var addDirectly = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2); // depth
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, addDirectly);

        il.Emit(OpCodes.Ldloc, itemLocal);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Stloc, listAsListLocal);
        il.Emit(OpCodes.Brfalse, addDirectly);

        // FlattenHelper(nestedList, result, depth - 1)
        il.Emit(OpCodes.Ldloc, listAsListLocal);
        il.Emit(OpCodes.Ldarg_1); // result
        il.Emit(OpCodes.Ldarg_2); // depth
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Call, method); // recursive call
        var continueLoop = il.DefineLabel();
        il.Emit(OpCodes.Br, continueLoop);

        // else: result.Add(item)
        il.MarkLabel(addDirectly);
        il.Emit(OpCodes.Ldarg_1); // result
        il.Emit(OpCodes.Ldloc, itemLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

        il.MarkLabel(continueLoop);
        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(loopCondition);
        // i < source.Count
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Count"));
        il.Emit(OpCodes.Blt, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ret);
    }

    private void EmitArrayFlatMap(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // ArrayFlatMap(List<object> list, object callback) -> List<object>
        var method = typeBuilder.DefineMethod(
            "ArrayFlatMap",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.ListOfObject, _types.Object]
        );
        runtime.ArrayFlatMap = method;

        var il = method.GetILGenerator();

        var resultLocal = il.DeclareLocal(_types.ListOfObject);
        var iLocal = il.DeclareLocal(_types.Int32);
        var callResultLocal = il.DeclareLocal(_types.Object);
        var nestedListLocal = il.DeclareLocal(_types.ListOfObject);

        // result = new List<object>()
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.EmptyTypes));
        il.Emit(OpCodes.Stloc, resultLocal);

        // for (int i = 0; i < list.Count; i++)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        var loopStart = il.DefineLabel();
        var loopCondition = il.DefineLabel();

        il.Emit(OpCodes.Br, loopCondition);

        il.MarkLabel(loopStart);

        var argsLocal = il.DeclareLocal(_types.ObjectArray);

        // Build args array: [list[i], (double)i, list]
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Newarr, _types.Object);

        // args[0] = list[i]
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", _types.Int32));
        il.Emit(OpCodes.Stelem_Ref);

        // args[1] = (double)i
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Stelem_Ref);

        // args[2] = list
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stelem_Ref);

        // Store args array in local
        il.Emit(OpCodes.Stloc, argsLocal);

        // callResult = InvokeValue(callback, args)
        il.Emit(OpCodes.Ldarg_1); // callback - first arg
        il.Emit(OpCodes.Ldloc, argsLocal); // args - second arg
        il.Emit(OpCodes.Call, runtime.InvokeValue);
        il.Emit(OpCodes.Stloc, callResultLocal);

        // if (callResult is List<object> nestedList)
        var addDirectly = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, callResultLocal);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Stloc, nestedListLocal);
        il.Emit(OpCodes.Brfalse, addDirectly);

        // result.AddRange(nestedList)
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, nestedListLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object?>).GetMethod("AddRange", [typeof(IEnumerable<object?>)])!);
        var continueLoop = il.DefineLabel();
        il.Emit(OpCodes.Br, continueLoop);

        // else: result.Add(callResult)
        il.MarkLabel(addDirectly);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, callResultLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

        il.MarkLabel(continueLoop);
        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(loopCondition);
        // i < list.Count
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Count"));
        il.Emit(OpCodes.Blt, loopStart);

        // return result
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitArraySort(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // ArraySort(List<object> list, object? compareFn) -> List<object>
        // Mutates the list in-place, returns the same list reference
        var method = typeBuilder.DefineMethod(
            "ArraySort",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.ListOfObject, _types.Object]
        );
        runtime.ArraySort = method;

        var il = method.GetILGenerator();

        // Use a simple in-place insertion sort for stability
        // This is efficient enough for typical use cases and guarantees stability
        EmitSortBody(il, runtime, mutateInPlace: true);
    }

    private void EmitArrayToSorted(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // ArrayToSorted(List<object> list, object? compareFn) -> List<object>
        // Returns a NEW sorted list, original is unchanged
        var method = typeBuilder.DefineMethod(
            "ArrayToSorted",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.ListOfObject, _types.Object]
        );
        runtime.ArrayToSorted = method;

        var il = method.GetILGenerator();

        // Create a copy of the list first
        var copyLocal = il.DeclareLocal(_types.ListOfObject);

        // var copy = new List<object>(list)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, typeof(List<object?>).GetConstructor([typeof(IEnumerable<object?>)])!);
        il.Emit(OpCodes.Stloc, copyLocal);

        // Now sort the copy using the same logic as EmitArraySort
        // We need to emit sort body but use copyLocal instead of arg0
        EmitSortBodyOnLocal(il, runtime, copyLocal);
    }

    /// <summary>
    /// Emits the body of the sort algorithm (stable insertion sort).
    /// When mutateInPlace is true, sorts arg0 and returns arg0.
    /// </summary>
    private void EmitSortBody(ILGenerator il, EmittedRuntime runtime, bool mutateInPlace)
    {
        var listLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, listLocal);

        EmitSortBodyOnLocal(il, runtime, listLocal);
    }

    /// <summary>
    /// Emits the sort body operating on a local variable (for toSorted which creates a copy).
    /// JavaScript spec: undefined values are always moved to end, never passed to compareFn.
    /// </summary>
    private void EmitSortBodyOnLocal(ILGenerator il, EmittedRuntime runtime, LocalBuilder listLocal)
    {
        // JavaScript sort algorithm:
        // 1. Partition: separate defined values from undefined values
        // 2. Sort only the defined values
        // 3. Append undefined values at the end

        var definedLocal = il.DeclareLocal(_types.ListOfObject);      // List of defined elements
        var undefinedCountLocal = il.DeclareLocal(_types.Int32);       // Count of undefined elements
        var iLocal = il.DeclareLocal(_types.Int32);
        var jLocal = il.DeclareLocal(_types.Int32);
        var tempLocal = il.DeclareLocal(_types.Object);
        var compareResultLocal = il.DeclareLocal(_types.Int32);
        var str1Local = il.DeclareLocal(_types.String);
        var str2Local = il.DeclareLocal(_types.String);
        var elementLocal = il.DeclareLocal(_types.Object);

        // === Phase 1: Partition defined vs undefined ===
        // defined = new List<object>()
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.EmptyTypes));
        il.Emit(OpCodes.Stloc, definedLocal);

        // undefinedCount = 0
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, undefinedCountLocal);

        // for (i = 0; i < list.Count; i++)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        var partitionLoopStart = il.DefineLabel();
        var partitionLoopCondition = il.DefineLabel();
        var isUndefinedLabel = il.DefineLabel();
        var partitionNext = il.DefineLabel();

        il.Emit(OpCodes.Br, partitionLoopCondition);

        il.MarkLabel(partitionLoopStart);
        // element = list[i]
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, elementLocal);

        // if (element is $Undefined) undefinedCount++ else defined.Add(element)
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, isUndefinedLabel);

        // Not undefined: defined.Add(element)
        il.Emit(OpCodes.Ldloc, definedLocal);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));
        il.Emit(OpCodes.Br, partitionNext);

        // Is undefined: undefinedCount++
        il.MarkLabel(isUndefinedLabel);
        il.Emit(OpCodes.Ldloc, undefinedCountLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, undefinedCountLocal);

        il.MarkLabel(partitionNext);
        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(partitionLoopCondition);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Blt, partitionLoopStart);

        // === Phase 2: Sort defined elements (stable insertion sort) ===
        // for (i = 1; i < defined.Count; i++)
        //     for (j = i; j > 0 && Compare(defined[j-1], defined[j]) > 0; j--)
        //         Swap(defined, j-1, j)

        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, iLocal);

        var outerLoopStart = il.DefineLabel();
        var outerLoopCondition = il.DefineLabel();
        var innerLoopStart = il.DefineLabel();
        var innerLoopCondition = il.DefineLabel();
        var incrementI = il.DefineLabel();

        il.Emit(OpCodes.Br, outerLoopCondition);

        // Outer loop body
        il.MarkLabel(outerLoopStart);

        // j = i
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Stloc, jLocal);

        il.Emit(OpCodes.Br, innerLoopCondition);

        // Inner loop body - swap if needed
        il.MarkLabel(innerLoopStart);

        // Swap defined[j-1] and defined[j]
        // temp = defined[j]
        il.Emit(OpCodes.Ldloc, definedLocal);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, tempLocal);

        // defined[j] = defined[j-1]
        il.Emit(OpCodes.Ldloc, definedLocal);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Ldloc, definedLocal);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetSetMethod()!);

        // defined[j-1] = temp
        il.Emit(OpCodes.Ldloc, definedLocal);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldloc, tempLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetSetMethod()!);

        // j--
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, jLocal);

        // Inner loop condition: j > 0 && Compare(defined[j-1], defined[j]) > 0
        il.MarkLabel(innerLoopCondition);

        // Check j > 0
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, incrementI);

        // Check compareFn (arg1)
        var hasCompareFn = il.DefineLabel();
        var checkCompareResult = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brtrue, hasCompareFn);

        // Default comparison: Stringify(a).CompareTo(Stringify(b)) using String.CompareOrdinal
        // str1 = Stringify(defined[j-1])
        il.Emit(OpCodes.Ldloc, definedLocal);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Stloc, str1Local);

        // str2 = Stringify(defined[j])
        il.Emit(OpCodes.Ldloc, definedLocal);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Stloc, str2Local);

        // compareResult = String.CompareOrdinal(str1, str2)
        il.Emit(OpCodes.Ldloc, str1Local);
        il.Emit(OpCodes.Ldloc, str2Local);
        il.Emit(OpCodes.Call, typeof(string).GetMethod("CompareOrdinal", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Stloc, compareResultLocal);
        il.Emit(OpCodes.Br, checkCompareResult);

        // Custom compareFn: call InvokeValue(compareFn, [a, b])
        il.MarkLabel(hasCompareFn);

        // Build args array [defined[j-1], defined[j]]
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);

        // args[0] = defined[j-1]
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, definedLocal);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!);
        il.Emit(OpCodes.Stelem_Ref);

        // args[1] = defined[j]
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, definedLocal);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!);
        il.Emit(OpCodes.Stelem_Ref);

        var argsLocal = il.DeclareLocal(_types.ObjectArray);
        il.Emit(OpCodes.Stloc, argsLocal);

        // result = InvokeValue(compareFn, args)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, runtime.InvokeValue);

        // Convert result to int comparison value
        // If result is double, use sign; if 0 or NaN, don't swap (stability)
        var resultIsNotDouble = il.DefineLabel();
        var resultLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, resultLocal);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, resultIsNotDouble);

        // It's a double - check if > 0
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        var doubleResultLocal = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Stloc, doubleResultLocal);

        // Check for NaN (NaN means no swap for stability)
        il.Emit(OpCodes.Ldloc, doubleResultLocal);
        il.Emit(OpCodes.Call, typeof(double).GetMethod("IsNaN", [typeof(double)])!);
        var notNaN = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notNaN);
        il.Emit(OpCodes.Ldc_I4_0);  // NaN -> 0 (no swap)
        il.Emit(OpCodes.Stloc, compareResultLocal);
        il.Emit(OpCodes.Br, checkCompareResult);

        il.MarkLabel(notNaN);
        // Convert double to int sign
        il.Emit(OpCodes.Ldloc, doubleResultLocal);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        var isZero = il.DefineLabel();
        var isPositive = il.DefineLabel();
        il.Emit(OpCodes.Beq, isZero);

        il.Emit(OpCodes.Ldloc, doubleResultLocal);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Bgt, isPositive);

        // Negative
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Stloc, compareResultLocal);
        il.Emit(OpCodes.Br, checkCompareResult);

        il.MarkLabel(isPositive);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, compareResultLocal);
        il.Emit(OpCodes.Br, checkCompareResult);

        il.MarkLabel(isZero);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, compareResultLocal);
        il.Emit(OpCodes.Br, checkCompareResult);

        il.MarkLabel(resultIsNotDouble);
        // Not a double - treat as 0 (no swap)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, compareResultLocal);

        il.MarkLabel(checkCompareResult);
        // If compareResult > 0, swap (continue inner loop)
        il.Emit(OpCodes.Ldloc, compareResultLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bgt, innerLoopStart);

        il.MarkLabel(incrementI);
        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);

        // Outer loop condition: i < defined.Count
        il.MarkLabel(outerLoopCondition);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, definedLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Blt, outerLoopStart);

        // === Phase 3: Rebuild original list with sorted defined + undefined at end ===
        // list.Clear()
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Clear"));

        // list.AddRange(defined)
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, definedLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object?>).GetMethod("AddRange", [typeof(IEnumerable<object?>)])!);

        // for (i = 0; i < undefinedCount; i++) list.Add($Undefined.Instance)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        var appendLoopStart = il.DefineLabel();
        var appendLoopCondition = il.DefineLabel();

        il.Emit(OpCodes.Br, appendLoopCondition);

        il.MarkLabel(appendLoopStart);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(appendLoopCondition);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, undefinedCountLocal);
        il.Emit(OpCodes.Blt, appendLoopStart);

        // Return the list
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Helper method implementing JavaScript's ToIntegerOrInfinity algorithm.
    /// Used by splice/toSpliced for argument coercion.
    /// </summary>
    private void EmitToIntegerOrInfinityHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // ToIntegerOrInfinity(object? value, int defaultValue) -> int
        var method = typeBuilder.DefineMethod(
            "ToIntegerOrInfinity",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Int32,
            [_types.Object, _types.Int32]
        );
        runtime.ToIntegerOrInfinity = method;

        var il = method.GetILGenerator();

        var valueIsNull = il.DefineLabel();
        var returnDefault = il.DefineLabel();
        var isDouble = il.DefineLabel();
        var notNaN = il.DefineLabel();
        var notPosInf = il.DefineLabel();
        var notNegInf = il.DefineLabel();

        // if (value == null) return defaultValue
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, returnDefault);

        // if (value is double d)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, returnDefault);

        // Get the double value
        var doubleLocal = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Stloc, doubleLocal);

        // if (double.IsNaN(d)) return 0
        il.Emit(OpCodes.Ldloc, doubleLocal);
        il.Emit(OpCodes.Call, typeof(double).GetMethod("IsNaN", [typeof(double)])!);
        il.Emit(OpCodes.Brfalse, notNaN);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notNaN);
        // if (double.IsPositiveInfinity(d)) return int.MaxValue
        il.Emit(OpCodes.Ldloc, doubleLocal);
        il.Emit(OpCodes.Call, typeof(double).GetMethod("IsPositiveInfinity", [typeof(double)])!);
        il.Emit(OpCodes.Brfalse, notPosInf);
        il.Emit(OpCodes.Ldc_I4, int.MaxValue);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notPosInf);
        // if (double.IsNegativeInfinity(d)) return int.MinValue
        il.Emit(OpCodes.Ldloc, doubleLocal);
        il.Emit(OpCodes.Call, typeof(double).GetMethod("IsNegativeInfinity", [typeof(double)])!);
        il.Emit(OpCodes.Brfalse, notNegInf);
        il.Emit(OpCodes.Ldc_I4, int.MinValue);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notNegInf);
        // return (int)Math.Truncate(d)
        il.Emit(OpCodes.Ldloc, doubleLocal);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Truncate", [typeof(double)])!);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnDefault);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ret);
    }

    private void EmitArraySplice(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // ArraySplice(List<object> list, object[] args) -> List<object>
        var method = typeBuilder.DefineMethod(
            "ArraySplice",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.ListOfObject, _types.ObjectArray]
        );
        runtime.ArraySplice = method;

        var il = method.GetILGenerator();

        // Local variables
        var lenLocal = il.DeclareLocal(_types.Int32);
        var actualStartLocal = il.DeclareLocal(_types.Int32);
        var relStartLocal = il.DeclareLocal(_types.Int32);
        var actualDeleteCountLocal = il.DeclareLocal(_types.Int32);
        var deletedLocal = il.DeclareLocal(_types.ListOfObject);
        var iLocal = il.DeclareLocal(_types.Int32);

        // len = list.Count
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, lenLocal);

        // if (args.Length == 0) return new List<object>()
        var hasArgs = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bgt, hasArgs);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.EmptyTypes));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(hasArgs);

        // Parse start: relStart = ToIntegerOrInfinity(args[0], 0)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, runtime.ToIntegerOrInfinity);
        il.Emit(OpCodes.Stloc, relStartLocal);

        // actualStart = relStart < 0 ? Max(len + relStart, 0) : Min(relStart, len)
        var startNotNegative = il.DefineLabel();
        var startDone = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, relStartLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, startNotNegative);

        // Negative: Max(len + relStart, 0)
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Ldloc, relStartLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Max", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, actualStartLocal);
        il.Emit(OpCodes.Br, startDone);

        il.MarkLabel(startNotNegative);
        // Non-negative: Min(relStart, len)
        il.Emit(OpCodes.Ldloc, relStartLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Min", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, actualStartLocal);

        il.MarkLabel(startDone);

        // Parse deleteCount
        var hasDeleteCount = il.DefineLabel();
        var deleteCountDone = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Bgt, hasDeleteCount);

        // No deleteCount: delete to end
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Ldloc, actualStartLocal);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, actualDeleteCountLocal);
        il.Emit(OpCodes.Br, deleteCountDone);

        il.MarkLabel(hasDeleteCount);
        // Has deleteCount: dc = ToIntegerOrInfinity(args[1], 0)
        // actualDeleteCount = Max(0, Min(dc, len - actualStart))
        var dcLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, runtime.ToIntegerOrInfinity);
        il.Emit(OpCodes.Stloc, dcLocal);

        // Min(dc, len - actualStart)
        il.Emit(OpCodes.Ldloc, dcLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Ldloc, actualStartLocal);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Min", _types.Int32, _types.Int32));
        // Max(0, ...)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Max", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, actualDeleteCountLocal);

        il.MarkLabel(deleteCountDone);

        // deleted = list.GetRange(actualStart, actualDeleteCount)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, actualStartLocal);
        il.Emit(OpCodes.Ldloc, actualDeleteCountLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "GetRange", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, deletedLocal);

        // list.RemoveRange(actualStart, actualDeleteCount)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, actualStartLocal);
        il.Emit(OpCodes.Ldloc, actualDeleteCountLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "RemoveRange", _types.Int32, _types.Int32));

        // if (args.Length > 2) insert items
        var noInsert = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ble, noInsert);

        // Insert items from args[2..] at actualStart
        // for (i = args.Length - 1; i >= 2; i--) list.Insert(actualStart, args[i])
        // (Insert in reverse order to maintain order)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, iLocal);

        var insertLoopStart = il.DefineLabel();
        var insertLoopCondition = il.DefineLabel();
        il.Emit(OpCodes.Br, insertLoopCondition);

        il.MarkLabel(insertLoopStart);
        // list.Insert(actualStart, args[i])
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, actualStartLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Insert", _types.Int32, _types.Object));

        // i--
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(insertLoopCondition);
        // i >= 2
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Bge, insertLoopStart);

        il.MarkLabel(noInsert);

        // return deleted
        il.Emit(OpCodes.Ldloc, deletedLocal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitArrayToReversed(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // ArrayToReversed(List<object> list) -> List<object>
        // Returns a NEW reversed list, original is unchanged
        var method = typeBuilder.DefineMethod(
            "ArrayToReversed",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.ListOfObject]
        );
        runtime.ArrayToReversed = method;

        var il = method.GetILGenerator();

        var resultLocal = il.DeclareLocal(_types.ListOfObject);
        var iLocal = il.DeclareLocal(_types.Int32);

        // result = new List<object>()
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.EmptyTypes));
        il.Emit(OpCodes.Stloc, resultLocal);

        // i = list.Count - 1
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, iLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        // Loop: for (int i = list.Count - 1; i >= 0; i--)
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, loopEnd);

        // result.Add(list[i])
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

        // i--
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitArrayWith(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // ArrayWith(List<object> list, object[] args) -> List<object>
        // args[0] = index, args[1] = value
        // Returns a NEW list with element at index replaced
        var method = typeBuilder.DefineMethod(
            "ArrayWith",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.ListOfObject, _types.ObjectArray]
        );
        runtime.ArrayWith = method;

        var il = method.GetILGenerator();

        var lenLocal = il.DeclareLocal(_types.Int32);
        var indexLocal = il.DeclareLocal(_types.Int32);
        var actualIndexLocal = il.DeclareLocal(_types.Int32);
        var resultLocal = il.DeclareLocal(_types.ListOfObject);

        // len = list.Count
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, lenLocal);

        // index = ToIntegerOrInfinity(args[0], 0)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, runtime.ToIntegerOrInfinity);
        il.Emit(OpCodes.Stloc, indexLocal);

        // actualIndex = index < 0 ? len + index : index
        var indexNotNegative = il.DefineLabel();
        var indexDone = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, indexNotNegative);

        // Negative: len + index
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, actualIndexLocal);
        il.Emit(OpCodes.Br, indexDone);

        il.MarkLabel(indexNotNegative);
        // Non-negative: use index directly
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Stloc, actualIndexLocal);

        il.MarkLabel(indexDone);

        // if (actualIndex < 0 || actualIndex >= len) throw RangeError
        var throwRangeError = il.DefineLabel();
        var validIndex = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, actualIndexLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, throwRangeError);

        il.Emit(OpCodes.Ldloc, actualIndexLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Bge, throwRangeError);
        il.Emit(OpCodes.Br, validIndex);

        // Throw RangeError
        il.MarkLabel(throwRangeError);
        il.Emit(OpCodes.Ldstr, "RangeError: Invalid index for with()");
        il.Emit(OpCodes.Newobj, typeof(Exception).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(validIndex);

        // result = new List<object>(list)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, typeof(List<object?>).GetConstructor([typeof(IEnumerable<object?>)])!);
        il.Emit(OpCodes.Stloc, resultLocal);

        // result[actualIndex] = args[1]
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, actualIndexLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetSetMethod()!);

        // return result
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitArrayToSpliced(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // ArrayToSpliced(List<object> list, object[] args) -> List<object>
        var method = typeBuilder.DefineMethod(
            "ArrayToSpliced",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.ListOfObject, _types.ObjectArray]
        );
        runtime.ArrayToSpliced = method;

        var il = method.GetILGenerator();

        // Local variables
        var lenLocal = il.DeclareLocal(_types.Int32);
        var actualStartLocal = il.DeclareLocal(_types.Int32);
        var relStartLocal = il.DeclareLocal(_types.Int32);
        var actualSkipCountLocal = il.DeclareLocal(_types.Int32);
        var resultLocal = il.DeclareLocal(_types.ListOfObject);
        var iLocal = il.DeclareLocal(_types.Int32);

        // len = list.Count
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, lenLocal);

        // if (args.Length == 0) return new List<object>(list)
        var hasArgs = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bgt, hasArgs);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, typeof(List<object?>).GetConstructor([typeof(IEnumerable<object?>)])!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(hasArgs);

        // Parse start: relStart = ToIntegerOrInfinity(args[0], 0)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, runtime.ToIntegerOrInfinity);
        il.Emit(OpCodes.Stloc, relStartLocal);

        // actualStart = relStart < 0 ? Max(len + relStart, 0) : Min(relStart, len)
        var startNotNegative = il.DefineLabel();
        var startDone = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, relStartLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, startNotNegative);

        // Negative: Max(len + relStart, 0)
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Ldloc, relStartLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Max", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, actualStartLocal);
        il.Emit(OpCodes.Br, startDone);

        il.MarkLabel(startNotNegative);
        // Non-negative: Min(relStart, len)
        il.Emit(OpCodes.Ldloc, relStartLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Min", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, actualStartLocal);

        il.MarkLabel(startDone);

        // Parse skipCount
        var hasSkipCount = il.DefineLabel();
        var skipCountDone = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Bgt, hasSkipCount);

        // No skipCount: skip to end
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Ldloc, actualStartLocal);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, actualSkipCountLocal);
        il.Emit(OpCodes.Br, skipCountDone);

        il.MarkLabel(hasSkipCount);
        // Has skipCount: sc = ToIntegerOrInfinity(args[1], 0)
        // actualSkipCount = Max(0, Min(sc, len - actualStart))
        var scLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, runtime.ToIntegerOrInfinity);
        il.Emit(OpCodes.Stloc, scLocal);

        // Min(sc, len - actualStart)
        il.Emit(OpCodes.Ldloc, scLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Ldloc, actualStartLocal);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Min", _types.Int32, _types.Int32));
        // Max(0, ...)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Max", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, actualSkipCountLocal);

        il.MarkLabel(skipCountDone);

        // result = new List<object>()
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.EmptyTypes));
        il.Emit(OpCodes.Stloc, resultLocal);

        // Add elements before actualStart: for (i = 0; i < actualStart; i++) result.Add(list[i])
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        var beforeLoopStart = il.DefineLabel();
        var beforeLoopCondition = il.DefineLabel();
        il.Emit(OpCodes.Br, beforeLoopCondition);

        il.MarkLabel(beforeLoopStart);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(beforeLoopCondition);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, actualStartLocal);
        il.Emit(OpCodes.Blt, beforeLoopStart);

        // Add inserted items from args[2..]
        var noInsert = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ble, noInsert);

        // for (i = 2; i < args.Length; i++) result.Add(args[i])
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Stloc, iLocal);

        var insertLoopStart = il.DefineLabel();
        var insertLoopCondition = il.DefineLabel();
        il.Emit(OpCodes.Br, insertLoopCondition);

        il.MarkLabel(insertLoopStart);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(insertLoopCondition);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Blt, insertLoopStart);

        il.MarkLabel(noInsert);

        // Add elements after actualStart + actualSkipCount
        // for (i = actualStart + actualSkipCount; i < len; i++) result.Add(list[i])
        il.Emit(OpCodes.Ldloc, actualStartLocal);
        il.Emit(OpCodes.Ldloc, actualSkipCountLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);

        var afterLoopStart = il.DefineLabel();
        var afterLoopCondition = il.DefineLabel();
        il.Emit(OpCodes.Br, afterLoopCondition);

        il.MarkLabel(afterLoopStart);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(afterLoopCondition);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Blt, afterLoopStart);

        // return result
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }
}


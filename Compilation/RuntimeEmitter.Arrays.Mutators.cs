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
}


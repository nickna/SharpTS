using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace SharpTS.Compilation;

public static partial class RuntimeEmitter
{
    private static void EmitCreateArray(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CreateArray",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(List<object>),
            [typeof(object[])]
        );
        runtime.CreateArray = method;

        var il = method.GetILGenerator();
        // new List<object>(elements)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, typeof(List<object>).GetConstructor([typeof(IEnumerable<object>)])!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitGetLength(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetLength",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(int),
            [typeof(object)]
        );
        runtime.GetLength = method;

        var il = method.GetILGenerator();
        var listLabel = il.DefineLabel();
        var stringLabel = il.DefineLabel();
        var defaultLabel = il.DefineLabel();

        // List
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(List<object>));
        il.Emit(OpCodes.Brtrue, listLabel);

        // String
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(string));
        il.Emit(OpCodes.Brtrue, stringLabel);

        // Default
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(listLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(List<object>));
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(stringLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(string));
        il.Emit(OpCodes.Callvirt, typeof(string).GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitGetElement(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetElement",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            [typeof(object), typeof(int)]
        );
        runtime.GetElement = method;

        var il = method.GetILGenerator();
        var listLabel = il.DefineLabel();
        var stringLabel = il.DefineLabel();

        // List
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(List<object>));
        il.Emit(OpCodes.Brtrue, listLabel);

        // String
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(string));
        il.Emit(OpCodes.Brtrue, stringLabel);

        // Default
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(listLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(List<object>));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("get_Item", [typeof(int)])!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(stringLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(string));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(string).GetMethod("get_Chars", [typeof(int)])!);
        il.Emit(OpCodes.Call, typeof(char).GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitGetKeys(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetKeys",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(List<object>),
            [typeof(object)]
        );
        runtime.GetKeys = method;

        var il = method.GetILGenerator();

        // Delegate to RuntimeTypes.GetKeys which handles both _fields dictionary and __ backing fields
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(RuntimeTypes).GetMethod("GetKeys", [typeof(object)])!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitSpreadArray(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SpreadArray",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(List<object>),
            [typeof(object)]
        );
        runtime.SpreadArray = method;

        var il = method.GetILGenerator();
        var listLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(List<object>));
        il.Emit(OpCodes.Brtrue, listLabel);

        // Not a list - return empty
        il.Emit(OpCodes.Newobj, typeof(List<object>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(listLabel);
        // Return new list with same elements
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(List<object>));
        il.Emit(OpCodes.Newobj, typeof(List<object>).GetConstructor([typeof(IEnumerable<object>)])!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitConcatArrays(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ConcatArrays",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(List<object>),
            [typeof(object[])]
        );
        runtime.ConcatArrays = method;

        var il = method.GetILGenerator();
        // var result = new List<object>();
        // foreach (var arr in arrays) if (arr is List<object> list) result.AddRange(list);
        // return result;
        var resultLocal = il.DeclareLocal(typeof(List<object>));
        var indexLocal = il.DeclareLocal(typeof(int));
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var addRangeLabel = il.DefineLabel();

        il.Emit(OpCodes.Newobj, typeof(List<object>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, loopEnd);

        // Get element
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Isinst, typeof(List<object>));
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, addRangeLabel);
        il.Emit(OpCodes.Pop);
        var skipLabel = il.DefineLabel();
        il.Emit(OpCodes.Br, skipLabel);

        il.MarkLabel(addRangeLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Castclass, typeof(List<object>));
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("AddRange", [typeof(IEnumerable<object>)])!);

        il.MarkLabel(skipLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitExpandCallArgs(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ExpandCallArgs",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object[]),
            [typeof(object[]), typeof(bool[])]
        );
        runtime.ExpandCallArgs = method;

        var il = method.GetILGenerator();
        // Simple implementation: create result list, iterate args, expand spreads
        var resultLocal = il.DeclareLocal(typeof(List<object>));
        var indexLocal = il.DeclareLocal(typeof(int));
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.Emit(OpCodes.Newobj, typeof(List<object>).GetConstructor(Type.EmptyTypes)!);
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

        // Is spread - add range if it's a list
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Isinst, typeof(List<object>));
        il.Emit(OpCodes.Dup);
        var notListLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notListLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Castclass, typeof(List<object>));
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("AddRange", [typeof(IEnumerable<object>)])!);
        il.Emit(OpCodes.Br, continueLabel);

        il.MarkLabel(notListLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, continueLabel);

        // Not spread - add single element
        il.MarkLabel(notSpreadLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("Add", [typeof(object)])!);

        il.MarkLabel(continueLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("ToArray")!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitArrayPop(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayPop",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            [typeof(List<object>)]
        );
        runtime.ArrayPop = method;

        var il = method.GetILGenerator();
        var emptyLabel = il.DefineLabel();

        // if (list.Count == 0) return null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Beq, emptyLabel);

        // var last = list[list.Count - 1]
        var lastLocal = il.DeclareLocal(typeof(object));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, lastLocal);

        // list.RemoveAt(list.Count - 1)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("RemoveAt", [typeof(int)])!);

        // return last
        il.Emit(OpCodes.Ldloc, lastLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(emptyLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitArrayShift(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayShift",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            [typeof(List<object>)]
        );
        runtime.ArrayShift = method;

        var il = method.GetILGenerator();
        var emptyLabel = il.DefineLabel();

        // if (list.Count == 0) return null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Beq, emptyLabel);

        // var first = list[0]
        var firstLocal = il.DeclareLocal(typeof(object));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, firstLocal);

        // list.RemoveAt(0)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("RemoveAt", [typeof(int)])!);

        // return first
        il.Emit(OpCodes.Ldloc, firstLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(emptyLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitArrayUnshift(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayUnshift",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(double),
            [typeof(List<object>), typeof(object)]
        );
        runtime.ArrayUnshift = method;

        var il = method.GetILGenerator();

        // list.Insert(0, element)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("Insert", [typeof(int), typeof(object)])!);

        // return (double)list.Count
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitArraySlice(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArraySlice",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(List<object>),
            [typeof(List<object>), typeof(object[])]
        );
        runtime.ArraySlice = method;

        var il = method.GetILGenerator();

        // For simplicity, call the static helper method in RuntimeTypes
        // This would require the RuntimeTypes class to be available, so instead
        // we'll emit inline IL for a basic implementation

        var startLocal = il.DeclareLocal(typeof(int));
        var endLocal = il.DeclareLocal(typeof(int));
        var countLocal = il.DeclareLocal(typeof(int));

        // count = list.Count
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
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
        il.Emit(OpCodes.Unbox_Any, typeof(double));
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
        il.Emit(OpCodes.Unbox_Any, typeof(double));
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
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Max", [typeof(int), typeof(int)])!);
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
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Max", [typeof(int), typeof(int)])!);
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
        il.Emit(OpCodes.Newobj, typeof(List<object>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(rangeValid);
        // return list.GetRange(start, end - start)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("GetRange", [typeof(int), typeof(int)])!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitArrayMap(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayMap",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(List<object>),
            [typeof(List<object>), typeof(object)]
        );
        runtime.ArrayMap = method;

        var il = method.GetILGenerator();

        // var result = new List<object>()
        var resultLocal = il.DeclareLocal(typeof(List<object>));
        il.Emit(OpCodes.Newobj, typeof(List<object>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);

        // var i = 0
        var indexLocal = il.DeclareLocal(typeof(int));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        // Loop: for (int i = 0; i < list.Count; i++)
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);

        // Call callback with (element, index, list) -> create args array
        // var args = new object[] { list[i], (double)i, list }
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Newarr, typeof(object));

        // args[0] = list[i]
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Stelem_Ref);

        // args[1] = (double)i
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, typeof(double));
        il.Emit(OpCodes.Stelem_Ref);

        // args[2] = list
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stelem_Ref);

        // Stack: args array
        // Load callback and args, call InvokeValue
        var argsLocal = il.DeclareLocal(typeof(object[]));
        il.Emit(OpCodes.Stloc, argsLocal);

        il.Emit(OpCodes.Ldarg_1); // callback
        il.Emit(OpCodes.Ldloc, argsLocal); // args
        il.Emit(OpCodes.Call, runtime.InvokeValue);

        // Store the call result
        var callResultLocal = il.DeclareLocal(typeof(object));
        il.Emit(OpCodes.Stloc, callResultLocal);

        // result.Add(callResult)
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, callResultLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("Add", [typeof(object)])!);

        // i++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitArrayFilter(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayFilter",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(List<object>),
            [typeof(List<object>), typeof(object)]
        );
        runtime.ArrayFilter = method;

        var il = method.GetILGenerator();

        // var result = new List<object>()
        var resultLocal = il.DeclareLocal(typeof(List<object>));
        il.Emit(OpCodes.Newobj, typeof(List<object>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);

        // var i = 0
        var indexLocal = il.DeclareLocal(typeof(int));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var skipAdd = il.DefineLabel();

        // Loop: for (int i = 0; i < list.Count; i++)
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);

        // Create args array: [list[i], (double)i, list]
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Newarr, typeof(object));

        // args[0] = list[i]
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Stelem_Ref);

        // args[1] = (double)i
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, typeof(double));
        il.Emit(OpCodes.Stelem_Ref);

        // args[2] = list
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stelem_Ref);

        var argsLocal = il.DeclareLocal(typeof(object[]));
        il.Emit(OpCodes.Stloc, argsLocal);

        // Call callback
        il.Emit(OpCodes.Ldarg_1); // callback
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, runtime.InvokeValue);

        // Call IsTruthy
        il.Emit(OpCodes.Call, runtime.IsTruthy);

        // if (!truthy) skip add
        il.Emit(OpCodes.Brfalse, skipAdd);

        // result.Add(list[i])
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("Add", [typeof(object)])!);

        il.MarkLabel(skipAdd);

        // i++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitArrayForEach(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayForEach",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            [typeof(List<object>), typeof(object)]
        );
        runtime.ArrayForEach = method;

        var il = method.GetILGenerator();

        // var i = 0
        var indexLocal = il.DeclareLocal(typeof(int));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        // Loop: for (int i = 0; i < list.Count; i++)
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);

        // Create args array: [list[i], (double)i, list]
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Newarr, typeof(object));

        // args[0] = list[i]
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Stelem_Ref);

        // args[1] = (double)i
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, typeof(double));
        il.Emit(OpCodes.Stelem_Ref);

        // args[2] = list
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stelem_Ref);

        var argsLocal = il.DeclareLocal(typeof(object[]));
        il.Emit(OpCodes.Stloc, argsLocal);

        // Call callback (discard result)
        il.Emit(OpCodes.Ldarg_1); // callback
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, runtime.InvokeValue);
        il.Emit(OpCodes.Pop); // Discard result

        // i++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitArrayPush(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayPush",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(double),
            [typeof(List<object>), typeof(object)]
        );
        runtime.ArrayPush = method;

        var il = method.GetILGenerator();

        // list.Add(element)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("Add", [typeof(object)])!);

        // return (double)list.Count
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitArrayFind(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayFind",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            [typeof(List<object>), typeof(object)]
        );
        runtime.ArrayFind = method;

        var il = method.GetILGenerator();

        var indexLocal = il.DeclareLocal(typeof(int));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);

        // Create args array: [list[i], (double)i, list]
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Newarr, typeof(object));

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, typeof(double));
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stelem_Ref);

        var argsLocal = il.DeclareLocal(typeof(object[]));
        il.Emit(OpCodes.Stloc, argsLocal);

        // Call callback
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, runtime.InvokeValue);

        // if (IsTruthy(result)) return list[i]
        il.Emit(OpCodes.Call, runtime.IsTruthy);
        var notFound = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notFound);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notFound);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitArrayFindIndex(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayFindIndex",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(double),
            [typeof(List<object>), typeof(object)]
        );
        runtime.ArrayFindIndex = method;

        var il = method.GetILGenerator();

        var indexLocal = il.DeclareLocal(typeof(int));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);

        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Newarr, typeof(object));

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, typeof(double));
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stelem_Ref);

        var argsLocal = il.DeclareLocal(typeof(object[]));
        il.Emit(OpCodes.Stloc, argsLocal);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, runtime.InvokeValue);
        il.Emit(OpCodes.Call, runtime.IsTruthy);

        var notFound = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notFound);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notFound);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldc_R8, -1.0);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitArraySome(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArraySome",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),  // Return boxed bool to match ILEmitter expectations
            [typeof(List<object>), typeof(object)]
        );
        runtime.ArraySome = method;

        var il = method.GetILGenerator();

        var indexLocal = il.DeclareLocal(typeof(int));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);

        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Newarr, typeof(object));

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, typeof(double));
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stelem_Ref);

        var argsLocal = il.DeclareLocal(typeof(object[]));
        il.Emit(OpCodes.Stloc, argsLocal);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, runtime.InvokeValue);
        il.Emit(OpCodes.Call, runtime.IsTruthy);

        var notFound = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notFound);
        il.Emit(OpCodes.Ldc_I4_1); // return true
        il.Emit(OpCodes.Box, typeof(bool));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notFound);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldc_I4_0); // return false
        il.Emit(OpCodes.Box, typeof(bool));
        il.Emit(OpCodes.Ret);
    }

    private static void EmitArrayEvery(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayEvery",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),  // Return boxed bool to match ILEmitter expectations
            [typeof(List<object>), typeof(object)]
        );
        runtime.ArrayEvery = method;

        var il = method.GetILGenerator();

        var indexLocal = il.DeclareLocal(typeof(int));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);

        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Newarr, typeof(object));

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, typeof(double));
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stelem_Ref);

        var argsLocal = il.DeclareLocal(typeof(object[]));
        il.Emit(OpCodes.Stloc, argsLocal);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, runtime.InvokeValue);
        il.Emit(OpCodes.Call, runtime.IsTruthy);

        var continueLoop = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, continueLoop);
        il.Emit(OpCodes.Ldc_I4_0); // return false
        il.Emit(OpCodes.Box, typeof(bool));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(continueLoop);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldc_I4_1); // return true
        il.Emit(OpCodes.Box, typeof(bool));
        il.Emit(OpCodes.Ret);
    }

    private static void EmitArrayReduce(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayReduce",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            [typeof(List<object>), typeof(object[])]
        );
        runtime.ArrayReduce = method;

        var il = method.GetILGenerator();

        // args[0] = callback, args[1] = initial value (optional)
        var accLocal = il.DeclareLocal(typeof(object));
        var indexLocal = il.DeclareLocal(typeof(int));
        var callbackLocal = il.DeclareLocal(typeof(object));

        // callback = args[0]
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, callbackLocal);

        // Check if initial value provided (args.Length > 1)
        var hasInitial = il.DefineLabel();
        var noInitial = il.DefineLabel();
        var startLoop = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Bgt, hasInitial);

        // No initial value: acc = list[0], start from index 1
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        var notEmpty = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, notEmpty);
        // Empty array with no initial - throw or return null
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notEmpty);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, accLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, startLoop);

        il.MarkLabel(hasInitial);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, accLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.MarkLabel(startLoop);
        var loopEnd = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);

        // Create args: [acc, list[i], i, list]
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Newarr, typeof(object));

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, accLocal);
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, typeof(double));
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stelem_Ref);

        var argsLocal = il.DeclareLocal(typeof(object[]));
        il.Emit(OpCodes.Stloc, argsLocal);

        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, runtime.InvokeValue);
        il.Emit(OpCodes.Stloc, accLocal);

        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, startLoop);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, accLocal);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitArrayIncludes(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayIncludes",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),  // Return boxed bool to match ILEmitter expectations
            [typeof(List<object>), typeof(object)]
        );
        runtime.ArrayIncludes = method;

        var il = method.GetILGenerator();

        var indexLocal = il.DeclareLocal(typeof(int));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);

        // if (Equals(list[i], searchElement)) return true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.Equals);

        var notMatch = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notMatch);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, typeof(bool));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notMatch);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, typeof(bool));
        il.Emit(OpCodes.Ret);
    }

    private static void EmitArrayIndexOf(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayIndexOf",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(double),
            [typeof(List<object>), typeof(object)]
        );
        runtime.ArrayIndexOf = method;

        var il = method.GetILGenerator();

        var indexLocal = il.DeclareLocal(typeof(int));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.Equals);

        var notMatch = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notMatch);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notMatch);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldc_R8, -1.0);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitArrayJoin(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayJoin",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(string),
            [typeof(List<object>), typeof(object)]
        );
        runtime.ArrayJoin = method;

        var il = method.GetILGenerator();

        // separator = arg1 ?? ","
        var sepLocal = il.DeclareLocal(typeof(string));
        il.Emit(OpCodes.Ldarg_1);
        var hasSep = il.DefineLabel();
        var afterSep = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, hasSep);
        il.Emit(OpCodes.Ldstr, ",");
        il.Emit(OpCodes.Br, afterSep);
        il.MarkLabel(hasSep);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.MarkLabel(afterSep);
        il.Emit(OpCodes.Stloc, sepLocal);

        // StringBuilder sb = new()
        var sbLocal = il.DeclareLocal(typeof(StringBuilder));
        il.Emit(OpCodes.Newobj, typeof(StringBuilder).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, sbLocal);

        var indexLocal = il.DeclareLocal(typeof(int));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);

        // if (i > 0) sb.Append(separator)
        var skipSep = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, skipSep);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, sepLocal);
        il.Emit(OpCodes.Callvirt, typeof(StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(skipSep);
        // sb.Append(Stringify(list[i]))
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Callvirt, typeof(StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("ToString")!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitArrayConcat(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayConcat",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(List<object>),
            [typeof(List<object>), typeof(object)]
        );
        runtime.ArrayConcat = method;

        var il = method.GetILGenerator();

        // result = new List<object>(list)
        var resultLocal = il.DeclareLocal(typeof(List<object>));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, typeof(List<object>).GetConstructor([typeof(IEnumerable<object>)])!);
        il.Emit(OpCodes.Stloc, resultLocal);

        // if (arg1 is List<object> otherList) result.AddRange(otherList)
        // else result.Add(arg1)
        var notList = il.DefineLabel();
        var done = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, typeof(List<object>));
        il.Emit(OpCodes.Brfalse, notList);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, typeof(List<object>));
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("AddRange", [typeof(IEnumerable<object>)])!);
        il.Emit(OpCodes.Br, done);

        il.MarkLabel(notList);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("Add", [typeof(object)])!);

        il.MarkLabel(done);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitArrayReverse(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayReverse",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(List<object>),
            [typeof(List<object>)]
        );
        runtime.ArrayReverse = method;

        var il = method.GetILGenerator();

        // list.Reverse()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod("Reverse", Type.EmptyTypes)!);

        // return list
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }
}

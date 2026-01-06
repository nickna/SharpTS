using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace SharpTS.Compilation;

public static partial class RuntimeEmitter
{
    private static void EmitStringify(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Stringify",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(string),
            [typeof(object)]
        );
        runtime.Stringify = method;

        var il = method.GetILGenerator();
        var nullLabel = il.DefineLabel();
        var boolLabel = il.DefineLabel();
        var doubleLabel = il.DefineLabel();
        var listLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // if (value == null) return "null"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // if (value is bool b) return b ? "true" : "false"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(bool));
        il.Emit(OpCodes.Brtrue, boolLabel);

        // if (value is double d) return d.ToString()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(double));
        il.Emit(OpCodes.Brtrue, doubleLabel);

        // if (value is List<object?>) return array string
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(List<object>));
        il.Emit(OpCodes.Brtrue, listLabel);

        // if (value is BigInteger) return value.ToString() + "n"
        var bigintLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(System.Numerics.BigInteger));
        il.Emit(OpCodes.Brtrue, bigintLabel);

        // Default: return value.ToString() ?? "null"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("ToString")!);
        il.Emit(OpCodes.Dup);
        var notNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, notNullLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "null");
        il.MarkLabel(notNullLabel);
        il.Emit(OpCodes.Br, endLabel);

        // null case
        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldstr, "null");
        il.Emit(OpCodes.Br, endLabel);

        // bool case
        il.MarkLabel(boolLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, typeof(bool));
        var trueLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, trueLabel);
        il.Emit(OpCodes.Ldstr, "false");
        il.Emit(OpCodes.Br, endLabel);
        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldstr, "true");
        il.Emit(OpCodes.Br, endLabel);

        // double case - simple ToString
        il.MarkLabel(doubleLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, typeof(double));
        var doubleLocal = il.DeclareLocal(typeof(double));
        il.Emit(OpCodes.Stloc, doubleLocal);
        il.Emit(OpCodes.Ldloca, doubleLocal);
        il.Emit(OpCodes.Call, typeof(double).GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Br, endLabel);

        // BigInteger case - format as value.ToString() + "n"
        il.MarkLabel(bigintLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, typeof(System.Numerics.BigInteger));
        var bigintLocal = il.DeclareLocal(typeof(System.Numerics.BigInteger));
        il.Emit(OpCodes.Stloc, bigintLocal);
        il.Emit(OpCodes.Ldloca, bigintLocal);
        il.Emit(OpCodes.Call, typeof(System.Numerics.BigInteger).GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ldstr, "n");
        il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Br, endLabel);

        // List case - format as "[elem1, elem2, ...]"
        il.MarkLabel(listLabel);
        // Use StringBuilder to build the result
        var sbLocal = il.DeclareLocal(typeof(StringBuilder));
        il.Emit(OpCodes.Newobj, typeof(StringBuilder).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, sbLocal);

        // Append "["
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "[");
        il.Emit(OpCodes.Callvirt, typeof(StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);

        // Loop through list elements
        var listLocal = il.DeclareLocal(typeof(List<object>));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(List<object>));
        il.Emit(OpCodes.Stloc, listLocal);

        var indexLocal = il.DeclareLocal(typeof(int));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        // if (index >= list.Count) break
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);

        // if (index > 0) append ", "
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        var skipComma = il.DefineLabel();
        il.Emit(OpCodes.Ble, skipComma);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, ", ");
        il.Emit(OpCodes.Callvirt, typeof(StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(skipComma);

        // Append Stringify(list[index])
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetProperty("Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, method); // Recursive call to Stringify
        il.Emit(OpCodes.Callvirt, typeof(StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);

        // index++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // Append "]" and return
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "]");
        il.Emit(OpCodes.Callvirt, typeof(StringBuilder).GetMethod("Append", [typeof(string)])!);
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, typeof(StringBuilder).GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitConsoleLog(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ConsoleLog",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            [typeof(object)]
        );
        runtime.ConsoleLog = method;

        var il = method.GetILGenerator();
        // Call Stringify then Console.WriteLine
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Call, typeof(Console).GetMethod("WriteLine", [typeof(string)])!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitConsoleLogMultiple(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ConsoleLogMultiple",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            [typeof(object[])]
        );
        runtime.ConsoleLogMultiple = method;

        var il = method.GetILGenerator();
        // Simple implementation: join with spaces and print
        // string.Join(" ", values.Select(Stringify))
        il.Emit(OpCodes.Ldstr, " ");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(string).GetMethod("Join", [typeof(string), typeof(object[])])!);
        il.Emit(OpCodes.Call, typeof(Console).GetMethod("WriteLine", [typeof(string)])!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitToNumber(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ToNumber",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(double),
            [typeof(object)]
        );
        runtime.ToNumber = method;

        var il = method.GetILGenerator();
        // Use Convert.ToDouble with try-catch fallback to NaN
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
        var endLabel = il.DefineLabel();
        il.Emit(OpCodes.Br, endLabel);
        il.BeginCatchBlock(typeof(Exception));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.EndExceptionBlock();
        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitIsTruthy(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "IsTruthy",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(bool),
            [typeof(object)]
        );
        runtime.IsTruthy = method;

        var il = method.GetILGenerator();
        var falseLabel = il.DefineLabel();
        var checkBool = il.DefineLabel();
        var trueLabel = il.DefineLabel();

        // null => false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, falseLabel);

        // bool => return value
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(bool));
        il.Emit(OpCodes.Brtrue, checkBool);

        // everything else => true
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(checkBool);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, typeof(bool));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitTypeOf(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "TypeOf",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(string),
            [typeof(object)]
        );
        runtime.TypeOf = method;

        var il = method.GetILGenerator();
        var nullLabel = il.DefineLabel();
        var boolLabel = il.DefineLabel();
        var numberLabel = il.DefineLabel();
        var stringLabel = il.DefineLabel();
        var symbolLabel = il.DefineLabel();
        var functionLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // null => "object" (JS typeof null === "object")
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // bool => "boolean"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(bool));
        il.Emit(OpCodes.Brtrue, boolLabel);

        // double => "number"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(double));
        il.Emit(OpCodes.Brtrue, numberLabel);

        // string => "string"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(string));
        il.Emit(OpCodes.Brtrue, stringLabel);

        // TSSymbol => "symbol"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSSymbolType);
        il.Emit(OpCodes.Brtrue, symbolLabel);

        // TSFunction => "function"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brtrue, functionLabel);

        // Delegate => "function"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(Delegate));
        il.Emit(OpCodes.Brtrue, functionLabel);

        // BigInteger => "bigint"
        var bigintLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(System.Numerics.BigInteger));
        il.Emit(OpCodes.Brtrue, bigintLabel);

        // Default => "object"
        il.Emit(OpCodes.Ldstr, "object");
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldstr, "object");
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(boolLabel);
        il.Emit(OpCodes.Ldstr, "boolean");
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(numberLabel);
        il.Emit(OpCodes.Ldstr, "number");
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(stringLabel);
        il.Emit(OpCodes.Ldstr, "string");
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(symbolLabel);
        il.Emit(OpCodes.Ldstr, "symbol");
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(functionLabel);
        il.Emit(OpCodes.Ldstr, "function");
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(bigintLabel);
        il.Emit(OpCodes.Ldstr, "bigint");
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitInstanceOf(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "InstanceOf",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(bool),
            [typeof(object), typeof(object)]
        );
        runtime.InstanceOf = method;

        var il = method.GetILGenerator();
        var falseLabel = il.DefineLabel();

        // if (instance == null || classType == null) return false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, falseLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, falseLabel);

        // Get type of instance and check IsAssignableFrom
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, typeof(Type));
        var notTypeLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notTypeLabel);

        // classType is Type, use it directly
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, typeof(Type));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("GetType")!);
        il.Emit(OpCodes.Callvirt, typeof(Type).GetMethod("IsAssignableFrom", [typeof(Type)])!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notTypeLabel);
        // classType is not Type, get its type
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("GetType")!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("GetType")!);
        il.Emit(OpCodes.Callvirt, typeof(Type).GetMethod("IsAssignableFrom", [typeof(Type)])!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitAdd(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Add",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            [typeof(object), typeof(object)]
        );
        runtime.Add = method;

        var il = method.GetILGenerator();
        var stringConcatLabel = il.DefineLabel();

        // if (left is string || right is string) string concat
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(string));
        il.Emit(OpCodes.Brtrue, stringConcatLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, typeof(string));
        il.Emit(OpCodes.Brtrue, stringConcatLabel);

        // Numeric addition
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Box, typeof(double));
        il.Emit(OpCodes.Ret);

        // String concat
        il.MarkLabel(stringConcatLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(object), typeof(object)])!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitEquals(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Equals",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(bool),
            [typeof(object), typeof(object)]
        );
        runtime.Equals = method;

        var il = method.GetILGenerator();
        // Use object.Equals(left, right)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(object).GetMethod("Equals", [typeof(object), typeof(object)])!);
        il.Emit(OpCodes.Ret);
    }
}

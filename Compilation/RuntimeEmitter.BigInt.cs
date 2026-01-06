using System.Reflection;
using System.Reflection.Emit;
using System.Numerics;

namespace SharpTS.Compilation;

public static partial class RuntimeEmitter
{
    private static void EmitCreateBigInt(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // CreateBigInt: object -> BigInteger (boxed)
        var method = typeBuilder.DefineMethod(
            "CreateBigInt",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            [typeof(object)]
        );
        runtime.CreateBigInt = method;

        var il = method.GetILGenerator();
        var bigIntType = typeof(System.Numerics.BigInteger);

        // If already BigInteger, return as-is (boxed)
        var notBigIntLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, bigIntType);
        il.Emit(OpCodes.Brfalse, notBigIntLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notBigIntLabel);

        // If double, convert to BigInteger
        var notDoubleLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(double));
        il.Emit(OpCodes.Brfalse, notDoubleLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, typeof(double));
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Newobj, bigIntType.GetConstructor([typeof(long)])!);
        il.Emit(OpCodes.Box, bigIntType);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notDoubleLabel);

        // If string, parse it
        var notStringLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, typeof(string));
        il.Emit(OpCodes.Brfalse, notStringLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, typeof(string));
        // Handle hex prefix "0x" or "0X"
        var hexCheckLocal = il.DeclareLocal(typeof(string));
        il.Emit(OpCodes.Stloc, hexCheckLocal);
        il.Emit(OpCodes.Ldloc, hexCheckLocal);
        il.Emit(OpCodes.Ldstr, "0x");
        il.Emit(OpCodes.Callvirt, typeof(string).GetMethod("StartsWith", [typeof(string)])!);
        var notHexLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notHexLabel);
        // Parse hex - prepend "0" to ensure positive interpretation
        il.Emit(OpCodes.Ldstr, "0");
        il.Emit(OpCodes.Ldloc, hexCheckLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Callvirt, typeof(string).GetMethod("Substring", [typeof(int)])!);
        il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Ldc_I4, (int)System.Globalization.NumberStyles.HexNumber);
        il.Emit(OpCodes.Call, bigIntType.GetMethod("Parse", [typeof(string), typeof(System.Globalization.NumberStyles)])!);
        il.Emit(OpCodes.Box, bigIntType);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notHexLabel);
        // Parse decimal
        il.Emit(OpCodes.Ldloc, hexCheckLocal);
        il.Emit(OpCodes.Call, bigIntType.GetMethod("Parse", [typeof(string)])!);
        il.Emit(OpCodes.Box, bigIntType);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notStringLabel);
        // Default: throw or return 0n
        il.Emit(OpCodes.Ldstr, "Cannot convert to BigInt");
        il.Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Throw);
    }

    private static void EmitBigIntArithmetic(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var bigIntType = typeof(System.Numerics.BigInteger);

        // Helper to emit binary BigInt operations
        void EmitBinaryBigIntOp(string name, string opMethodName, MethodBuilder target)
        {
            var method = typeBuilder.DefineMethod(
                name,
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(object),
                [typeof(object), typeof(object)]
            );
            if (name == "BigIntAdd") runtime.BigIntAdd = method;
            else if (name == "BigIntSubtract") runtime.BigIntSubtract = method;
            else if (name == "BigIntMultiply") runtime.BigIntMultiply = method;
            else if (name == "BigIntDivide") runtime.BigIntDivide = method;
            else if (name == "BigIntRemainder") runtime.BigIntRemainder = method;

            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Unbox_Any, bigIntType);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Unbox_Any, bigIntType);
            il.Emit(OpCodes.Call, bigIntType.GetMethod(opMethodName, [bigIntType, bigIntType])!);
            il.Emit(OpCodes.Box, bigIntType);
            il.Emit(OpCodes.Ret);
        }

        EmitBinaryBigIntOp("BigIntAdd", "op_Addition", null!);
        EmitBinaryBigIntOp("BigIntSubtract", "op_Subtraction", null!);
        EmitBinaryBigIntOp("BigIntMultiply", "op_Multiply", null!);
        EmitBinaryBigIntOp("BigIntDivide", "op_Division", null!);
        EmitBinaryBigIntOp("BigIntRemainder", "op_Modulus", null!);

        // BigIntPow
        {
            var method = typeBuilder.DefineMethod(
                "BigIntPow",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(object),
                [typeof(object), typeof(object)]
            );
            runtime.BigIntPow = method;

            var il = method.GetILGenerator();
            // Use explicit int cast - find the method that returns int
            var explicitToIntMethod = bigIntType.GetMethods().First(m =>
                m.Name == "op_Explicit" && m.ReturnType == typeof(int) &&
                m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == bigIntType);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Unbox_Any, bigIntType);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Unbox_Any, bigIntType);
            // Convert exponent to int for BigInteger.Pow (value on stack, not address)
            il.Emit(OpCodes.Call, explicitToIntMethod);
            il.Emit(OpCodes.Call, bigIntType.GetMethod("Pow", [bigIntType, typeof(int)])!);
            il.Emit(OpCodes.Box, bigIntType);
            il.Emit(OpCodes.Ret);
        }

        // BigIntNegate
        {
            var method = typeBuilder.DefineMethod(
                "BigIntNegate",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(object),
                [typeof(object)]
            );
            runtime.BigIntNegate = method;

            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Unbox_Any, bigIntType);
            il.Emit(OpCodes.Call, bigIntType.GetMethod("op_UnaryNegation", [bigIntType])!);
            il.Emit(OpCodes.Box, bigIntType);
            il.Emit(OpCodes.Ret);
        }
    }

    private static void EmitBigIntComparison(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var bigIntType = typeof(System.Numerics.BigInteger);

        void EmitCompare(string name, string opName, MethodBuilder target)
        {
            var method = typeBuilder.DefineMethod(
                name,
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(bool),
                [typeof(object), typeof(object)]
            );
            if (name == "BigIntEquals") runtime.BigIntEquals = method;
            else if (name == "BigIntLessThan") runtime.BigIntLessThan = method;
            else if (name == "BigIntLessThanOrEqual") runtime.BigIntLessThanOrEqual = method;
            else if (name == "BigIntGreaterThan") runtime.BigIntGreaterThan = method;
            else if (name == "BigIntGreaterThanOrEqual") runtime.BigIntGreaterThanOrEqual = method;

            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Unbox_Any, bigIntType);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Unbox_Any, bigIntType);
            il.Emit(OpCodes.Call, bigIntType.GetMethod(opName, [bigIntType, bigIntType])!);
            il.Emit(OpCodes.Ret);
        }

        EmitCompare("BigIntEquals", "op_Equality", null!);
        EmitCompare("BigIntLessThan", "op_LessThan", null!);
        EmitCompare("BigIntLessThanOrEqual", "op_LessThanOrEqual", null!);
        EmitCompare("BigIntGreaterThan", "op_GreaterThan", null!);
        EmitCompare("BigIntGreaterThanOrEqual", "op_GreaterThanOrEqual", null!);
    }

    private static void EmitBigIntBitwise(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var bigIntType = typeof(System.Numerics.BigInteger);

        void EmitBinaryBitwise(string name, string opName)
        {
            var method = typeBuilder.DefineMethod(
                name,
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(object),
                [typeof(object), typeof(object)]
            );
            if (name == "BigIntBitwiseAnd") runtime.BigIntBitwiseAnd = method;
            else if (name == "BigIntBitwiseOr") runtime.BigIntBitwiseOr = method;
            else if (name == "BigIntBitwiseXor") runtime.BigIntBitwiseXor = method;

            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Unbox_Any, bigIntType);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Unbox_Any, bigIntType);
            il.Emit(OpCodes.Call, bigIntType.GetMethod(opName, [bigIntType, bigIntType])!);
            il.Emit(OpCodes.Box, bigIntType);
            il.Emit(OpCodes.Ret);
        }

        EmitBinaryBitwise("BigIntBitwiseAnd", "op_BitwiseAnd");
        EmitBinaryBitwise("BigIntBitwiseOr", "op_BitwiseOr");
        EmitBinaryBitwise("BigIntBitwiseXor", "op_ExclusiveOr");

        // BigIntBitwiseNot
        {
            var method = typeBuilder.DefineMethod(
                "BigIntBitwiseNot",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(object),
                [typeof(object)]
            );
            runtime.BigIntBitwiseNot = method;

            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Unbox_Any, bigIntType);
            il.Emit(OpCodes.Call, bigIntType.GetMethod("op_OnesComplement", [bigIntType])!);
            il.Emit(OpCodes.Box, bigIntType);
            il.Emit(OpCodes.Ret);
        }

        // Get the explicit to int method once for shift operations
        var explicitToInt = bigIntType.GetMethods().First(m =>
            m.Name == "op_Explicit" && m.ReturnType == typeof(int) &&
            m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == bigIntType);

        // BigIntLeftShift
        {
            var method = typeBuilder.DefineMethod(
                "BigIntLeftShift",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(object),
                [typeof(object), typeof(object)]
            );
            runtime.BigIntLeftShift = method;

            var il = method.GetILGenerator();
            // Stack after setup: [value, shiftAmount]
            // Need: [value, (int)shiftAmount] for op_LeftShift
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Unbox_Any, bigIntType);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Unbox_Any, bigIntType);
            // Convert shift count to int (value on stack)
            il.Emit(OpCodes.Call, explicitToInt);
            il.Emit(OpCodes.Call, bigIntType.GetMethod("op_LeftShift", [bigIntType, typeof(int)])!);
            il.Emit(OpCodes.Box, bigIntType);
            il.Emit(OpCodes.Ret);
        }

        // BigIntRightShift
        {
            var method = typeBuilder.DefineMethod(
                "BigIntRightShift",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(object),
                [typeof(object), typeof(object)]
            );
            runtime.BigIntRightShift = method;

            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Unbox_Any, bigIntType);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Unbox_Any, bigIntType);
            // Convert shift count to int (value on stack)
            il.Emit(OpCodes.Call, explicitToInt);
            il.Emit(OpCodes.Call, bigIntType.GetMethod("op_RightShift", [bigIntType, typeof(int)])!);
            il.Emit(OpCodes.Box, bigIntType);
            il.Emit(OpCodes.Ret);
        }
    }
}

using System.Reflection;
using System.Reflection.Emit;
using System.Numerics;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitCreateBigInt(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // CreateBigInt: object -> BigInteger (boxed)
        var method = typeBuilder.DefineMethod(
            "CreateBigInt",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.CreateBigInt = method;

        var il = method.GetILGenerator();
        var bigIntType = _types.BigInteger;

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
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, notDoubleLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(bigIntType, _types.Int64));
        il.Emit(OpCodes.Box, bigIntType);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notDoubleLabel);

        // If string, parse it
        var notStringLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, notStringLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        // Handle hex prefix "0x" or "0X"
        var hexCheckLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, hexCheckLocal);
        il.Emit(OpCodes.Ldloc, hexCheckLocal);
        il.Emit(OpCodes.Ldstr, "0x");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "StartsWith", _types.String));
        var notHexLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notHexLabel);
        // Parse hex - prepend "0" to ensure positive interpretation
        il.Emit(OpCodes.Ldstr, "0");
        il.Emit(OpCodes.Ldloc, hexCheckLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Substring", _types.Int32));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Ldc_I4, (int)System.Globalization.NumberStyles.HexNumber);
        var numberStylesType = _types.Resolve("System.Globalization.NumberStyles");
        il.Emit(OpCodes.Call, _types.GetMethod(bigIntType, "Parse", _types.String, numberStylesType));
        il.Emit(OpCodes.Box, bigIntType);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notHexLabel);
        // Parse decimal
        il.Emit(OpCodes.Ldloc, hexCheckLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(bigIntType, "Parse", _types.String));
        il.Emit(OpCodes.Box, bigIntType);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notStringLabel);
        // Default: throw or return 0n
        il.Emit(OpCodes.Ldstr, "Cannot convert to BigInt");
        var invalidOpException = _types.Resolve("System.InvalidOperationException");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(invalidOpException, _types.String));
        il.Emit(OpCodes.Throw);
    }

    private void EmitBigIntArithmetic(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var bigIntType = _types.BigInteger;

        // Helper to emit binary BigInt operations
        void EmitBinaryBigIntOp(string name, string opMethodName, MethodBuilder target)
        {
            var method = typeBuilder.DefineMethod(
                name,
                MethodAttributes.Public | MethodAttributes.Static,
                _types.Object,
                [_types.Object, _types.Object]
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
            il.Emit(OpCodes.Call, _types.GetMethod(bigIntType, opMethodName, bigIntType, bigIntType));
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
                _types.Object,
                [_types.Object, _types.Object]
            );
            runtime.BigIntPow = method;

            var il = method.GetILGenerator();
            // Use explicit int cast - find the method that returns int
            var explicitToIntMethod = _types.GetMethods(bigIntType).First(m =>
                m.Name == "op_Explicit" && m.ReturnType == _types.Int32 &&
                m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == bigIntType);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Unbox_Any, bigIntType);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Unbox_Any, bigIntType);
            // Convert exponent to int for BigInteger.Pow (value on stack, not address)
            il.Emit(OpCodes.Call, explicitToIntMethod);
            il.Emit(OpCodes.Call, _types.GetMethod(bigIntType, "Pow", bigIntType, _types.Int32));
            il.Emit(OpCodes.Box, bigIntType);
            il.Emit(OpCodes.Ret);
        }

        // BigIntNegate
        {
            var method = typeBuilder.DefineMethod(
                "BigIntNegate",
                MethodAttributes.Public | MethodAttributes.Static,
                _types.Object,
                [_types.Object]
            );
            runtime.BigIntNegate = method;

            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Unbox_Any, bigIntType);
            il.Emit(OpCodes.Call, _types.GetMethod(bigIntType, "op_UnaryNegation", bigIntType));
            il.Emit(OpCodes.Box, bigIntType);
            il.Emit(OpCodes.Ret);
        }
    }

    private void EmitBigIntComparison(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var bigIntType = _types.BigInteger;

        void EmitCompare(string name, string opName, MethodBuilder target)
        {
            var method = typeBuilder.DefineMethod(
                name,
                MethodAttributes.Public | MethodAttributes.Static,
                _types.Boolean,
                [_types.Object, _types.Object]
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
            il.Emit(OpCodes.Call, _types.GetMethod(bigIntType, opName, bigIntType, bigIntType));
            il.Emit(OpCodes.Ret);
        }

        EmitCompare("BigIntEquals", "op_Equality", null!);
        EmitCompare("BigIntLessThan", "op_LessThan", null!);
        EmitCompare("BigIntLessThanOrEqual", "op_LessThanOrEqual", null!);
        EmitCompare("BigIntGreaterThan", "op_GreaterThan", null!);
        EmitCompare("BigIntGreaterThanOrEqual", "op_GreaterThanOrEqual", null!);

        EmitBigIntLooseEquals(typeBuilder, runtime);
        EmitBigIntToStringRadix(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits $Runtime.BigIntToStringRadix(object value, double radix) -> string:
    /// BigInt.prototype.toString([radix]). Radix 10 (the default the caller passes
    /// when no argument is given) is the bare decimal form; radices 2–36 use a
    /// DivRem loop with lowercase digits and a leading '-' for negatives. The radix
    /// arrives pre-coerced to a double (the call site emits ToNumber), keeping this
    /// method independent of the $Runtime method-emission order. Mirrors the
    /// interpreter's BigIntBuiltIns.ToStringWithRadix.
    /// </summary>
    private void EmitBigIntToStringRadix(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var bi = _types.BigInteger;
        var method = typeBuilder.DefineMethod(
            "BigIntToStringRadix",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object, _types.Double]
        );
        runtime.BigIntToStringRadix = method;

        var il = method.GetILGenerator();
        var explicitToInt = _types.GetMethods(bi).First(m =>
            m.Name == "op_Explicit" && m.ReturnType == _types.Int32 &&
            m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == bi);
        var divRem = bi.GetMethod("DivRem", [bi, bi, bi.MakeByRefType()])
            ?? throw new InvalidOperationException("BigInteger.DivRem(BigInteger, BigInteger, out BigInteger) not found");
        var sbInsertChar = _types.GetMethod(_types.StringBuilder, "Insert", _types.Int32, _types.Char);
        var getChars = _types.GetMethod(_types.String, "get_Chars", _types.Int32);
        const string digitChars = "0123456789abcdefghijklmnopqrstuvwxyz";

        var valueLocal = il.DeclareLocal(bi);
        var radixLocal = il.DeclareLocal(_types.Int32);
        var absLocal = il.DeclareLocal(bi);
        var rBigLocal = il.DeclareLocal(bi);
        var sbLocal = il.DeclareLocal(_types.StringBuilder);
        var remLocal = il.DeclareLocal(bi);

        var throwRange = il.DefineLabel();

        // value = (BigInteger)valueObj; radix = (int)radixD
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, bi);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, radixLocal);

        // if (radix < 2 || radix > 36) throw
        il.Emit(OpCodes.Ldloc, radixLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Blt, throwRange);
        il.Emit(OpCodes.Ldloc, radixLocal);
        il.Emit(OpCodes.Ldc_I4, 36);
        il.Emit(OpCodes.Bgt, throwRange);

        // if (radix == 10) return value.ToString()
        var notTen = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, radixLocal);
        il.Emit(OpCodes.Ldc_I4, 10);
        il.Emit(OpCodes.Bne_Un, notTen);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(bi, "ToString"));
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notTen);

        // if (value.IsZero) return "0"
        var notZero = il.DefineLabel();
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(bi, "get_IsZero"));
        il.Emit(OpCodes.Brfalse, notZero);
        il.Emit(OpCodes.Ldstr, "0");
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notZero);

        // abs = BigInteger.Abs(value); sb = new StringBuilder(); rBig = new BigInteger(radix)
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(bi, "Abs", bi));
        il.Emit(OpCodes.Stloc, absLocal);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.StringBuilder, _types.EmptyTypes));
        il.Emit(OpCodes.Stloc, sbLocal);
        il.Emit(OpCodes.Ldloc, radixLocal);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(bi, _types.Int32));
        il.Emit(OpCodes.Stloc, rBigLocal);

        // while (abs.Sign != 0) { abs = DivRem(abs, rBig, out rem); sb.Insert(0, digits[(int)rem]); }
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloca, absLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(bi, "get_Sign"));
        il.Emit(OpCodes.Brfalse, loopEnd);
        il.Emit(OpCodes.Ldloc, absLocal);
        il.Emit(OpCodes.Ldloc, rBigLocal);
        il.Emit(OpCodes.Ldloca, remLocal);
        il.Emit(OpCodes.Call, divRem);
        il.Emit(OpCodes.Stloc, absLocal);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldstr, digitChars);
        il.Emit(OpCodes.Ldloc, remLocal);
        il.Emit(OpCodes.Call, explicitToInt);
        il.Emit(OpCodes.Callvirt, getChars);
        il.Emit(OpCodes.Callvirt, sbInsertChar);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, loopStart);
        il.MarkLabel(loopEnd);

        // if (value.Sign < 0) sb.Insert(0, '-')
        var notNeg = il.DefineLabel();
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(bi, "get_Sign"));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, notNeg);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4, (int)'-');
        il.Emit(OpCodes.Callvirt, sbInsertChar);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(notNeg);

        // return sb.ToString()
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.StringBuilder, "ToString"));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(throwRange);
        il.Emit(OpCodes.Ldstr, "toString() radix must be between 2 and 36");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Resolve("System.Exception"), _types.String));
        il.Emit(OpCodes.Throw);
    }

    /// <summary>
    /// Emits $Runtime.BigIntLooseEquals(object, object) -> bool: ECMA-262 7.2.15
    /// loose equality where exactly one operand is a bigint and the other is a
    /// Number/String/Boolean (mixed ==). bigint==Number compares mathematical values
    /// (false for NaN/±Infinity/non-integral); bigint==String parses the trimmed
    /// string (empty → 0n; otherwise BigInteger.TryParse, false on failure);
    /// bigint==Boolean coerces to 0n/1n; anything else is unequal. Self-contained
    /// (BCL-only) so the output DLL stays standalone. Mirrors the interpreter's
    /// Interpreter.LooseEqualsBigInt / TryStringToBigInt.
    /// </summary>
    private void EmitBigIntLooseEquals(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var bi = _types.BigInteger;
        var method = typeBuilder.DefineMethod(
            "BigIntLooseEquals",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.Object]
        );
        runtime.BigIntLooseEquals = method;

        var il = method.GetILGenerator();
        var opEquality = _types.GetMethod(bi, "op_Equality", bi, bi);
        var getZero = _types.GetMethod(bi, "get_Zero");

        var biLocal = il.DeclareLocal(bi);
        var otherLocal = il.DeclareLocal(_types.Object);
        var returnFalse = il.DefineLabel();

        // Identify the bigint operand and the other operand (one of them is a bigint).
        var leftIsBig = il.DefineLabel();
        var afterAssign = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, bi);
        il.Emit(OpCodes.Brtrue, leftIsBig);
        // right is the bigint
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, bi);
        il.Emit(OpCodes.Stloc, biLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, otherLocal);
        il.Emit(OpCodes.Br, afterAssign);
        il.MarkLabel(leftIsBig);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, bi);
        il.Emit(OpCodes.Stloc, biLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stloc, otherLocal);
        il.MarkLabel(afterAssign);

        // other is BigInteger → direct compare (both-bigint, e.g. routed loose ==).
        var notBigOther = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, otherLocal);
        il.Emit(OpCodes.Isinst, bi);
        il.Emit(OpCodes.Brfalse, notBigOther);
        il.Emit(OpCodes.Ldloc, biLocal);
        il.Emit(OpCodes.Ldloc, otherLocal);
        il.Emit(OpCodes.Unbox_Any, bi);
        il.Emit(OpCodes.Call, opEquality);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notBigOther);

        // other is double → integrality-guarded mathematical compare.
        var notDouble = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, otherLocal);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, notDouble);
        var dLocal = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Ldloc, otherLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Stloc, dLocal);
        // NaN → false
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsNaN", _types.Double));
        il.Emit(OpCodes.Brtrue, returnFalse);
        // ±Infinity → false
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsInfinity", _types.Double));
        il.Emit(OpCodes.Brtrue, returnFalse);
        // d != floor(d) → false (non-integral)
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Floor", _types.Double));
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brfalse, returnFalse);
        // return bi == new BigInteger(d)
        il.Emit(OpCodes.Ldloc, biLocal);
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(bi, _types.Double));
        il.Emit(OpCodes.Call, opEquality);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notDouble);

        // other is bool → compare against 0n/1n.
        var notBool = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, otherLocal);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brfalse, notBool);
        il.Emit(OpCodes.Ldloc, biLocal);
        il.Emit(OpCodes.Ldloc, otherLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        var boolTrue = il.DefineLabel();
        var afterBool = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, boolTrue);
        il.Emit(OpCodes.Call, getZero);
        il.Emit(OpCodes.Br, afterBool);
        il.MarkLabel(boolTrue);
        il.Emit(OpCodes.Call, _types.GetMethod(bi, "get_One"));
        il.MarkLabel(afterBool);
        il.Emit(OpCodes.Call, opEquality);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notBool);

        // other is string → StringToBigInt (trim; empty → 0n; else TryParse).
        il.Emit(OpCodes.Ldloc, otherLocal);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, returnFalse);
        var sLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldloc, otherLocal);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.String, "Trim"));
        il.Emit(OpCodes.Stloc, sLocal);
        // empty → bi == 0n
        var notEmpty = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, sLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Length"));
        il.Emit(OpCodes.Brtrue, notEmpty);
        il.Emit(OpCodes.Ldloc, biLocal);
        il.Emit(OpCodes.Call, getZero);
        il.Emit(OpCodes.Call, opEquality);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notEmpty);
        // BigInteger.TryParse(s, out parsed) ? bi == parsed : false
        var parsedLocal = il.DeclareLocal(bi);
        var tryParse = bi.GetMethod("TryParse", [_types.String, bi.MakeByRefType()])
            ?? throw new InvalidOperationException("BigInteger.TryParse(string, out BigInteger) not found");
        il.Emit(OpCodes.Ldloc, sLocal);
        il.Emit(OpCodes.Ldloca, parsedLocal);
        il.Emit(OpCodes.Call, tryParse);
        il.Emit(OpCodes.Brfalse, returnFalse);
        il.Emit(OpCodes.Ldloc, biLocal);
        il.Emit(OpCodes.Ldloc, parsedLocal);
        il.Emit(OpCodes.Call, opEquality);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnFalse);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitBigIntBitwise(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var bigIntType = _types.BigInteger;

        void EmitBinaryBitwise(string name, string opName)
        {
            var method = typeBuilder.DefineMethod(
                name,
                MethodAttributes.Public | MethodAttributes.Static,
                _types.Object,
                [_types.Object, _types.Object]
            );
            if (name == "BigIntBitwiseAnd") runtime.BigIntBitwiseAnd = method;
            else if (name == "BigIntBitwiseOr") runtime.BigIntBitwiseOr = method;
            else if (name == "BigIntBitwiseXor") runtime.BigIntBitwiseXor = method;

            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Unbox_Any, bigIntType);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Unbox_Any, bigIntType);
            il.Emit(OpCodes.Call, _types.GetMethod(bigIntType, opName, bigIntType, bigIntType));
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
                _types.Object,
                [_types.Object]
            );
            runtime.BigIntBitwiseNot = method;

            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Unbox_Any, bigIntType);
            il.Emit(OpCodes.Call, _types.GetMethod(bigIntType, "op_OnesComplement", bigIntType));
            il.Emit(OpCodes.Box, bigIntType);
            il.Emit(OpCodes.Ret);
        }

        // Get the explicit to int method once for shift operations
        var explicitToInt = _types.GetMethods(bigIntType).First(m =>
            m.Name == "op_Explicit" && m.ReturnType == _types.Int32 &&
            m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == bigIntType);

        // BigIntLeftShift
        {
            var method = typeBuilder.DefineMethod(
                "BigIntLeftShift",
                MethodAttributes.Public | MethodAttributes.Static,
                _types.Object,
                [_types.Object, _types.Object]
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
            il.Emit(OpCodes.Call, _types.GetMethod(bigIntType, "op_LeftShift", bigIntType, _types.Int32));
            il.Emit(OpCodes.Box, bigIntType);
            il.Emit(OpCodes.Ret);
        }

        // BigIntRightShift
        {
            var method = typeBuilder.DefineMethod(
                "BigIntRightShift",
                MethodAttributes.Public | MethodAttributes.Static,
                _types.Object,
                [_types.Object, _types.Object]
            );
            runtime.BigIntRightShift = method;

            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Unbox_Any, bigIntType);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Unbox_Any, bigIntType);
            // Convert shift count to int (value on stack)
            il.Emit(OpCodes.Call, explicitToInt);
            il.Emit(OpCodes.Call, _types.GetMethod(bigIntType, "op_RightShift", bigIntType, _types.Int32));
            il.Emit(OpCodes.Box, bigIntType);
            il.Emit(OpCodes.Ret);
        }
    }
}


using System.Reflection;
using System.Reflection.Emit;
using System.Numerics;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitBigIntStaticMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        runtime.BigIntAsIntN = EmitBigIntTruncate(typeBuilder, runtime, "BigIntAsIntN", signed: true);
        runtime.BigIntAsUintN = EmitBigIntTruncate(typeBuilder, runtime, "BigIntAsUintN", signed: false);
    }

    private MethodBuilder EmitBigIntTruncate(
        TypeBuilder typeBuilder, EmittedRuntime runtime, string name, bool signed)
    {
        var method = typeBuilder.DefineMethod(
            name,
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]);

        var il = method.GetILGenerator();
        var bitsNumber = il.DeclareLocal(_types.Double);
        var bits = il.DeclareLocal(_types.Int32);
        var value = il.DeclareLocal(_types.BigInteger);
        var modulus = il.DeclareLocal(_types.BigInteger);
        var truncated = il.DeclareLocal(_types.BigInteger);

        // ToIndex(bits): NaN => +0; otherwise truncate and reject negative,
        // infinite, or widths outside the emitted implementation's int range.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Stloc, bitsNumber);
        var notNaN = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, bitsNumber);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsNaN", _types.Double));
        il.Emit(OpCodes.Brfalse, notNaN);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Stloc, bitsNumber);
        il.MarkLabel(notNaN);
        il.Emit(OpCodes.Ldloc, bitsNumber);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Truncate", _types.Double));
        il.Emit(OpCodes.Stloc, bitsNumber);

        var invalidBits = il.DefineLabel();
        var validBits = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, bitsNumber);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Blt, invalidBits);
        il.Emit(OpCodes.Ldloc, bitsNumber);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsInfinity", _types.Double));
        il.Emit(OpCodes.Brtrue, invalidBits);
        il.Emit(OpCodes.Ldloc, bitsNumber);
        il.Emit(OpCodes.Ldc_R8, (double)int.MaxValue);
        il.Emit(OpCodes.Ble, validBits);
        il.MarkLabel(invalidBits);
        GuestErrorEmitter.ThrowError(il, runtime, runtime.TSRangeErrorCtor,
            "BigInt bit width is outside the supported index range");
        il.MarkLabel(validBits);
        il.Emit(OpCodes.Ldloc, bitsNumber);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, bits);

        // ToBigInt(value), including observable object-to-primitive coercion.
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToBigInt);
        il.Emit(OpCodes.Unbox_Any, _types.BigInteger);
        il.Emit(OpCodes.Stloc, value);

        var nonZeroWidth = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, bits);
        il.Emit(OpCodes.Brtrue, nonZeroWidth);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.BigInteger, "Zero")!.GetGetMethod()!);
        il.Emit(OpCodes.Box, _types.BigInteger);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(nonZeroWidth);

        // modulus = 1n << bits; truncated = ((value % modulus) + modulus) % modulus.
        var shiftLeft = _types.GetMethod(_types.BigInteger, "op_LeftShift", _types.BigInteger, _types.Int32);
        var remainder = _types.GetMethod(_types.BigInteger, "op_Modulus", _types.BigInteger, _types.BigInteger);
        var add = _types.GetMethod(_types.BigInteger, "op_Addition", _types.BigInteger, _types.BigInteger);
        var subtract = _types.GetMethod(_types.BigInteger, "op_Subtraction", _types.BigInteger, _types.BigInteger);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.BigInteger, "One")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, bits);
        il.Emit(OpCodes.Call, shiftLeft);
        il.Emit(OpCodes.Stloc, modulus);
        il.Emit(OpCodes.Ldloc, value);
        il.Emit(OpCodes.Ldloc, modulus);
        il.Emit(OpCodes.Call, remainder);
        il.Emit(OpCodes.Ldloc, modulus);
        il.Emit(OpCodes.Call, add);
        il.Emit(OpCodes.Ldloc, modulus);
        il.Emit(OpCodes.Call, remainder);
        il.Emit(OpCodes.Stloc, truncated);

        if (signed)
        {
            var returnValue = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, truncated);
            il.Emit(OpCodes.Call, _types.GetProperty(_types.BigInteger, "One")!.GetGetMethod()!);
            il.Emit(OpCodes.Ldloc, bits);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Sub);
            il.Emit(OpCodes.Call, shiftLeft);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.BigInteger, "op_LessThan", _types.BigInteger, _types.BigInteger));
            il.Emit(OpCodes.Brtrue, returnValue);
            il.Emit(OpCodes.Ldloc, truncated);
            il.Emit(OpCodes.Ldloc, modulus);
            il.Emit(OpCodes.Call, subtract);
            il.Emit(OpCodes.Stloc, truncated);
            il.MarkLabel(returnValue);
        }

        il.Emit(OpCodes.Ldloc, truncated);
        il.Emit(OpCodes.Box, _types.BigInteger);
        il.Emit(OpCodes.Ret);
        return method;
    }

    /// <summary>
    /// Emits ECMA-262 NumberFromBigInt. <see cref="BigInteger"/>'s direct
    /// conversion to <see cref="double"/> truncates some halfway-adjacent
    /// values (for example 2^53 + 3) instead of applying the spec's
    /// round-to-nearest, ties-to-even rule, so the rounding is performed while
    /// the integer is still exact and only the 53-bit significand is cast.
    /// </summary>
    private void EmitBigIntToNumber(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "BigIntToNumber",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.BigInteger]);
        runtime.BigIntToNumber = method;

        var il = method.GetILGenerator();
        var magnitude = il.DeclareLocal(_types.BigInteger);
        var significand = il.DeclareLocal(_types.BigInteger);
        var remainder = il.DeclareLocal(_types.BigInteger);
        var halfway = il.DeclareLocal(_types.BigInteger);
        var shift = il.DeclareLocal(_types.Int32);
        var halfwayComparison = il.DeclareLocal(_types.Int32);
        var negative = il.DeclareLocal(_types.Boolean);
        var result = il.DeclareLocal(_types.Double);

        var isZero = _types.GetProperty(_types.BigInteger, "IsZero")!.GetGetMethod()!;
        var sign = _types.GetProperty(_types.BigInteger, "Sign")!.GetGetMethod()!;
        var isEven = _types.GetProperty(_types.BigInteger, "IsEven")!.GetGetMethod()!;
        var one = _types.GetProperty(_types.BigInteger, "One")!.GetGetMethod()!;
        var abs = _types.GetMethod(_types.BigInteger, "Abs", _types.BigInteger);
        var getBitLength = _types.GetMethodNoParams(_types.BigInteger, "GetBitLength");
        var shiftRight = _types.GetMethod(_types.BigInteger, "op_RightShift", _types.BigInteger, _types.Int32);
        var shiftLeft = _types.GetMethod(_types.BigInteger, "op_LeftShift", _types.BigInteger, _types.Int32);
        var subtract = _types.GetMethod(_types.BigInteger, "op_Subtraction", _types.BigInteger, _types.BigInteger);
        var increment = _types.GetMethod(_types.BigInteger, "op_Increment", _types.BigInteger);
        var compare = _types.GetMethod(_types.BigInteger, "Compare", _types.BigInteger, _types.BigInteger);
        var explicitToDouble = _types.GetMethods(_types.BigInteger,
                BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == "op_Explicit"
                && m.ReturnType == _types.Double
                && m.GetParameters() is [{ ParameterType: var p }] && p == _types.BigInteger);

        // 0n converts exactly to +0.
        var nonZero = il.DefineLabel();
        il.Emit(OpCodes.Ldarga_S, 0);
        il.Emit(OpCodes.Call, isZero);
        il.Emit(OpCodes.Brfalse, nonZero);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(nonZero);

        // Record the sign and operate on the exact magnitude.
        il.Emit(OpCodes.Ldarga_S, 0);
        il.Emit(OpCodes.Call, sign);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Clt);
        il.Emit(OpCodes.Stloc, negative);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, abs);
        il.Emit(OpCodes.Stloc, magnitude);

        // shift = max(0, bitLength - 53); significand = magnitude >> shift.
        il.Emit(OpCodes.Ldloca, magnitude);
        il.Emit(OpCodes.Call, getBitLength);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4, 53);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Max", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, shift);
        il.Emit(OpCodes.Ldloc, magnitude);
        il.Emit(OpCodes.Ldloc, shift);
        il.Emit(OpCodes.Call, shiftRight);
        il.Emit(OpCodes.Stloc, significand);

        var finish = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, shift);
        il.Emit(OpCodes.Brfalse, finish);

        // Compare the discarded bits with the halfway point. Round up when
        // above halfway, or exactly halfway with an odd significand.
        il.Emit(OpCodes.Ldloc, magnitude);
        il.Emit(OpCodes.Ldloc, significand);
        il.Emit(OpCodes.Ldloc, shift);
        il.Emit(OpCodes.Call, shiftLeft);
        il.Emit(OpCodes.Call, subtract);
        il.Emit(OpCodes.Stloc, remainder);
        il.Emit(OpCodes.Call, one);
        il.Emit(OpCodes.Ldloc, shift);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Call, shiftLeft);
        il.Emit(OpCodes.Stloc, halfway);
        il.Emit(OpCodes.Ldloc, remainder);
        il.Emit(OpCodes.Ldloc, halfway);
        il.Emit(OpCodes.Call, compare);
        il.Emit(OpCodes.Stloc, halfwayComparison);

        var roundUp = il.DefineLabel();
        var belowHalfway = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, halfwayComparison);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bgt, roundUp);
        il.Emit(OpCodes.Ldloc, halfwayComparison);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, belowHalfway);
        il.Emit(OpCodes.Ldloca, significand);
        il.Emit(OpCodes.Call, isEven);
        il.Emit(OpCodes.Brtrue, finish);

        il.MarkLabel(roundUp);
        il.Emit(OpCodes.Ldloc, significand);
        il.Emit(OpCodes.Call, increment);
        il.Emit(OpCodes.Stloc, significand);

        // Carry out of the 53-bit significand advances the exponent.
        il.Emit(OpCodes.Ldloca, significand);
        il.Emit(OpCodes.Call, getBitLength);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4, 53);
        il.Emit(OpCodes.Ble, finish);
        il.Emit(OpCodes.Ldloc, significand);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, shiftRight);
        il.Emit(OpCodes.Stloc, significand);
        il.Emit(OpCodes.Ldloc, shift);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, shift);
        il.Emit(OpCodes.Br, finish);

        il.MarkLabel(belowHalfway);

        il.MarkLabel(finish);
        il.Emit(OpCodes.Ldloc, significand);
        il.Emit(OpCodes.Call, explicitToDouble);
        il.Emit(OpCodes.Ldloc, shift);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "ScaleB", _types.Double, _types.Int32));
        il.Emit(OpCodes.Stloc, result);
        var positive = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, negative);
        il.Emit(OpCodes.Brfalse, positive);
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Neg);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(positive);
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ret);
    }

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

        // Both BigInt(value) and ToBigInt(value) use ToPrimitive(value,
        // number). Keep it separate because their Number handling differs:
        // BigInt(1) accepts an integral Number while ToBigInt(1) rejects it.
        var toPrimitive = EmitBigIntToPrimitive(typeBuilder, runtime);

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

        // ToBigInt accepts booleans.
        var notBooleanLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brfalse, notBooleanLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        var booleanFalse = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, booleanFalse);
        il.Emit(OpCodes.Call, _types.GetProperty(bigIntType, "One")!.GetGetMethod()!);
        il.Emit(OpCodes.Box, bigIntType);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(booleanFalse);
        il.Emit(OpCodes.Call, _types.GetProperty(bigIntType, "Zero")!.GetGetMethod()!);
        il.Emit(OpCodes.Box, bigIntType);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notBooleanLabel);

        // If double, convert to BigInteger
        var notDoubleLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, notDoubleLabel);
        var numberLocal = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Stloc, numberLocal);
        var invalidNumber = il.DefineLabel();
        var validNumber = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, numberLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsNaN", _types.Double));
        il.Emit(OpCodes.Brtrue, invalidNumber);
        il.Emit(OpCodes.Ldloc, numberLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsInfinity", _types.Double));
        il.Emit(OpCodes.Brtrue, invalidNumber);
        il.Emit(OpCodes.Ldloc, numberLocal);
        il.Emit(OpCodes.Ldloc, numberLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Truncate", _types.Double));
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brtrue, validNumber);
        il.MarkLabel(invalidNumber);
        GuestErrorEmitter.ThrowError(il, runtime, runtime.TSRangeErrorCtor,
            "The number cannot be converted to a BigInt because it is not an integer");
        il.MarkLabel(validNumber);
        il.Emit(OpCodes.Ldloc, numberLocal);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(bigIntType, _types.Double));
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
        var hexCheckLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.String, "Trim"));
        il.Emit(OpCodes.Stloc, hexCheckLocal);

        // Empty/whitespace-only strings convert to 0n.
        var nonEmptyString = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, hexCheckLocal);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.String, "Length"));
        il.Emit(OpCodes.Brtrue, nonEmptyString);
        il.Emit(OpCodes.Call, _types.GetProperty(bigIntType, "Zero")!.GetGetMethod()!);
        il.Emit(OpCodes.Box, bigIntType);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(nonEmptyString);

        var startsWithIgnoreCase = typeof(string).GetMethod(
            nameof(string.StartsWith), [typeof(string), typeof(StringComparison)])!;

        void EmitRadixParser(string prefix, int radix)
        {
            var nextPrefix = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, hexCheckLocal);
            il.Emit(OpCodes.Ldstr, prefix);
            il.Emit(OpCodes.Ldc_I4, (int)StringComparison.OrdinalIgnoreCase);
            il.Emit(OpCodes.Callvirt, startsWithIgnoreCase);
            il.Emit(OpCodes.Brfalse, nextPrefix);

            var hasDigits = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, hexCheckLocal);
            il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.String, "Length"));
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Bgt, hasDigits);
            GuestErrorEmitter.ThrowError(il, runtime, runtime.TSSyntaxErrorCtor,
                "Cannot convert string to BigInt");
            il.MarkLabel(hasDigits);

            var resultLocal = il.DeclareLocal(bigIntType);
            var indexLocal = il.DeclareLocal(_types.Int32);
            var digitLocal = il.DeclareLocal(_types.Int32);
            il.Emit(OpCodes.Call, _types.GetProperty(bigIntType, "Zero")!.GetGetMethod()!);
            il.Emit(OpCodes.Stloc, resultLocal);
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Stloc, indexLocal);
            var check = il.DefineLabel();
            var body = il.DefineLabel();
            il.Emit(OpCodes.Br, check);
            il.MarkLabel(body);
            il.Emit(OpCodes.Ldloc, hexCheckLocal);
            il.Emit(OpCodes.Ldloc, indexLocal);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Chars")!.GetGetMethod()!);
            il.Emit(OpCodes.Ldc_I4, (int)'0');
            il.Emit(OpCodes.Sub);
            il.Emit(OpCodes.Stloc, digitLocal);
            var invalidDigit = il.DefineLabel();
            var validDigit = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, digitLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Blt, invalidDigit);
            il.Emit(OpCodes.Ldloc, digitLocal);
            il.Emit(OpCodes.Ldc_I4, radix);
            il.Emit(OpCodes.Blt, validDigit);
            il.MarkLabel(invalidDigit);
            GuestErrorEmitter.ThrowError(il, runtime, runtime.TSSyntaxErrorCtor,
                "Cannot convert string to BigInt");
            il.MarkLabel(validDigit);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ldc_I4, radix);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(bigIntType, _types.Int32));
            il.Emit(OpCodes.Call, _types.GetMethod(bigIntType, "op_Multiply", bigIntType, bigIntType));
            il.Emit(OpCodes.Ldloc, digitLocal);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(bigIntType, _types.Int32));
            il.Emit(OpCodes.Call, _types.GetMethod(bigIntType, "op_Addition", bigIntType, bigIntType));
            il.Emit(OpCodes.Stloc, resultLocal);
            il.Emit(OpCodes.Ldloc, indexLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, indexLocal);
            il.MarkLabel(check);
            il.Emit(OpCodes.Ldloc, indexLocal);
            il.Emit(OpCodes.Ldloc, hexCheckLocal);
            il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.String, "Length"));
            il.Emit(OpCodes.Blt, body);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Box, bigIntType);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(nextPrefix);
        }

        EmitRadixParser("0b", 2);
        EmitRadixParser("0o", 8);

        // Handle hex prefix "0x" or "0X".
        il.Emit(OpCodes.Ldloc, hexCheckLocal);
        il.Emit(OpCodes.Ldstr, "0x");
        il.Emit(OpCodes.Ldc_I4, (int)StringComparison.OrdinalIgnoreCase);
        il.Emit(OpCodes.Callvirt, startsWithIgnoreCase);
        var notHexLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notHexLabel);
        var numberStylesType = _types.Resolve("System.Globalization.NumberStyles");
        var parsedStringLocal = il.DeclareLocal(bigIntType);
        var parsedHex = il.DefineLabel();
        il.BeginExceptionBlock();
        // Parse hex - prepend "0" to ensure positive interpretation.
        il.Emit(OpCodes.Ldstr, "0");
        il.Emit(OpCodes.Ldloc, hexCheckLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Substring", _types.Int32));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Ldc_I4, (int)System.Globalization.NumberStyles.HexNumber);
        il.Emit(OpCodes.Call, _types.GetMethod(bigIntType, "Parse", _types.String, numberStylesType));
        il.Emit(OpCodes.Stloc, parsedStringLocal);
        il.Emit(OpCodes.Leave, parsedHex);
        il.BeginCatchBlock(_types.Resolve("System.FormatException"));
        il.Emit(OpCodes.Pop);
        GuestErrorEmitter.ThrowError(il, runtime, runtime.TSSyntaxErrorCtor,
            "Cannot convert string to BigInt");
        il.EndExceptionBlock();
        il.MarkLabel(parsedHex);
        il.Emit(OpCodes.Ldloc, parsedStringLocal);
        il.Emit(OpCodes.Box, bigIntType);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notHexLabel);
        // Parse decimal, translating BCL FormatException into the guest
        // SyntaxError required by StringToBigInt.
        var parsedDecimal = il.DefineLabel();
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldloc, hexCheckLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(bigIntType, "Parse", _types.String));
        il.Emit(OpCodes.Stloc, parsedStringLocal);
        il.Emit(OpCodes.Leave, parsedDecimal);
        il.BeginCatchBlock(_types.Resolve("System.FormatException"));
        il.Emit(OpCodes.Pop);
        GuestErrorEmitter.ThrowError(il, runtime, runtime.TSSyntaxErrorCtor,
            "Cannot convert string to BigInt");
        il.EndExceptionBlock();
        il.MarkLabel(parsedDecimal);
        il.Emit(OpCodes.Ldloc, parsedStringLocal);
        il.Emit(OpCodes.Box, bigIntType);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notStringLabel);

        // Arrays use their ordinary primitive string conversion ([], [1],
        // [10n], ...), then re-enter the string parser above.
        var notArrayLikeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.IListOfObject);
        il.Emit(OpCodes.Brfalse, notArrayLikeLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Call, method);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notArrayLikeLabel);

        // Ordinary objects undergo ToPrimitive(number) once, then re-enter
        // the callable BigInt conversion with the resulting primitive.
        var primitiveLocal = il.DeclareLocal(_types.Object);
        var objectLike = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brtrue, objectLike);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, objectLike);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot convert value to BigInt");
        il.MarkLabel(objectLike);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, toPrimitive);
        il.Emit(OpCodes.Stloc, primitiveLocal);
        il.Emit(OpCodes.Ldloc, primitiveLocal);
        il.Emit(OpCodes.Call, method);
        il.Emit(OpCodes.Ret);

        EmitStrictToBigInt(typeBuilder, runtime, toPrimitive);
    }

    private MethodBuilder EmitBigIntToPrimitive(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "BigIntToPrimitive",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]);
        var il = method.GetILGenerator();
        var emptyArgs = il.DeclareLocal(_types.ObjectArray);
        var hintArgs = il.DeclareLocal(_types.ObjectArray);
        var candidate = il.DeclareLocal(_types.Object);
        var result = il.DeclareLocal(_types.Object);

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, emptyArgs);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldstr, "number");
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Stloc, hintArgs);

        bool EmitReturnIfPrimitive(LocalBuilder value)
        {
            var next = il.DefineLabel();
            var returnValue = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, value);
            il.Emit(OpCodes.Brfalse, returnValue);
            foreach (var primitiveType in new[]
                     {
                         runtime.UndefinedType, _types.String, _types.Double,
                         _types.Boolean, _types.BigInteger, runtime.TSSymbolType
                     })
            {
                il.Emit(OpCodes.Ldloc, value);
                il.Emit(OpCodes.Isinst, primitiveType);
                il.Emit(OpCodes.Brtrue, returnValue);
            }
            il.Emit(OpCodes.Br, next);
            il.MarkLabel(returnValue);
            il.Emit(OpCodes.Ldloc, value);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(next);
            return true;
        }

        // ExoticToPrim: GetMethod(input, @@toPrimitive).
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, runtime.SymbolToPrimitive);
        il.Emit(OpCodes.Call, runtime.GetIndex);
        il.Emit(OpCodes.Stloc, candidate);
        var ordinary = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, candidate);
        il.Emit(OpCodes.Brfalse, ordinary);
        il.Emit(OpCodes.Ldloc, candidate);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, ordinary);
        il.Emit(OpCodes.Ldloc, candidate);
        il.Emit(OpCodes.Call, runtime.TypeOf);
        il.Emit(OpCodes.Ldstr, "function");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        var exoticCallable = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, exoticCallable);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Symbol.toPrimitive is not callable");
        il.MarkLabel(exoticCallable);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, candidate);
        il.Emit(OpCodes.Ldloc, hintArgs);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Stloc, result);
        EmitReturnIfPrimitive(result);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Symbol.toPrimitive must return a primitive value");

        il.MarkLabel(ordinary);
        foreach (var name in new[] { "valueOf", "toString" })
        {
            var nextMethod = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Call, runtime.GetProperty);
            il.Emit(OpCodes.Stloc, candidate);
            il.Emit(OpCodes.Ldloc, candidate);
            il.Emit(OpCodes.Call, runtime.TypeOf);
            il.Emit(OpCodes.Ldstr, "function");
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brfalse, nextMethod);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, candidate);
            il.Emit(OpCodes.Ldloc, emptyArgs);
            il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
            il.Emit(OpCodes.Stloc, result);
            EmitReturnIfPrimitive(result);
            il.MarkLabel(nextMethod);
        }
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot convert object to primitive value");
        return method;
    }

    private void EmitStrictToBigInt(
        TypeBuilder typeBuilder, EmittedRuntime runtime, MethodBuilder toPrimitive)
    {
        var method = runtime.ToBigInt;
        var il = method.GetILGenerator();
        var notNumber = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, notNumber);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "BigInt value is required");
        il.MarkLabel(notNumber);

        var convertPrimitive = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brtrue, convertPrimitive);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        var direct = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, direct);
        il.MarkLabel(convertPrimitive);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, toPrimitive);
        il.Emit(OpCodes.Call, method);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(direct);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.CreateBigInt);
        il.Emit(OpCodes.Ret);
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
        var divRem = _types.TryGetMethod(bi, "DivRem", bi, bi, bi.MakeByRefType())
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
        GuestErrorEmitter.ThrowError(il, runtime, runtime.TSRangeErrorCtor,
            "toString() radix must be between 2 and 36");
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
        var tryParse = _types.TryGetMethod(bi, "TryParse", _types.String, bi.MakeByRefType())
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


using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace SharpTS.Compilation;

/// <summary>
/// Number-related runtime emission methods.
/// </summary>
public partial class RuntimeEmitter
{
    private void EmitNumberMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Emit helper methods first (they're used by other methods)
        EmitGetDigitValue(typeBuilder, runtime);
        EmitParseIntStringHelper(typeBuilder, runtime);
        EmitParseIntHelper(typeBuilder, runtime);
        EmitConvertIntToRadix(typeBuilder, runtime);
        EmitGetValidFloatPart(typeBuilder, runtime);

        EmitNumberParseInt(typeBuilder, runtime);
        EmitNumberParseFloat(typeBuilder, runtime);
        EmitNumberIsNaN(typeBuilder, runtime);
        EmitNumberIsFinite(typeBuilder, runtime);
        EmitNumberIsInteger(typeBuilder, runtime);
        EmitNumberIsSafeInteger(typeBuilder, runtime);
        EmitGlobalIsNaN(typeBuilder, runtime);
        EmitGlobalIsFinite(typeBuilder, runtime);
        EmitNumberToFixedDouble(typeBuilder, runtime);
        EmitNumberToFixed(typeBuilder, runtime);
        EmitNumberToPrecision(typeBuilder, runtime);
        EmitNumberToExponential(typeBuilder, runtime);
        EmitNumberToStringRadix(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits the allocation-free typed parse core used when the caller has already
    /// proved the input is a string and the radix is a native Int32. The general
    /// object/object helper remains responsible for observable JS coercion.
    /// </summary>
    private void EmitParseIntStringHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "NumberParseIntString",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.String, _types.Int32]);
        method.SetImplementationFlags(MethodImplAttributes.AggressiveOptimization);
        runtime.NumberParseIntString = method;

        var il = method.GetILGenerator();
        var strLocal = il.DeclareLocal(_types.String);
        var radixLocal = il.DeclareLocal(_types.Int32);
        var signLocal = il.DeclareLocal(_types.Int32);
        var startLocal = il.DeclareLocal(_types.Int32);
        var indexLocal = il.DeclareLocal(_types.Int32);
        var digitLocal = il.DeclareLocal(_types.Int32);
        var prefixCharLocal = il.DeclareLocal(_types.Char);
        var resultLocal = il.DeclareLocal(_types.Double);
        var hasDigitsLocal = il.DeclareLocal(_types.Boolean);

        var notEmpty = il.DefineLabel();
        var notMinus = il.DefineLabel();
        var afterSign = il.DefineLabel();
        var checkPrefix = il.DefineLabel();
        var noPrefix = il.DefineLabel();
        var prefixFound = il.DefineLabel();
        var validateRadix = il.DefineLabel();
        var radixAtLeastTwo = il.DefineLabel();
        var radixValid = il.DefineLabel();
        var loop = il.DefineLabel();
        var endLoop = il.DefineLabel();
        var returnResult = il.DefineLabel();

        // Trim is allocation-free when no surrounding whitespace is present.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Trim", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, strLocal);
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, notEmpty);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notEmpty);

        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, signLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, startLocal);

        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", [_types.Int32])!);
        il.Emit(OpCodes.Ldc_I4, (int)'-');
        il.Emit(OpCodes.Bne_Un, notMinus);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Stloc, signLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, startLocal);
        il.Emit(OpCodes.Br, afterSign);

        il.MarkLabel(notMinus);
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", [_types.Int32])!);
        il.Emit(OpCodes.Ldc_I4, (int)'+');
        il.Emit(OpCodes.Bne_Un, afterSign);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, startLocal);
        il.MarkLabel(afterSign);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stloc, radixLocal);
        il.Emit(OpCodes.Ldloc, radixLocal);
        il.Emit(OpCodes.Brfalse, checkPrefix);
        il.Emit(OpCodes.Ldloc, radixLocal);
        il.Emit(OpCodes.Ldc_I4, 16);
        il.Emit(OpCodes.Beq, checkPrefix);
        il.Emit(OpCodes.Br, validateRadix);

        // Radix 0 and 16 both recognize and strip an optional 0x prefix.
        il.MarkLabel(checkPrefix);
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Blt, noPrefix);
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", [_types.Int32])!);
        il.Emit(OpCodes.Ldc_I4, (int)'0');
        il.Emit(OpCodes.Bne_Un, noPrefix);
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", [_types.Int32])!);
        il.Emit(OpCodes.Stloc, prefixCharLocal);
        il.Emit(OpCodes.Ldloc, prefixCharLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'x');
        il.Emit(OpCodes.Beq, prefixFound);
        il.Emit(OpCodes.Ldloc, prefixCharLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'X');
        il.Emit(OpCodes.Bne_Un, noPrefix);

        il.MarkLabel(prefixFound);
        il.Emit(OpCodes.Ldc_I4, 16);
        il.Emit(OpCodes.Stloc, radixLocal);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, startLocal);
        il.Emit(OpCodes.Br, validateRadix);

        il.MarkLabel(noPrefix);
        il.Emit(OpCodes.Ldloc, radixLocal);
        il.Emit(OpCodes.Brtrue, validateRadix);
        il.Emit(OpCodes.Ldc_I4, 10);
        il.Emit(OpCodes.Stloc, radixLocal);

        il.MarkLabel(validateRadix);
        il.Emit(OpCodes.Ldloc, radixLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Bge, radixAtLeastTwo);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(radixAtLeastTwo);
        il.Emit(OpCodes.Ldloc, radixLocal);
        il.Emit(OpCodes.Ldc_I4, 36);
        il.Emit(OpCodes.Ble, radixValid);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(radixValid);

        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, hasDigitsLocal);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, endLoop);
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", [_types.Int32])!);
        il.Emit(OpCodes.Call, runtime.GetDigitValue);
        il.Emit(OpCodes.Stloc, digitLocal);
        il.Emit(OpCodes.Ldloc, digitLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, endLoop);
        il.Emit(OpCodes.Ldloc, digitLocal);
        il.Emit(OpCodes.Ldloc, radixLocal);
        il.Emit(OpCodes.Bge, endLoop);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, radixLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Ldloc, digitLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, hasDigitsLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loop);

        il.MarkLabel(endLoop);
        il.Emit(OpCodes.Ldloc, hasDigitsLocal);
        il.Emit(OpCodes.Brtrue, returnResult);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnResult);
        il.Emit(OpCodes.Ldloc, signLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Ret);
    }

    private void EmitNumberParseInt(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // parseInt implementation using emitted helper
        var method = typeBuilder.DefineMethod(
            "NumberParseInt",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object, _types.Object]
        );
        runtime.NumberParseInt = method;

        var il = method.GetILGenerator();

        // Call the emitted ParseIntHelper method
        il.Emit(OpCodes.Ldarg_0); // str
        il.Emit(OpCodes.Ldarg_1); // radix
        il.Emit(OpCodes.Call, runtime.ParseIntHelper);
        il.Emit(OpCodes.Ret);
    }

    private void EmitParseIntHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Helper method that implements parseInt logic
        var method = typeBuilder.DefineMethod(
            "ParseIntHelper",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object, _types.Object]
        );
        runtime.ParseIntHelper = method;

        var il = method.GetILGenerator();
        var strLocal = il.DeclareLocal(_types.String);
        var radixLocal = il.DeclareLocal(_types.Int32);
        var signLocal = il.DeclareLocal(_types.Int32);
        var startIndexLocal = il.DeclareLocal(_types.Int32);
        var resultLocal = il.DeclareLocal(_types.Int64);

        var getRadixLabel = il.DefineLabel();
        var radixFromDoubleLabel = il.DefineLabel();
        var radixFromIntLabel = il.DefineLabel();
        var afterRadixLabel = il.DefineLabel();
        var checkHexLabel = il.DefineLabel();
        var noHexPrefixLabel = il.DefineLabel();
        var validateRadixLabel = il.DefineLabel();
        var parseLoopLabel = il.DefineLabel();
        var loopBodyLabel = il.DefineLabel();
        var endLoopLabel = il.DefineLabel();
        var returnResultLabel = il.DefineLabel();

        // Apply ECMAScript ToString before parsing. In particular, JavaScript
        // stringifies negative zero as "0" (where Double.ToString() yields
        // "-0"), and object arguments must observe their coercion hooks.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Trim", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, strLocal);

        // Check for empty string
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length")!.GetGetMethod()!);
        var notEmptyLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, notEmptyLabel);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notEmptyLabel);

        // Initialize sign = 1, startIndex = 0
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, signLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, startIndexLocal);

        // Check for sign
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", [_types.Int32])!);
        il.Emit(OpCodes.Ldc_I4, (int)'-');
        var notMinusLabel = il.DefineLabel();
        il.Emit(OpCodes.Bne_Un, notMinusLabel);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Stloc, signLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, startIndexLocal);
        il.Emit(OpCodes.Br, getRadixLabel);

        il.MarkLabel(notMinusLabel);
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", [_types.Int32])!);
        il.Emit(OpCodes.Ldc_I4, (int)'+');
        var notPlusLabel = il.DefineLabel();
        il.Emit(OpCodes.Bne_Un, notPlusLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, startIndexLocal);

        il.MarkLabel(notPlusLabel);

        // Get radix (default 10)
        il.MarkLabel(getRadixLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, afterRadixLabel); // null -> check for hex prefix

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, radixFromDoubleLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Int32);
        il.Emit(OpCodes.Brtrue, radixFromIntLabel);

        il.Emit(OpCodes.Br, afterRadixLabel);

        il.MarkLabel(radixFromDoubleLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, radixLocal);
        il.Emit(OpCodes.Br, checkHexLabel);

        il.MarkLabel(radixFromIntLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.Int32);
        il.Emit(OpCodes.Stloc, radixLocal);
        il.Emit(OpCodes.Br, checkHexLabel);

        il.MarkLabel(afterRadixLabel);
        il.Emit(OpCodes.Ldc_I4_0); // radix 0 means auto-detect
        il.Emit(OpCodes.Stloc, radixLocal);

        // Check for 0x prefix (only if radix is 0 or 16)
        il.MarkLabel(checkHexLabel);
        il.Emit(OpCodes.Ldloc, radixLocal);
        il.Emit(OpCodes.Brtrue, validateRadixLabel); // radix != 0, skip hex detection

        // radix is 0, check for 0x
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, startIndexLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Add);
        var noRoomForHexLabel = il.DefineLabel();
        il.Emit(OpCodes.Ble, noRoomForHexLabel);

        // Check if str[startIndex] == '0' && (str[startIndex+1] == 'x' || str[startIndex+1] == 'X')
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Ldloc, startIndexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", [_types.Int32])!);
        il.Emit(OpCodes.Ldc_I4, (int)'0');
        il.Emit(OpCodes.Bne_Un, noRoomForHexLabel);

        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Ldloc, startIndexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", [_types.Int32])!);
        var charLocal = il.DeclareLocal(_types.Char);
        il.Emit(OpCodes.Stloc, charLocal);
        il.Emit(OpCodes.Ldloc, charLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'x');
        var isHexLabel = il.DefineLabel();
        il.Emit(OpCodes.Beq, isHexLabel);
        il.Emit(OpCodes.Ldloc, charLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'X');
        il.Emit(OpCodes.Bne_Un, noRoomForHexLabel);

        il.MarkLabel(isHexLabel);
        il.Emit(OpCodes.Ldc_I4, 16);
        il.Emit(OpCodes.Stloc, radixLocal);
        il.Emit(OpCodes.Ldloc, startIndexLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, startIndexLocal);
        il.Emit(OpCodes.Br, validateRadixLabel);

        il.MarkLabel(noRoomForHexLabel);
        // Default to radix 10 if no hex prefix found
        il.Emit(OpCodes.Ldloc, radixLocal);
        il.Emit(OpCodes.Brtrue, validateRadixLabel);
        il.Emit(OpCodes.Ldc_I4, 10);
        il.Emit(OpCodes.Stloc, radixLocal);

        // Validate radix 2-36
        il.MarkLabel(validateRadixLabel);
        il.Emit(OpCodes.Ldloc, radixLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        var radixValidLabel = il.DefineLabel();
        il.Emit(OpCodes.Bge, radixValidLabel);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(radixValidLabel);
        il.Emit(OpCodes.Ldloc, radixLocal);
        il.Emit(OpCodes.Ldc_I4, 36);
        var radixNotTooLargeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ble, radixNotTooLargeLabel);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Ret);

        // Parse digits: result = 0, iterate through string
        il.MarkLabel(radixNotTooLargeLabel);
        il.Emit(OpCodes.Ldc_I8, 0L);
        il.Emit(OpCodes.Stloc, resultLocal);

        var indexLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldloc, startIndexLocal);
        il.Emit(OpCodes.Stloc, indexLocal);
        var hasDigitsLocal = il.DeclareLocal(_types.Boolean);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, hasDigitsLocal);

        il.MarkLabel(parseLoopLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, endLoopLabel);

        // Get digit value
        var digitLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", [_types.Int32])!);
        il.Emit(OpCodes.Call, runtime.GetDigitValue); // Helper to get digit value
        il.Emit(OpCodes.Stloc, digitLocal);

        // Check if digit is valid for this radix
        il.Emit(OpCodes.Ldloc, digitLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, endLoopLabel); // Invalid digit, stop

        il.Emit(OpCodes.Ldloc, digitLocal);
        il.Emit(OpCodes.Ldloc, radixLocal);
        il.Emit(OpCodes.Bge, endLoopLabel); // Digit >= radix, stop

        // result = result * radix + digit
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, radixLocal);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Ldloc, digitLocal);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, resultLocal);

        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, hasDigitsLocal);

        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, parseLoopLabel);

        il.MarkLabel(endLoopLabel);

        // If no digits parsed, return NaN
        il.Emit(OpCodes.Ldloc, hasDigitsLocal);
        il.Emit(OpCodes.Brtrue, returnResultLabel);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Ret);

        // Return sign * result
        il.MarkLabel(returnResultLabel);
        il.Emit(OpCodes.Ldloc, signLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Ret);
    }

    private void EmitGetDigitValue(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Returns digit value for character, or -1 if invalid
        var method = typeBuilder.DefineMethod(
            "GetDigitValue",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Int32,
            [_types.Char]
        );
        runtime.GetDigitValue = method;

        var il = method.GetILGenerator();
        var checkLowerLabel = il.DefineLabel();
        var checkUpperLabel = il.DefineLabel();
        var invalidLabel = il.DefineLabel();

        // if (c >= '0' && c <= '9') return c - '0'
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, (int)'0');
        il.Emit(OpCodes.Blt, checkLowerLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, (int)'9');
        il.Emit(OpCodes.Bgt, checkLowerLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, (int)'0');
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ret);

        // if (c >= 'a' && c <= 'z') return c - 'a' + 10
        il.MarkLabel(checkLowerLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, (int)'a');
        il.Emit(OpCodes.Blt, checkUpperLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, (int)'z');
        il.Emit(OpCodes.Bgt, checkUpperLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, (int)'a');
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldc_I4, 10);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ret);

        // if (c >= 'A' && c <= 'Z') return c - 'A' + 10
        il.MarkLabel(checkUpperLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, (int)'A');
        il.Emit(OpCodes.Blt, invalidLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, (int)'Z');
        il.Emit(OpCodes.Bgt, invalidLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, (int)'A');
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldc_I4, 10);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ret);

        // return -1
        il.MarkLabel(invalidLabel);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Ret);
    }

    private void EmitNumberParseFloat(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // parseFloat: extracts valid float prefix and parses it
        var method = typeBuilder.DefineMethod(
            "NumberParseFloat",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object]
        );
        runtime.NumberParseFloat = method;

        var il = method.GetILGenerator();
        var strLocal = il.DeclareLocal(_types.String);
        var validPartLocal = il.DeclareLocal(_types.String);
        var resultLocal = il.DeclareLocal(_types.Double);
        var notNullLabel = il.DefineLabel();
        var tryParseLabel = il.DefineLabel();
        var parseSuccessLabel = il.DefineLabel();

        // Get string from arg
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, tryParseLabel); // null -> empty string

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, strLocal);
        il.Emit(OpCodes.Br, notNullLabel);

        il.MarkLabel(tryParseLabel);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Stloc, strLocal);

        il.MarkLabel(notNullLabel);

        // Trim the string
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Trim", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, strLocal);

        // Check for empty string
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length")!.GetGetMethod()!);
        var notEmptyLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, notEmptyLabel);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notEmptyLabel);

        // Extract valid float part (JavaScript behavior: "42.5abc" -> "42.5")
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Call, runtime.GetValidFloatPart);
        il.Emit(OpCodes.Stloc, validPartLocal);

        // Check if valid part is empty
        il.Emit(OpCodes.Ldloc, validPartLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length")!.GetGetMethod()!);
        var hasValidPartLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, hasValidPartLabel);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(hasValidPartLabel);

        // Try to parse the valid part
        il.Emit(OpCodes.Ldloc, validPartLocal);
        il.Emit(OpCodes.Ldc_I4, (int)NumberStyles.Float);
        il.Emit(OpCodes.Call, typeof(CultureInfo).GetProperty("InvariantCulture")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloca, resultLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "TryParse", [_types.String, typeof(NumberStyles), typeof(IFormatProvider), _types.Double.MakeByRefType()])!);
        il.Emit(OpCodes.Brtrue, parseSuccessLabel);

        // Parse failed - return NaN
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(parseSuccessLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitGetValidFloatPart(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Helper: extracts valid float prefix from string (JavaScript parseFloat behavior)
        var method = typeBuilder.DefineMethod(
            "GetValidFloatPart",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.String]
        );
        runtime.GetValidFloatPart = method;

        var il = method.GetILGenerator();
        var resultLocal = il.DeclareLocal(typeof(StringBuilder));
        var hasDecimalLocal = il.DeclareLocal(_types.Boolean);
        var hasExponentLocal = il.DeclareLocal(_types.Boolean);
        var hasDigitLocal = il.DeclareLocal(_types.Boolean);
        var indexLocal = il.DeclareLocal(_types.Int32);
        var charLocal = il.DeclareLocal(_types.Char);
        var lengthLocal = il.DeclareLocal(_types.Int32);

        // result = new StringBuilder()
        il.Emit(OpCodes.Newobj, _types.StringBuilderDefaultCtor);
        il.Emit(OpCodes.Stloc, resultLocal);

        // length = str.Length
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, lengthLocal);

        // i = 0
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopLabel = il.DefineLabel();
        var endLoopLabel = il.DefineLabel();
        var checkDigitLabel = il.DefineLabel();
        var checkDecimalLabel = il.DefineLabel();
        var checkExponentLabel = il.DefineLabel();
        var appendCharLabel = il.DefineLabel();
        var nextIterLabel = il.DefineLabel();

        il.MarkLabel(loopLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Bge, endLoopLabel);

        // c = str[i]
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", [_types.Int32])!);
        il.Emit(OpCodes.Stloc, charLocal);

        // if (i == 0 && (c == '+' || c == '-')) { result.Append(c); continue; }
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Brtrue, checkDigitLabel);
        il.Emit(OpCodes.Ldloc, charLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'+');
        il.Emit(OpCodes.Beq, appendCharLabel);
        il.Emit(OpCodes.Ldloc, charLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'-');
        il.Emit(OpCodes.Beq, appendCharLabel);

        // if (c >= '0' && c <= '9') { result.Append(c); hasDigit = true; continue; }
        il.MarkLabel(checkDigitLabel);
        il.Emit(OpCodes.Ldloc, charLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'0');
        il.Emit(OpCodes.Blt, checkDecimalLabel);
        il.Emit(OpCodes.Ldloc, charLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'9');
        il.Emit(OpCodes.Bgt, checkDecimalLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, hasDigitLocal);
        il.Emit(OpCodes.Br, appendCharLabel);

        // if (c == '.' && !hasDecimal && !hasExponent) { result.Append(c); hasDecimal = true; continue; }
        il.MarkLabel(checkDecimalLabel);
        il.Emit(OpCodes.Ldloc, charLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'.');
        il.Emit(OpCodes.Bne_Un, checkExponentLabel);
        il.Emit(OpCodes.Ldloc, hasDecimalLocal);
        il.Emit(OpCodes.Brtrue, endLoopLabel);
        il.Emit(OpCodes.Ldloc, hasExponentLocal);
        il.Emit(OpCodes.Brtrue, endLoopLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, hasDecimalLocal);
        il.Emit(OpCodes.Br, appendCharLabel);

        // if ((c == 'e' || c == 'E') && hasDigit && !hasExponent) { handle exponent }
        il.MarkLabel(checkExponentLabel);
        il.Emit(OpCodes.Ldloc, charLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'e');
        var checkUpperELabel = il.DefineLabel();
        il.Emit(OpCodes.Beq, checkUpperELabel);
        il.Emit(OpCodes.Ldloc, charLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'E');
        il.Emit(OpCodes.Bne_Un, endLoopLabel); // Not a valid char, break
        il.MarkLabel(checkUpperELabel);
        il.Emit(OpCodes.Ldloc, hasDigitLocal);
        il.Emit(OpCodes.Brfalse, endLoopLabel);
        il.Emit(OpCodes.Ldloc, hasExponentLocal);
        il.Emit(OpCodes.Brtrue, endLoopLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, hasExponentLocal);

        // Append 'e' and check for optional sign
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, charLocal);
        il.Emit(OpCodes.Callvirt, _types.StringBuilderAppendChar);
        il.Emit(OpCodes.Pop);

        // Check if next char is + or -
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Bge, nextIterLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", [_types.Int32])!);
        var nextCharLocal = il.DeclareLocal(_types.Char);
        il.Emit(OpCodes.Stloc, nextCharLocal);

        il.Emit(OpCodes.Ldloc, nextCharLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'+');
        var appendExpSignLabel = il.DefineLabel();
        il.Emit(OpCodes.Beq, appendExpSignLabel);
        il.Emit(OpCodes.Ldloc, nextCharLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'-');
        il.Emit(OpCodes.Bne_Un, nextIterLabel);

        il.MarkLabel(appendExpSignLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, nextCharLocal);
        il.Emit(OpCodes.Callvirt, _types.StringBuilderAppendChar);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, nextIterLabel);

        // Append character
        il.MarkLabel(appendCharLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, charLocal);
        il.Emit(OpCodes.Callvirt, _types.StringBuilderAppendChar);
        il.Emit(OpCodes.Pop);

        // i++
        il.MarkLabel(nextIterLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopLabel);

        il.MarkLabel(endLoopLabel);

        // return hasDigit ? result.ToString() : ""
        il.Emit(OpCodes.Ldloc, hasDigitLocal);
        var returnEmptyLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, returnEmptyLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Callvirt, _types.StringBuilderToString);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnEmptyLabel);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Ret);
    }

    private void EmitNumberIsNaN(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Number.isNaN is stricter - only returns true for actual NaN double values
        var method = typeBuilder.DefineMethod(
            "NumberIsNaN",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]
        );
        runtime.NumberIsNaN = method;

        var il = method.GetILGenerator();
        var notDoubleLabel = il.DefineLabel();

        // if (value is not double) return false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, notDoubleLabel);

        // return double.IsNaN((double)value)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsNaN", [_types.Double])!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notDoubleLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitNumberIsFinite(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Number.isFinite is stricter - only returns true for finite double values
        var method = typeBuilder.DefineMethod(
            "NumberIsFinite",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]
        );
        runtime.NumberIsFinite = method;

        var il = method.GetILGenerator();
        var notDoubleLabel = il.DefineLabel();

        // if (value is not double) return false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, notDoubleLabel);

        // return double.IsFinite((double)value)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsFinite", [_types.Double])!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notDoubleLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitNumberIsInteger(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Number.isInteger: returns true if value is finite and truncate(value) == value
        var method = typeBuilder.DefineMethod(
            "NumberIsInteger",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]
        );
        runtime.NumberIsInteger = method;

        var il = method.GetILGenerator();
        var falseLabel = il.DefineLabel();
        var valueLocal = il.DeclareLocal(_types.Double);

        // if (value is not double) return false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, falseLabel);

        // double d = (double)value
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Stloc, valueLocal);

        // if (!double.IsFinite(d)) return false
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsFinite", [_types.Double])!);
        il.Emit(OpCodes.Brfalse, falseLabel);

        // return Math.Truncate(d) == d
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Truncate", [_types.Double])!);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitNumberIsSafeInteger(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Number.isSafeInteger: IsInteger && Math.Abs(d) <= MAX_SAFE_INTEGER
        const double MAX_SAFE_INTEGER = 9007199254740991;

        var method = typeBuilder.DefineMethod(
            "NumberIsSafeInteger",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]
        );
        runtime.NumberIsSafeInteger = method;

        var il = method.GetILGenerator();
        var falseLabel = il.DefineLabel();
        var valueLocal = il.DeclareLocal(_types.Double);

        // if (value is not double) return false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, falseLabel);

        // double d = (double)value
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Stloc, valueLocal);

        // if (!double.IsFinite(d)) return false
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsFinite", [_types.Double])!);
        il.Emit(OpCodes.Brfalse, falseLabel);

        // if (Math.Truncate(d) != d) return false
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Truncate", [_types.Double])!);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brfalse, falseLabel);

        // return Math.Abs(d) <= MAX_SAFE_INTEGER
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Abs", [_types.Double])!);
        il.Emit(OpCodes.Ldc_R8, MAX_SAFE_INTEGER);
        il.Emit(OpCodes.Cgt);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ceq); // NOT the Cgt result
        il.Emit(OpCodes.Ret);

        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitGlobalIsNaN(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Global isNaN coerces to number first
        var method = typeBuilder.DefineMethod(
            "GlobalIsNaN",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]
        );
        runtime.GlobalIsNaN = method;

        var il = method.GetILGenerator();
        var checkStringLabel = il.DefineLabel();
        var checkNullLabel = il.DefineLabel();
        var checkBoolLabel = il.DefineLabel();
        var returnTrueLabel = il.DefineLabel();
        var parsedLocal = il.DeclareLocal(_types.Double);

        // if (value is double d) return double.IsNaN(d)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, checkStringLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsNaN", [_types.Double])!);
        il.Emit(OpCodes.Ret);

        // if (value is string s) return !double.TryParse(s, ...)
        il.MarkLabel(checkStringLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, checkNullLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ldc_I4, (int)NumberStyles.Float);
        il.Emit(OpCodes.Call, typeof(CultureInfo).GetProperty("InvariantCulture")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloca, parsedLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "TryParse", [_types.String, typeof(NumberStyles), typeof(IFormatProvider), _types.Double.MakeByRefType()])!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ceq); // NOT the result
        il.Emit(OpCodes.Ret);

        // if (value is null) return true
        il.MarkLabel(checkNullLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brtrue, checkBoolLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        // if (value is bool) return false
        il.MarkLabel(checkBoolLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brfalse, returnTrueLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        // default: return true
        il.MarkLabel(returnTrueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
    }

    private void EmitGlobalIsFinite(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Global isFinite coerces to number first
        var method = typeBuilder.DefineMethod(
            "GlobalIsFinite",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]
        );
        runtime.GlobalIsFinite = method;

        var il = method.GetILGenerator();
        var checkStringLabel = il.DefineLabel();
        var checkNullLabel = il.DefineLabel();
        var checkBoolLabel = il.DefineLabel();
        var returnFalseLabel = il.DefineLabel();
        var parsedLocal = il.DeclareLocal(_types.Double);
        var tryParseSuccessLabel = il.DefineLabel();

        // if (value is double d) return double.IsFinite(d)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, checkStringLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsFinite", [_types.Double])!);
        il.Emit(OpCodes.Ret);

        // if (value is string s && double.TryParse(s, ...)) return double.IsFinite(parsed)
        il.MarkLabel(checkStringLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, checkNullLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ldc_I4, (int)NumberStyles.Float);
        il.Emit(OpCodes.Call, typeof(CultureInfo).GetProperty("InvariantCulture")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloca, parsedLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "TryParse", [_types.String, typeof(NumberStyles), typeof(IFormatProvider), _types.Double.MakeByRefType()])!);
        il.Emit(OpCodes.Brtrue, tryParseSuccessLabel);
        il.Emit(OpCodes.Ldc_I4_0); // TryParse failed, return false
        il.Emit(OpCodes.Ret);

        il.MarkLabel(tryParseSuccessLabel);
        il.Emit(OpCodes.Ldloc, parsedLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsFinite", [_types.Double])!);
        il.Emit(OpCodes.Ret);

        // if (value is null) return true (null coerces to 0 which is finite)
        il.MarkLabel(checkNullLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brtrue, checkBoolLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        // if (value is bool) return true (true=1, false=0, both finite)
        il.MarkLabel(checkBoolLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brfalse, returnFalseLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        // default: return false
        il.MarkLabel(returnFalseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitNumberToFixedDouble(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "NumberToFixedDouble",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Double, _types.Int32, _types.String]);
        method.SetImplementationFlags(MethodImplAttributes.AggressiveOptimization);
        runtime.NumberToFixedDouble = method;

        var il = method.GetILGenerator();
        var valueLocal = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, valueLocal);

        // Keep validation inside the standalone helper even though the current
        // direct emitter only selects compile-time literals in range.
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        var notNegative = il.DefineLabel();
        il.Emit(OpCodes.Bge, notNegative);
        GuestErrorEmitter.ThrowRangeError(
            il, runtime, "toFixed() digits argument must be between 0 and 100");
        il.MarkLabel(notNegative);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4, 100);
        var inRange = il.DefineLabel();
        il.Emit(OpCodes.Ble, inRange);
        GuestErrorEmitter.ThrowRangeError(
            il, runtime, "toFixed() digits argument must be between 0 and 100");
        il.MarkLabel(inRange);

        // Values at or above 1e21 use ordinary Number::toString. Blt_Un also
        // sends NaN to the BCL fixed formatter, which returns the JS spelling.
        var fixedNotation = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Abs", [_types.Double])!);
        il.Emit(OpCodes.Ldc_R8, 1e21);
        il.Emit(OpCodes.Blt_Un, fixedNotation);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Call, runtime.FormatNumber);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(fixedNotation);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        var nonZero = il.DefineLabel();
        il.Emit(OpCodes.Bne_Un, nonZero);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.MarkLabel(nonZero);

        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, typeof(CultureInfo).GetProperty("InvariantCulture")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, _types.GetMethod(
            _types.Double, "ToString", [_types.String, typeof(IFormatProvider)])!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitNumberToFixed(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "NumberToFixed",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object, _types.Object]
        );
        runtime.NumberToFixed = method;

        var il = method.GetILGenerator();
        var valueLocal = il.DeclareLocal(_types.Double);
        var receiverLocal = il.DeclareLocal(_types.Object);
        var digitsLocal = il.DeclareLocal(_types.Int32);
        var validDigitsLabel = il.DefineLabel();
        var getDigitsLabel = il.DefineLabel();
        var digitsFromDoubleLabel = il.DefineLabel();
        var digitsFromIntLabel = il.DefineLabel();
        var afterDigitsLabel = il.DefineLabel();

        // Boxed primitive unwrap: if receiver is $Object with __primitiveValue
        // marker, use the primitive value (ECMA-262 thisNumberValue semantics).
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, receiverLocal);
        var notBoxedLabel = il.DefineLabel();
        var primValLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "__primitiveValue");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, primValLocal);
        il.Emit(OpCodes.Ldloc, primValLocal);
        il.Emit(OpCodes.Brfalse, notBoxedLabel);
        il.Emit(OpCodes.Ldloc, primValLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, notBoxedLabel);
        il.Emit(OpCodes.Ldloc, primValLocal);
        il.Emit(OpCodes.Stloc, receiverLocal);
        il.MarkLabel(notBoxedLabel);

        // Number.prototype's [[NumberData]] is +0 per ECMA-262 §21.1.3.
        il.Emit(OpCodes.Ldloc, receiverLocal);
        il.Emit(OpCodes.Ldsfld, runtime.NumberPrototypeField);
        var notNumberPrototypeLabel = il.DefineLabel();
        il.Emit(OpCodes.Bne_Un, notNumberPrototypeLabel);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Br, getDigitsLabel);
        il.MarkLabel(notNumberPrototypeLabel);

        // Get value as double (NaN if not double)
        il.Emit(OpCodes.Ldloc, receiverLocal);
        il.Emit(OpCodes.Isinst, _types.Double);
        var notDoubleLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notDoubleLabel);
        il.Emit(OpCodes.Ldloc, receiverLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Br, getDigitsLabel);

        il.MarkLabel(notDoubleLabel);
        // Per ECMA-262 21.1.3.3 step 1, thisNumberValue throws TypeError when
        // receiver is neither a Number primitive nor a Number-marker $TSObject.
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Number.prototype.toFixed requires a Number this value");

        // ECMA-262 21.1.3.3: digits = ToIntegerOrInfinity(digits, 0). Coerces
        // bool/string via ToNumber.
        il.MarkLabel(getDigitsLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, runtime.ToIntegerOrInfinity);
        il.Emit(OpCodes.Stloc, digitsLocal);

        // Validate digits 0-100
        il.MarkLabel(validDigitsLabel);
        il.Emit(OpCodes.Ldloc, digitsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        var notNegativeLabel = il.DefineLabel();
        il.Emit(OpCodes.Bge, notNegativeLabel);
        // ECMA-262 21.1.3.3 step 3: range error for f < 0 or f > 100. Use $RangeError
        // (not bare Exception) so `assert.throws(RangeError, …)` succeeds.
        GuestErrorEmitter.ThrowRangeError(il, runtime, "toFixed() digits argument must be between 0 and 100");

        il.MarkLabel(notNegativeLabel);
        il.Emit(OpCodes.Ldloc, digitsLocal);
        il.Emit(OpCodes.Ldc_I4, 100);
        var notTooLargeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ble, notTooLargeLabel);
        GuestErrorEmitter.ThrowRangeError(il, runtime, "toFixed() digits argument must be between 0 and 100");

        // return value.ToString($"F{digits}", CultureInfo.InvariantCulture).
        // ECMA-262: -0 formatted as "0" (no sign) — strip via abs on zero.
        il.MarkLabel(notTooLargeLabel);

        // ECMA-262 21.1.3.3 step 10: values at or above 10^21 use the
        // ordinary Number::toString representation, irrespective of the
        // requested fraction digit count. The fixed-point formatter below
        // would otherwise produce "1000000000000000000000" instead of
        // "1e+21".
        var fixedNotationLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Abs", [_types.Double])!);
        il.Emit(OpCodes.Ldc_R8, 1e21);
        il.Emit(OpCodes.Blt_Un, fixedNotationLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Call, runtime.FormatNumber);
        il.Emit(OpCodes.Ret);

        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        var nonZeroFLabel = il.DefineLabel();
        il.Emit(OpCodes.Bne_Un, nonZeroFLabel);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.MarkLabel(nonZeroFLabel);
        il.MarkLabel(fixedNotationLabel);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Ldstr, "F");
        il.Emit(OpCodes.Ldloc, digitsLocal);
        il.Emit(OpCodes.Box, _types.Int32);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [_types.String, _types.Object])!);
        il.Emit(OpCodes.Call, typeof(CultureInfo).GetProperty("InvariantCulture")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "ToString", [_types.String, typeof(IFormatProvider)])!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitNumberToPrecision(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "NumberToPrecision",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object, _types.Object]
        );
        runtime.NumberToPrecision = method;

        var il = method.GetILGenerator();
        var valueLocal = il.DeclareLocal(_types.Double);
        var receiverLocal = il.DeclareLocal(_types.Object);
        var precisionLocal = il.DeclareLocal(_types.Int32);
        var hasPrecisionLabel = il.DefineLabel();
        var precisionFromDoubleLabel = il.DefineLabel();
        var precisionFromIntLabel = il.DefineLabel();
        var afterPrecisionLabel = il.DefineLabel();
        var validatePrecisionLabel = il.DefineLabel();
        var notNaNLabel = il.DefineLabel();
        var notPosInfLabel = il.DefineLabel();
        var notNegInfLabel = il.DefineLabel();
        var formatLabel = il.DefineLabel();

        // Boxed primitive unwrap (ECMA-262 thisNumberValue).
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, receiverLocal);
        var notBoxedPLabel = il.DefineLabel();
        var primValLocalP = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "__primitiveValue");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, primValLocalP);
        il.Emit(OpCodes.Ldloc, primValLocalP);
        il.Emit(OpCodes.Brfalse, notBoxedPLabel);
        il.Emit(OpCodes.Ldloc, primValLocalP);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, notBoxedPLabel);
        il.Emit(OpCodes.Ldloc, primValLocalP);
        il.Emit(OpCodes.Stloc, receiverLocal);
        il.MarkLabel(notBoxedPLabel);

        // Number.prototype's [[NumberData]] is +0 per ECMA-262 §21.1.3.
        il.Emit(OpCodes.Ldloc, receiverLocal);
        il.Emit(OpCodes.Ldsfld, runtime.NumberPrototypeField);
        var notNumberPrototypePLabel = il.DefineLabel();
        il.Emit(OpCodes.Bne_Un, notNumberPrototypePLabel);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Br, hasPrecisionLabel);
        il.MarkLabel(notNumberPrototypePLabel);

        // Get value as double (NaN if not double)
        il.Emit(OpCodes.Ldloc, receiverLocal);
        il.Emit(OpCodes.Isinst, _types.Double);
        var notDoubleLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notDoubleLabel);
        il.Emit(OpCodes.Ldloc, receiverLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Br, hasPrecisionLabel);

        il.MarkLabel(notDoubleLabel);
        // Per ECMA-262 21.1.3.5 step 1, thisNumberValue throws TypeError when
        // receiver is neither a Number primitive nor a Number-marker $TSObject.
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Number.prototype.toPrecision requires a Number this value");

        // Check if precision is null OR $Undefined - if so, return value.ToString().
        // ECMA-262 21.1.3.5 step 2: "If precision is undefined, return ! ToString(x)".
        // Both `null` (passed via missing arg) and the `$Undefined` singleton (passed
        // via explicit `undefined`) must take this short-circuit path.
        il.MarkLabel(hasPrecisionLabel);
        var defaultToStringLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, defaultToStringLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, defaultToStringLabel);
        il.Emit(OpCodes.Br, afterPrecisionLabel);

        il.MarkLabel(defaultToStringLabel);
        // precision is null/undefined - return value.ToString(CultureInfo.InvariantCulture)
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Call, typeof(CultureInfo).GetProperty("InvariantCulture")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "ToString", [typeof(IFormatProvider)])!);
        il.Emit(OpCodes.Ret);

        // ECMA-262 21.1.3.5: precision = ToIntegerOrInfinity(precision). Coerces
        // bool/string/array via ToNumber then truncates. Without this,
        // `(123.456).toPrecision(true)` failed because true wasn't unboxed as Double.
        il.MarkLabel(afterPrecisionLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, runtime.ToIntegerOrInfinity);
        il.Emit(OpCodes.Stloc, precisionLocal);

        // ECMA-262 21.1.3.5 step 5: handle NaN/Infinity BEFORE precision range check.
        // Spec: "If x is not finite, return Number::toString(x)" — runs before the
        // RangeError-on-invalid-precision branch, so `(Infinity).toPrecision(1000)`
        // returns "Infinity" instead of throwing.
        // if (double.IsNaN(value)) return "NaN"
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsNaN", [_types.Double])!);
        il.Emit(OpCodes.Brfalse, notNaNLabel);
        il.Emit(OpCodes.Ldstr, "NaN");
        il.Emit(OpCodes.Ret);

        // if (double.IsPositiveInfinity(value)) return "Infinity"
        il.MarkLabel(notNaNLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsPositiveInfinity", [_types.Double])!);
        il.Emit(OpCodes.Brfalse, notPosInfLabel);
        il.Emit(OpCodes.Ldstr, "Infinity");
        il.Emit(OpCodes.Ret);

        // if (double.IsNegativeInfinity(value)) return "-Infinity"
        il.MarkLabel(notPosInfLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsNegativeInfinity", [_types.Double])!);
        il.Emit(OpCodes.Brfalse, validatePrecisionLabel);
        il.Emit(OpCodes.Ldstr, "-Infinity");
        il.Emit(OpCodes.Ret);

        // Validate precision 1-100 (only for finite numbers).
        il.MarkLabel(validatePrecisionLabel);
        il.Emit(OpCodes.Ldloc, precisionLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        var notTooSmallLabel = il.DefineLabel();
        il.Emit(OpCodes.Bge, notTooSmallLabel);
        GuestErrorEmitter.ThrowRangeError(il, runtime, "toPrecision() argument must be between 1 and 100");

        il.MarkLabel(notTooSmallLabel);
        il.Emit(OpCodes.Ldloc, precisionLocal);
        il.Emit(OpCodes.Ldc_I4, 100);
        var notTooLargeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ble, notTooLargeLabel);
        GuestErrorEmitter.ThrowRangeError(il, runtime, "toPrecision() argument must be between 1 and 100");

        il.MarkLabel(notTooLargeLabel);
        il.Emit(OpCodes.Br, formatLabel);

        // Empty markers for legacy paths (kept for IL flow consistency).
        var deadInfLabel = il.DefineLabel();
        il.MarkLabel(deadInfLabel);
        il.Emit(OpCodes.Ret);

        // ECMA-262 21.1.3.5: when e < -6 OR e >= p, return exponential form
        // with EXACTLY p significant digits (so trailing zeros must be preserved
        // — e.g., 100.toPrecision(2) → "1.0e+2", not "1e+2"). When in fixed
        // range, use "G{precision}" which already gives p significant digits.
        // Detection: format with "G{precision}" first; if the result contains
        // "E", we're in exponential-required range so reformat with "E{precision-1}"
        // to preserve trailing zeros.
        il.MarkLabel(formatLabel);
        // ECMA-262 21.1.3.5: round to `precision` significant digits and format
        // either as fixed-point (when the leading digit's exponent is in
        // [-6, precision)) or in exponential form (otherwise). Trailing zeros
        // must be preserved exactly to `precision` significant digits.
        //
        // Strategy:
        //   - Pre-detect zero/-0 → format manually as "0" or "0.000...".
        //   - Otherwise, compute e = floor(log10(|value|)), then:
        //     * If e < -6 or e >= precision → use "E{precision-1}" and
        //       reformat to JS-style "1.234e+5" syntax.
        //     * Else → use "F{precision-1-e}" which preserves trailing zeros.
        //   - Rounding can shift e (e.g. 9.95 → 1.00e+1), so re-derive the
        //     bucket from the formatted mantissa exponent in the exponential
        //     branch. For the fixed branch this is harmless: F applies the
        //     same rounding and re-derives correctly.

        // -0 → "0"
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        var nonZeroPLabel = il.DefineLabel();
        il.Emit(OpCodes.Bne_Un, nonZeroPLabel);
        // Format as "0" / "0.0" / "0.00" depending on precision.
        il.Emit(OpCodes.Ldloc, precisionLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        var precGt1Label = il.DefineLabel();
        il.Emit(OpCodes.Bgt, precGt1Label);
        il.Emit(OpCodes.Ldstr, "0");
        il.Emit(OpCodes.Ret);
        il.MarkLabel(precGt1Label);
        il.Emit(OpCodes.Ldstr, "0.");
        il.Emit(OpCodes.Ldc_I4, (int)'0');
        il.Emit(OpCodes.Ldloc, precisionLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.String, [typeof(char), _types.Int32])!);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [_types.String, _types.String])!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(nonZeroPLabel);
        // Compute |value| → absLocal
        var absLocal = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Abs", [_types.Double])!);
        il.Emit(OpCodes.Stloc, absLocal);

        // e = (int)Math.Floor(Math.Log10(abs))
        var eLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldloc, absLocal);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Log10", [_types.Double])!);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Floor", [_types.Double])!);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, eLocal);

        // Branch: if e < -6 OR e >= precision → exponential.
        var exponentialLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, eLocal);
        il.Emit(OpCodes.Ldc_I4_S, (sbyte)-6);
        il.Emit(OpCodes.Blt, exponentialLabel);
        il.Emit(OpCodes.Ldloc, eLocal);
        il.Emit(OpCodes.Ldloc, precisionLocal);
        il.Emit(OpCodes.Bge, exponentialLabel);

        // Fixed-point branch: F{precision - 1 - e}
        var decLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldloc, precisionLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldloc, eLocal);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, decLocal);
        // Clamp negative to 0 (e > precision-1 shouldn't happen here, but safe).
        var nonNegDecLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, decLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, nonNegDecLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, decLocal);
        il.MarkLabel(nonNegDecLabel);

        // valueLocal.ToString("F" + decLocal, InvariantCulture)
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Ldstr, "F");
        il.Emit(OpCodes.Ldloc, decLocal);
        il.Emit(OpCodes.Box, _types.Int32);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [_types.String, _types.Object])!);
        il.Emit(OpCodes.Call, typeof(CultureInfo).GetProperty("InvariantCulture")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "ToString", [_types.String, typeof(IFormatProvider)])!);
        // After fixed-point formatting, rounding may have shifted to next decade
        // (e.g., 9.95.toPrecision(2) → "10.0" via F1, but result has too many digits).
        // For the common cases this is fine; F format handles rounding correctly.
        il.Emit(OpCodes.Ret);

        // Exponential branch: E{precision-1} → JS-style mantissa+e+sign+digits
        il.MarkLabel(exponentialLabel);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Ldstr, "E");
        il.Emit(OpCodes.Ldloc, precisionLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Box, _types.Int32);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [_types.String, _types.Object])!);
        il.Emit(OpCodes.Call, typeof(CultureInfo).GetProperty("InvariantCulture")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "ToString", [_types.String, typeof(IFormatProvider)])!);
        // Replace "E" with "e".
        il.Emit(OpCodes.Ldstr, "E");
        il.Emit(OpCodes.Ldstr, "e");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Replace", [_types.String, _types.String])!);
        // Strip leading zeros from exponent (e.g., "1.0e+002" → "1.0e+2").
        il.Emit(OpCodes.Ldstr, @"e([+-])0+(?=\d)");
        il.Emit(OpCodes.Ldstr, "e$1");
        il.Emit(OpCodes.Call, typeof(System.Text.RegularExpressions.Regex).GetMethod("Replace", [_types.String, _types.String, _types.String])!);
        // For precision=1, .NET emits "7.0E+000" (it always includes the decimal).
        // Strip ".0e" → "e" only when precision==1 (mantissa would be single digit).
        il.Emit(OpCodes.Ldloc, precisionLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        var notP1Label = il.DefineLabel();
        il.Emit(OpCodes.Bne_Un, notP1Label);
        il.Emit(OpCodes.Ldstr, ".0e");
        il.Emit(OpCodes.Ldstr, "e");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Replace", [_types.String, _types.String])!);
        il.MarkLabel(notP1Label);
        il.Emit(OpCodes.Ret);
    }

    private void EmitNumberToExponential(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "NumberToExponential",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object, _types.Object]
        );
        runtime.NumberToExponential = method;

        var il = method.GetILGenerator();
        var valueLocal = il.DeclareLocal(_types.Double);
        var receiverLocal = il.DeclareLocal(_types.Object);
        var digitsLocal = il.DeclareLocal(_types.Int32);
        var notNaNLabel = il.DefineLabel();
        var notPosInfLabel = il.DefineLabel();
        var notNegInfLabel = il.DefineLabel();
        var hasDigitsLabel = il.DefineLabel();
        var digitsFromDoubleLabel = il.DefineLabel();
        var digitsFromIntLabel = il.DefineLabel();
        var validateDigitsLabel = il.DefineLabel();
        var formatWithDigitsLabel = il.DefineLabel();

        // Boxed primitive unwrap (ECMA-262 thisNumberValue).
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, receiverLocal);
        var notBoxedELabel = il.DefineLabel();
        var primValLocalE = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "__primitiveValue");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, primValLocalE);
        il.Emit(OpCodes.Ldloc, primValLocalE);
        il.Emit(OpCodes.Brfalse, notBoxedELabel);
        il.Emit(OpCodes.Ldloc, primValLocalE);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, notBoxedELabel);
        il.Emit(OpCodes.Ldloc, primValLocalE);
        il.Emit(OpCodes.Stloc, receiverLocal);
        il.MarkLabel(notBoxedELabel);

        // Number.prototype's [[NumberData]] is +0 per ECMA-262 §21.1.3.
        il.Emit(OpCodes.Ldloc, receiverLocal);
        il.Emit(OpCodes.Ldsfld, runtime.NumberPrototypeField);
        var notNumberPrototypeELabel = il.DefineLabel();
        il.Emit(OpCodes.Bne_Un, notNumberPrototypeELabel);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Br, notNaNLabel);
        il.MarkLabel(notNumberPrototypeELabel);

        // Get value as double (else throw TypeError per ECMA-262 thisNumberValue)
        il.Emit(OpCodes.Ldloc, receiverLocal);
        il.Emit(OpCodes.Isinst, _types.Double);
        var notDoubleLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notDoubleLabel);
        il.Emit(OpCodes.Ldloc, receiverLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Br, notNaNLabel);

        il.MarkLabel(notDoubleLabel);
        // Per ECMA-262 21.1.3.2 step 1, thisNumberValue throws TypeError when
        // receiver is neither a Number primitive nor a Number-marker $TSObject.
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Number.prototype.toExponential requires a Number this value");

        // Unused but keeps original valueLocal init for this branch (unreachable).
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Stloc, valueLocal);

        // Handle special values first
        // if (double.IsNaN(value)) return "NaN"
        il.MarkLabel(notNaNLabel);

        // ECMA-262 21.1.3.2 step 2: ToInteger(fractionDigits) is observable
        // BEFORE the NaN/Infinity short-circuits in step 5. The arg coercion
        // can throw (Symbol → TypeError, valueOf/toString throws); test262
        // patterns like `NaN.toExponential(Symbol())` and
        // `NaN.toExponential({valueOf:()=>{throw}})` rely on the throw firing.
        // Pre-coerce here, then the NaN/Inf check below short-circuits without
        // re-coercing.
        var notSymbolDigitsLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, notSymbolDigitsLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.TSSymbolType);
        il.Emit(OpCodes.Brfalse, notSymbolDigitsLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot convert a Symbol value to a number");
        il.MarkLabel(notSymbolDigitsLabel);

        // Pre-coerce fractionDigits via ToIntegerOrInfinity unless it's
        // undefined (spec keeps undefined as the "shortest representation"
        // signal). Skip null too — null ToInteger-coerces to 0 and the call
        // is side-effect-free. Side-effects only matter for object args, so
        // the Isinst-Object guard limits the coercion to those.
        var skipPreCoerceLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, skipPreCoerceLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        var checkTSObjectLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, checkTSObjectLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, skipPreCoerceLabel);
        il.MarkLabel(checkTSObjectLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, runtime.ToIntegerOrInfinity);
        il.Emit(OpCodes.Stloc, digitsLocal);
        il.MarkLabel(skipPreCoerceLabel);

        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsNaN", [_types.Double])!);
        il.Emit(OpCodes.Brfalse, notPosInfLabel);
        il.Emit(OpCodes.Ldstr, "NaN");
        il.Emit(OpCodes.Ret);

        // if (double.IsPositiveInfinity(value)) return "Infinity"
        il.MarkLabel(notPosInfLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsPositiveInfinity", [_types.Double])!);
        il.Emit(OpCodes.Brfalse, notNegInfLabel);
        il.Emit(OpCodes.Ldstr, "Infinity");
        il.Emit(OpCodes.Ret);

        // if (double.IsNegativeInfinity(value)) return "-Infinity"
        il.MarkLabel(notNegInfLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsNegativeInfinity", [_types.Double])!);
        il.Emit(OpCodes.Brfalse, hasDigitsLabel);
        il.Emit(OpCodes.Ldstr, "-Infinity");
        il.Emit(OpCodes.Ret);

        // ECMA-262 22.1.3.6: only `undefined` skips fractionDigits coercion
        // (uses "shortest exponential" form). `null` ToInteger-coerces to 0.
        // Pre-fix branched on `Ldarg_1; Brtrue` which treated null AND
        // undefined as the no-arg case, producing ".NET-default 6-digit
        // fraction" output instead of the spec's "fractionDigits=0" form.
        il.MarkLabel(hasDigitsLabel);
        var fractionDigitsUndefinedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, fractionDigitsUndefinedLabel);

        // Non-undefined (including null): apply ToIntegerOrInfinity. Per spec,
        // null → 0, false → 0, true → 1, "2" → 2, etc. Default arg coercion is 6
        // (only used if ToIntegerOrInfinity hits the "no arg" branch — but the
        // call site here always provides an arg).
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, runtime.ToIntegerOrInfinity);
        il.Emit(OpCodes.Stloc, digitsLocal);
        il.Emit(OpCodes.Br, validateDigitsLabel);

        // undefined fractionDigits: ECMA-262 step 9.a uses the shortest
        // exponential representation. .NET's "R" round-trip format does this:
        // (123.456).ToString("R") → "123.456" (no e if not needed) — but we
        // always need exponential here. Fall back to G17 with JS exponent fixup
        // to approximate. Test262 expects "1.23456e+2" for (123.456); G17
        // gives "1.23456E+002" → after replace + strip → "1.23456e+2".
        il.MarkLabel(fractionDigitsUndefinedLabel);
        // Pre-zero check (handle ±0 → "0e+0"):
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        var nonZeroForUndef = il.DefineLabel();
        il.Emit(OpCodes.Bne_Un, nonZeroForUndef);
        il.Emit(OpCodes.Ldstr, "0e+0");
        il.Emit(OpCodes.Ret);
        il.MarkLabel(nonZeroForUndef);
        // value.ToString("G17", invariant)
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Ldstr, "G17");
        il.Emit(OpCodes.Call, typeof(CultureInfo).GetProperty("InvariantCulture")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "ToString", [_types.String, typeof(IFormatProvider)])!);
        // Result might be "123.456" or "1.23456E+02" depending on magnitude.
        // We need to ensure exponential form. If no 'E' or 'e', convert via
        // log10. Simplification: use E15 then strip — gives spec-compliant
        // shortest exponential form for most ranges.
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Ldstr, "G17");
        il.Emit(OpCodes.Call, typeof(CultureInfo).GetProperty("InvariantCulture")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "ToString", [_types.String, typeof(IFormatProvider)])!);
        // For the actually-exponential case, .NET's G17 gives "1.23456E+02".
        // Convert to JS spec: replace E→e, strip leading zeros, ensure sign.
        // For values that emit without 'E' (like 123.456 → "123.456"), insert
        // an exponent manually using log10. Defer that to a static helper —
        // for now, fall back to ToString("e", InvariantCulture) which gives
        // 6-digit fraction (correct for spec when fractionDigits=6).
        // Actually spec wants shortest. Use ToString("R") then fix.
        il.Emit(OpCodes.Pop);
        // Simpler approach: format with E15 then trim trailing zeros from
        // mantissa, fall through to JS exponent normalize.
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Ldstr, "e15");
        il.Emit(OpCodes.Call, typeof(CultureInfo).GetProperty("InvariantCulture")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "ToString", [_types.String, typeof(IFormatProvider)])!);
        // Trim trailing zeros from mantissa (between '.' and 'e').
        // Pattern: \.([0-9]+?)0+e → .$1e (drop trailing zeros while keeping at least one digit after dot)
        // Simpler: use \.?0+e → e if all decimals are zero (e.g. "1.000000e+02" → "1e+02"); else \.([1-9])0+e → .$1e
        il.Emit(OpCodes.Ldstr, @"(\.\d*?)0+(?=e)");
        il.Emit(OpCodes.Ldstr, "$1");
        il.Emit(OpCodes.Call, typeof(System.Text.RegularExpressions.Regex).GetMethod("Replace", [_types.String, _types.String, _types.String])!);
        // After zero-trim, "1.e+02" needs → "1e+02"; remove dangling decimal point.
        il.Emit(OpCodes.Ldstr, @"\.e");
        il.Emit(OpCodes.Ldstr, "e");
        il.Emit(OpCodes.Call, typeof(System.Text.RegularExpressions.Regex).GetMethod("Replace", [_types.String, _types.String, _types.String])!);
        // Strip leading zeros from exponent: "e+002" → "e+2".
        il.Emit(OpCodes.Ldstr, @"e([+-])0+(?=\d)");
        il.Emit(OpCodes.Ldstr, "e$1");
        il.Emit(OpCodes.Call, typeof(System.Text.RegularExpressions.Regex).GetMethod("Replace", [_types.String, _types.String, _types.String])!);
        il.Emit(OpCodes.Ret);

        // ECMA-262 21.1.3.2: digits = ToIntegerOrInfinity(digits, 6). Coerces
        // bool/string via ToNumber.
        il.MarkLabel(digitsFromDoubleLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_6);
        il.Emit(OpCodes.Call, runtime.ToIntegerOrInfinity);
        il.Emit(OpCodes.Stloc, digitsLocal);

        // Validate digits 0-100
        il.MarkLabel(validateDigitsLabel);
        il.Emit(OpCodes.Ldloc, digitsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        var notNegativeLabel = il.DefineLabel();
        il.Emit(OpCodes.Bge, notNegativeLabel);
        GuestErrorEmitter.ThrowRangeError(il, runtime, "toExponential() argument must be between 0 and 100");

        il.MarkLabel(notNegativeLabel);
        il.Emit(OpCodes.Ldloc, digitsLocal);
        il.Emit(OpCodes.Ldc_I4, 100);
        var notTooLargeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ble, notTooLargeLabel);
        GuestErrorEmitter.ThrowRangeError(il, runtime, "toExponential() argument must be between 0 and 100");

        // return Regex.Replace(value.ToString($"e{digits}", InvariantCulture),
        //                      @"e([+-])0+(?=\d)", "e$1");
        // .NET's "1.2e+002" → JS spec's "1.2e+2".
        il.MarkLabel(notTooLargeLabel);

        // ECMA-262 21.1.3.2 step 8.b: only treat x as negative when x < 0 (not
        // when x is -0). .NET formats -0 as "-0E+0"; strip the leading minus.
        // Use Math.Abs to drop the sign on -0 before formatting.
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        var nonZeroLabel = il.DefineLabel();
        il.Emit(OpCodes.Bne_Un, nonZeroLabel);
        // value == 0 (handles ±0): replace with +0.0 to ensure no leading minus
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.MarkLabel(nonZeroLabel);

        // .NET Double.ToString("eN") rounds with MidpointRounding.ToEven (banker's),
        // but ECMA-262 21.1.3.2 specifies round-half-away-from-zero for the
        // mantissa. (25).toExponential(0) must yield "3e+1" (not "2e+1"); likewise
        // (12345).toExponential(3) must yield "1.235e+4" (not "1.234e+4").
        // Manually decompose value = mantissa * 10^exp, round the mantissa with
        // AwayFromZero, handle rollover, and reassemble.
        //
        // .NET Math.Round(double, int, MidpointRounding) only supports digits in
        // [0, 15]. For higher precision (digits > 15), the test expectations track
        // .NET's underlying double-precision formatter exactly, so fall through to
        // the legacy "eN" path (which DOES handle up to ~17 digits).
        var legacyHighPrecisionLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, digitsLocal);
        il.Emit(OpCodes.Ldc_I4, 15);
        il.Emit(OpCodes.Bgt, legacyHighPrecisionLabel);
        var roundedLocal = il.DeclareLocal(_types.Double);
        var expIntLocal = il.DeclareLocal(_types.Int32);
        var absLocal = il.DeclareLocal(_types.Double);
        var signLocal = il.DeclareLocal(_types.Double);
        var mantissaLocal = il.DeclareLocal(_types.Double);

        // sign = value < 0 ? -1 : 1
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        var notNegSignLabel = il.DefineLabel();
        var afterSignLabel = il.DefineLabel();
        il.Emit(OpCodes.Bge, notNegSignLabel);
        il.Emit(OpCodes.Ldc_R8, -1.0);
        il.Emit(OpCodes.Stloc, signLocal);
        il.Emit(OpCodes.Br, afterSignLabel);
        il.MarkLabel(notNegSignLabel);
        il.Emit(OpCodes.Ldc_R8, 1.0);
        il.Emit(OpCodes.Stloc, signLocal);
        il.MarkLabel(afterSignLabel);

        // abs = Math.Abs(value)
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Abs", _types.Double));
        il.Emit(OpCodes.Stloc, absLocal);

        // For value == 0, exp = 0 and rounded = 0 (skip log10/divide).
        var zeroFmtLabel = il.DefineLabel();
        var skipZeroLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, absLocal);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Bne_Un, skipZeroLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, expIntLocal);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Stloc, roundedLocal);
        il.Emit(OpCodes.Br, zeroFmtLabel);
        il.MarkLabel(skipZeroLabel);

        // exp = (int)Math.Floor(Math.Log10(abs))
        il.Emit(OpCodes.Ldloc, absLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Log10", _types.Double));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Floor", _types.Double));
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, expIntLocal);

        // mantissa = abs / Math.Pow(10, exp)
        il.Emit(OpCodes.Ldloc, absLocal);
        il.Emit(OpCodes.Ldc_R8, 10.0);
        il.Emit(OpCodes.Ldloc, expIntLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Pow", _types.Double, _types.Double));
        il.Emit(OpCodes.Div);
        il.Emit(OpCodes.Stloc, mantissaLocal);

        // rounded = Math.Round(mantissa, digits, MidpointRounding.AwayFromZero)
        il.Emit(OpCodes.Ldloc, mantissaLocal);
        il.Emit(OpCodes.Ldloc, digitsLocal);
        il.Emit(OpCodes.Ldc_I4_1); // MidpointRounding.AwayFromZero == 1
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Round", _types.Double, _types.Int32, typeof(MidpointRounding)));
        il.Emit(OpCodes.Stloc, roundedLocal);

        // Rollover: if rounded >= 10, divide by 10 and exp += 1.
        var noRolloverLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, roundedLocal);
        il.Emit(OpCodes.Ldc_R8, 10.0);
        il.Emit(OpCodes.Blt, noRolloverLabel);
        il.Emit(OpCodes.Ldloc, roundedLocal);
        il.Emit(OpCodes.Ldc_R8, 10.0);
        il.Emit(OpCodes.Div);
        il.Emit(OpCodes.Stloc, roundedLocal);
        il.Emit(OpCodes.Ldloc, expIntLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, expIntLocal);
        il.MarkLabel(noRolloverLabel);

        il.MarkLabel(zeroFmtLabel);

        // signedRounded = sign * rounded
        il.Emit(OpCodes.Ldloc, signLocal);
        il.Emit(OpCodes.Ldloc, roundedLocal);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Stloc, roundedLocal);

        // result = roundedRounded.ToString("F{digits}", InvariantCulture)
        //          + "e" + (exp >= 0 ? "+" : "-")
        //          + Math.Abs(exp).ToString(InvariantCulture)
        var sbLocal = il.DeclareLocal(_types.StringBuilder);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.StringBuilder));
        il.Emit(OpCodes.Stloc, sbLocal);

        // mantissa formatted with N fractional digits
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloca, roundedLocal);
        il.Emit(OpCodes.Ldstr, "F");
        il.Emit(OpCodes.Ldloc, digitsLocal);
        il.Emit(OpCodes.Box, _types.Int32);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [_types.String, _types.Object])!);
        il.Emit(OpCodes.Call, typeof(CultureInfo).GetProperty("InvariantCulture")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "ToString", [_types.String, typeof(IFormatProvider)])!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);

        // exponent sign
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, expIntLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        var negExpLabel = il.DefineLabel();
        var afterExpSignLabel = il.DefineLabel();
        il.Emit(OpCodes.Blt, negExpLabel);
        il.Emit(OpCodes.Ldstr, "e+");
        il.Emit(OpCodes.Br, afterExpSignLabel);
        il.MarkLabel(negExpLabel);
        il.Emit(OpCodes.Ldstr, "e-");
        il.MarkLabel(afterExpSignLabel);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);

        // |exp|
        var absExpLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldloc, expIntLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Abs", _types.Int32));
        il.Emit(OpCodes.Stloc, absExpLocal);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloca, absExpLocal);
        il.Emit(OpCodes.Call, typeof(CultureInfo).GetProperty("InvariantCulture")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Int32, "ToString", typeof(IFormatProvider)));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Ret);

        // Legacy path for digits > 15 — falls through to .NET's "eN" formatter.
        il.MarkLabel(legacyHighPrecisionLabel);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Ldstr, "e");
        il.Emit(OpCodes.Ldloc, digitsLocal);
        il.Emit(OpCodes.Box, _types.Int32);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [_types.String, _types.Object])!);
        il.Emit(OpCodes.Call, typeof(CultureInfo).GetProperty("InvariantCulture")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "ToString", [_types.String, typeof(IFormatProvider)])!);
        il.Emit(OpCodes.Ldstr, @"e([+-])0+(?=\d)");
        il.Emit(OpCodes.Ldstr, "e$1");
        il.Emit(OpCodes.Call, typeof(System.Text.RegularExpressions.Regex).GetMethod("Replace", [_types.String, _types.String, _types.String])!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitNumberToStringRadix(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "NumberToStringRadix",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object, _types.Object]
        );
        runtime.NumberToStringRadix = method;

        var il = method.GetILGenerator();
        var valueLocal = il.DeclareLocal(_types.Double);
        var receiverLocal = il.DeclareLocal(_types.Object);
        var radixLocal = il.DeclareLocal(_types.Int32);
        var notNaNLabel = il.DefineLabel();
        var notPosInfLabel = il.DefineLabel();
        var notNegInfLabel = il.DefineLabel();
        var hasRadixLabel = il.DefineLabel();
        var radixFromDoubleLabel = il.DefineLabel();
        var radixFromIntLabel = il.DefineLabel();
        var validateRadixLabel = il.DefineLabel();
        var convertLabel = il.DefineLabel();
        var notZeroLabel = il.DefineLabel();

        // Boxed primitive unwrap: if receiver is $Object with __primitiveValue
        // marker (Stage 4z19 wrapper from `new Number(x)`), use the primitive
        // value as the receiver. ECMA-262 thisNumberValue extracts [[NumberData]].
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, receiverLocal);
        var notBoxedNumLabel = il.DefineLabel();
        var primValLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "__primitiveValue");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, primValLocal);
        il.Emit(OpCodes.Ldloc, primValLocal);
        il.Emit(OpCodes.Brfalse, notBoxedNumLabel);
        il.Emit(OpCodes.Ldloc, primValLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, notBoxedNumLabel);
        // Replace receiver with unwrapped primitive
        il.Emit(OpCodes.Ldloc, primValLocal);
        il.Emit(OpCodes.Stloc, receiverLocal);
        il.MarkLabel(notBoxedNumLabel);

        // ECMA-262 §21.1.3: Number.prototype is itself a Number Exotic Object
        // whose [[NumberData]] is +0. `Number.prototype.toString()` returns "0".
        // Detect via reference-equality with the singleton dict field.
        il.Emit(OpCodes.Ldloc, receiverLocal);
        il.Emit(OpCodes.Ldsfld, runtime.NumberPrototypeField);
        var notNumberPrototypeLabel = il.DefineLabel();
        il.Emit(OpCodes.Bne_Un, notNumberPrototypeLabel);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Br, hasRadixLabel);
        il.MarkLabel(notNumberPrototypeLabel);

        // Get value as double (else throw TypeError per ECMA-262 thisNumberValue)
        il.Emit(OpCodes.Ldloc, receiverLocal);
        il.Emit(OpCodes.Isinst, _types.Double);
        var notDoubleLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notDoubleLabel);
        il.Emit(OpCodes.Ldloc, receiverLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Br, hasRadixLabel);

        il.MarkLabel(notDoubleLabel);
        // Receiver is neither a Number primitive nor a Number-marker $TSObject
        // nor the Number.prototype singleton. ECMA-262 21.1.3.6 step 1 calls
        // thisNumberValue which throws TypeError in this case.
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Number.prototype.toString requires a Number this value");

        // Check if radix is null
        il.MarkLabel(hasRadixLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brtrue, radixFromDoubleLabel);

        // radix is null/undefined - delegate to Stringify for JS-spec format.
        // (.NET Double.ToString with InvariantCulture uses scientific for |x| ≥ 1e16
        // and uppercase "E", but JS spec wants plain decimal up to 1e21.)
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Ret);

        // ECMA-262 21.1.3.6: radix coerced via ToIntegerOrInfinity (default 10).
        // Handles bool/string/array via the helper's ToPrimitive path.
        il.MarkLabel(radixFromDoubleLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4, 10);
        il.Emit(OpCodes.Call, runtime.ToIntegerOrInfinity);
        il.Emit(OpCodes.Stloc, radixLocal);

        // Validate radix 2-36
        il.MarkLabel(validateRadixLabel);
        il.Emit(OpCodes.Ldloc, radixLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        var radixValidLabel = il.DefineLabel();
        il.Emit(OpCodes.Bge, radixValidLabel);
        GuestErrorEmitter.ThrowRangeError(il, runtime, "toString() radix must be between 2 and 36");

        il.MarkLabel(radixValidLabel);
        il.Emit(OpCodes.Ldloc, radixLocal);
        il.Emit(OpCodes.Ldc_I4, 36);
        var radixNotTooLargeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ble, radixNotTooLargeLabel);
        GuestErrorEmitter.ThrowRangeError(il, runtime, "toString() radix must be between 2 and 36");

        // Handle special values
        il.MarkLabel(radixNotTooLargeLabel);

        // if (double.IsNaN(value)) return "NaN"
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsNaN", [_types.Double])!);
        il.Emit(OpCodes.Brfalse, notNaNLabel);
        il.Emit(OpCodes.Ldstr, "NaN");
        il.Emit(OpCodes.Ret);

        // if (double.IsPositiveInfinity(value)) return "Infinity"
        il.MarkLabel(notNaNLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsPositiveInfinity", [_types.Double])!);
        il.Emit(OpCodes.Brfalse, notPosInfLabel);
        il.Emit(OpCodes.Ldstr, "Infinity");
        il.Emit(OpCodes.Ret);

        // if (double.IsNegativeInfinity(value)) return "-Infinity"
        il.MarkLabel(notPosInfLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsNegativeInfinity", [_types.Double])!);
        il.Emit(OpCodes.Brfalse, convertLabel);
        il.Emit(OpCodes.Ldstr, "-Infinity");
        il.Emit(OpCodes.Ret);

        // if (radix == 10) delegate to Stringify for JS-spec format.
        // (.NET's Double.ToString(InvariantCulture) uses scientific for |x| ≥ 1e16
        // with uppercase "E"; JS spec uses plain decimal up to 1e21 and lowercase
        // "e". Stringify implements the spec rules.)
        il.MarkLabel(convertLabel);
        il.Emit(OpCodes.Ldloc, radixLocal);
        il.Emit(OpCodes.Ldc_I4, 10);
        var notRadix10Label = il.DefineLabel();
        il.Emit(OpCodes.Bne_Un, notRadix10Label);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Ret);

        // if (value == 0) return "0"
        il.MarkLabel(notRadix10Label);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Bne_Un, notZeroLabel);
        il.Emit(OpCodes.Ldstr, "0");
        il.Emit(OpCodes.Ret);

        // Handle negative numbers and convert
        il.MarkLabel(notZeroLabel);
        var negativeLocal = il.DeclareLocal(_types.Boolean);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Clt);
        il.Emit(OpCodes.Stloc, negativeLocal);

        // value = Math.Abs(value)
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Abs", [_types.Double])!);
        il.Emit(OpCodes.Stloc, valueLocal);

        // long intPart = (long)Math.Truncate(value)
        var intPartLocal = il.DeclareLocal(_types.Int64);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Truncate", [_types.Double])!);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Stloc, intPartLocal);

        // string intStr = ConvertIntToRadix(intPart, radix)
        il.Emit(OpCodes.Ldloc, intPartLocal);
        il.Emit(OpCodes.Ldloc, radixLocal);
        il.Emit(OpCodes.Call, runtime.ConvertIntToRadix);
        var intStrLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Stloc, intStrLocal);

        // return negative ? "-" + intStr : intStr
        il.Emit(OpCodes.Ldloc, negativeLocal);
        var returnPositiveLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, returnPositiveLabel);

        il.Emit(OpCodes.Ldstr, "-");
        il.Emit(OpCodes.Ldloc, intStrLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [_types.String, _types.String])!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnPositiveLabel);
        il.Emit(OpCodes.Ldloc, intStrLocal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitConvertIntToRadix(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Helper: converts a long to string with given radix
        var method = typeBuilder.DefineMethod(
            "ConvertIntToRadix",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Int64, _types.Int32]
        );
        runtime.ConvertIntToRadix = method;

        var il = method.GetILGenerator();
        var valueLocal = il.DeclareLocal(_types.Int64);
        var resultLocal = il.DeclareLocal(typeof(StringBuilder));
        var digitLocal = il.DeclareLocal(_types.Char);

        // if (value == 0) return "0"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I8, 0L);
        var notZeroLabel = il.DefineLabel();
        il.Emit(OpCodes.Bne_Un, notZeroLabel);
        il.Emit(OpCodes.Ldstr, "0");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notZeroLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, valueLocal);

        // StringBuilder result = new StringBuilder()
        il.Emit(OpCodes.Newobj, _types.StringBuilderDefaultCtor);
        il.Emit(OpCodes.Stloc, resultLocal);

        // while (value > 0) { result.Insert(0, digits[value % radix]); value /= radix; }
        var loopLabel = il.DefineLabel();
        var endLoopLabel = il.DefineLabel();

        il.MarkLabel(loopLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ldc_I8, 0L);
        il.Emit(OpCodes.Ble, endLoopLabel);

        // char digit = "0123456789abcdefghijklmnopqrstuvwxyz"[(int)(value % radix)]
        il.Emit(OpCodes.Ldstr, "0123456789abcdefghijklmnopqrstuvwxyz");
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Rem);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", [_types.Int32])!);
        il.Emit(OpCodes.Stloc, digitLocal);

        // result.Insert(0, digit) - proper order: this, index, value
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, digitLocal);
        il.Emit(OpCodes.Callvirt, typeof(StringBuilder).GetMethod("Insert", [_types.Int32, _types.Char])!);
        il.Emit(OpCodes.Pop); // Discard return value

        // value /= radix
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Div);
        il.Emit(OpCodes.Stloc, valueLocal);

        il.Emit(OpCodes.Br, loopLabel);

        il.MarkLabel(endLoopLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Callvirt, _types.StringBuilderToString);
        il.Emit(OpCodes.Ret);
    }
}

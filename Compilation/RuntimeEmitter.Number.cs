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
        EmitNumberToFixed(typeBuilder, runtime);
        EmitNumberToPrecision(typeBuilder, runtime);
        EmitNumberToExponential(typeBuilder, runtime);
        EmitNumberToStringRadix(typeBuilder, runtime);
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

        // Get string from arg
        il.Emit(OpCodes.Ldarg_0);
        var notNullStrLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, notNullStrLabel);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notNullStrLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("Trim", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, strLocal);

        // Check for empty string
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Callvirt, _types.String.GetProperty("Length")!.GetGetMethod()!);
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
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("get_Chars", [_types.Int32])!);
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
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("get_Chars", [_types.Int32])!);
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
        il.Emit(OpCodes.Callvirt, _types.String.GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, startIndexLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Add);
        var noRoomForHexLabel = il.DefineLabel();
        il.Emit(OpCodes.Ble, noRoomForHexLabel);

        // Check if str[startIndex] == '0' && (str[startIndex+1] == 'x' || str[startIndex+1] == 'X')
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Ldloc, startIndexLocal);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("get_Chars", [_types.Int32])!);
        il.Emit(OpCodes.Ldc_I4, (int)'0');
        il.Emit(OpCodes.Bne_Un, noRoomForHexLabel);

        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Ldloc, startIndexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("get_Chars", [_types.Int32])!);
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
        il.Emit(OpCodes.Callvirt, _types.String.GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, endLoopLabel);

        // Get digit value
        var digitLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("get_Chars", [_types.Int32])!);
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
        il.Emit(OpCodes.Callvirt, _types.Object.GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, strLocal);
        il.Emit(OpCodes.Br, notNullLabel);

        il.MarkLabel(tryParseLabel);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Stloc, strLocal);

        il.MarkLabel(notNullLabel);

        // Trim the string
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("Trim", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, strLocal);

        // Check for empty string
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Callvirt, _types.String.GetProperty("Length")!.GetGetMethod()!);
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
        il.Emit(OpCodes.Callvirt, _types.String.GetProperty("Length")!.GetGetMethod()!);
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
        il.Emit(OpCodes.Call, _types.Double.GetMethod("TryParse", [_types.String, typeof(NumberStyles), typeof(IFormatProvider), _types.Double.MakeByRefType()])!);
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
        il.Emit(OpCodes.Newobj, typeof(StringBuilder).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);

        // length = str.Length
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.String.GetProperty("Length")!.GetGetMethod()!);
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
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("get_Chars", [_types.Int32])!);
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
        il.Emit(OpCodes.Callvirt, typeof(StringBuilder).GetMethod("Append", [_types.Char])!);
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
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("get_Chars", [_types.Int32])!);
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
        il.Emit(OpCodes.Callvirt, typeof(StringBuilder).GetMethod("Append", [_types.Char])!);
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
        il.Emit(OpCodes.Callvirt, typeof(StringBuilder).GetMethod("Append", [_types.Char])!);
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
        il.Emit(OpCodes.Callvirt, typeof(StringBuilder).GetMethod("ToString", Type.EmptyTypes)!);
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
        il.Emit(OpCodes.Call, _types.Double.GetMethod("IsNaN", [_types.Double])!);
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
        il.Emit(OpCodes.Call, _types.Double.GetMethod("IsFinite", [_types.Double])!);
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
        il.Emit(OpCodes.Call, _types.Double.GetMethod("IsFinite", [_types.Double])!);
        il.Emit(OpCodes.Brfalse, falseLabel);

        // return Math.Truncate(d) == d
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Call, _types.Math.GetMethod("Truncate", [_types.Double])!);
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
        il.Emit(OpCodes.Call, _types.Double.GetMethod("IsFinite", [_types.Double])!);
        il.Emit(OpCodes.Brfalse, falseLabel);

        // if (Math.Truncate(d) != d) return false
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Call, _types.Math.GetMethod("Truncate", [_types.Double])!);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brfalse, falseLabel);

        // return Math.Abs(d) <= MAX_SAFE_INTEGER
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Call, _types.Math.GetMethod("Abs", [_types.Double])!);
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
        il.Emit(OpCodes.Call, _types.Double.GetMethod("IsNaN", [_types.Double])!);
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
        il.Emit(OpCodes.Call, _types.Double.GetMethod("TryParse", [_types.String, typeof(NumberStyles), typeof(IFormatProvider), _types.Double.MakeByRefType()])!);
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
        il.Emit(OpCodes.Call, _types.Double.GetMethod("IsFinite", [_types.Double])!);
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
        il.Emit(OpCodes.Call, _types.Double.GetMethod("TryParse", [_types.String, typeof(NumberStyles), typeof(IFormatProvider), _types.Double.MakeByRefType()])!);
        il.Emit(OpCodes.Brtrue, tryParseSuccessLabel);
        il.Emit(OpCodes.Ldc_I4_0); // TryParse failed, return false
        il.Emit(OpCodes.Ret);

        il.MarkLabel(tryParseSuccessLabel);
        il.Emit(OpCodes.Ldloc, parsedLocal);
        il.Emit(OpCodes.Call, _types.Double.GetMethod("IsFinite", [_types.Double])!);
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
        var digitsLocal = il.DeclareLocal(_types.Int32);
        var validDigitsLabel = il.DefineLabel();
        var getDigitsLabel = il.DefineLabel();
        var digitsFromDoubleLabel = il.DefineLabel();
        var digitsFromIntLabel = il.DefineLabel();
        var afterDigitsLabel = il.DefineLabel();

        // Get value as double (NaN if not double)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        var notDoubleLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notDoubleLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Br, getDigitsLabel);

        il.MarkLabel(notDoubleLabel);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Stloc, valueLocal);

        // Get digits (default 0)
        il.MarkLabel(getDigitsLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, afterDigitsLabel); // null -> 0

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, digitsFromDoubleLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Int32);
        il.Emit(OpCodes.Brtrue, digitsFromIntLabel);

        il.Emit(OpCodes.Br, afterDigitsLabel); // unknown type -> 0

        il.MarkLabel(digitsFromDoubleLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, digitsLocal);
        il.Emit(OpCodes.Br, validDigitsLabel);

        il.MarkLabel(digitsFromIntLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.Int32);
        il.Emit(OpCodes.Stloc, digitsLocal);
        il.Emit(OpCodes.Br, validDigitsLabel);

        il.MarkLabel(afterDigitsLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, digitsLocal);

        // Validate digits 0-100
        il.MarkLabel(validDigitsLabel);
        il.Emit(OpCodes.Ldloc, digitsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        var notNegativeLabel = il.DefineLabel();
        il.Emit(OpCodes.Bge, notNegativeLabel);
        il.Emit(OpCodes.Ldstr, "Runtime Error: toFixed() digits argument must be between 0 and 100");
        il.Emit(OpCodes.Newobj, _types.Exception.GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(notNegativeLabel);
        il.Emit(OpCodes.Ldloc, digitsLocal);
        il.Emit(OpCodes.Ldc_I4, 100);
        var notTooLargeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ble, notTooLargeLabel);
        il.Emit(OpCodes.Ldstr, "Runtime Error: toFixed() digits argument must be between 0 and 100");
        il.Emit(OpCodes.Newobj, _types.Exception.GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);

        // return value.ToString($"F{digits}", CultureInfo.InvariantCulture)
        il.MarkLabel(notTooLargeLabel);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Ldstr, "F");
        il.Emit(OpCodes.Ldloc, digitsLocal);
        il.Emit(OpCodes.Box, _types.Int32);
        il.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [_types.String, _types.Object])!);
        il.Emit(OpCodes.Call, typeof(CultureInfo).GetProperty("InvariantCulture")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, _types.Double.GetMethod("ToString", [_types.String, typeof(IFormatProvider)])!);
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

        // Get value as double (NaN if not double)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        var notDoubleLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notDoubleLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Br, hasPrecisionLabel);

        il.MarkLabel(notDoubleLabel);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Stloc, valueLocal);

        // Check if precision is null - if so, return value.ToString()
        il.MarkLabel(hasPrecisionLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brtrue, afterPrecisionLabel);

        // precision is null - return value.ToString(CultureInfo.InvariantCulture)
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Call, typeof(CultureInfo).GetProperty("InvariantCulture")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, _types.Double.GetMethod("ToString", [typeof(IFormatProvider)])!);
        il.Emit(OpCodes.Ret);

        // Get precision from arg1
        il.MarkLabel(afterPrecisionLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, precisionFromDoubleLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Int32);
        il.Emit(OpCodes.Brtrue, precisionFromIntLabel);

        il.Emit(OpCodes.Ldc_I4_0); // default
        il.Emit(OpCodes.Stloc, precisionLocal);
        il.Emit(OpCodes.Br, validatePrecisionLabel);

        il.MarkLabel(precisionFromDoubleLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, precisionLocal);
        il.Emit(OpCodes.Br, validatePrecisionLabel);

        il.MarkLabel(precisionFromIntLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.Int32);
        il.Emit(OpCodes.Stloc, precisionLocal);

        // Validate precision 1-100
        il.MarkLabel(validatePrecisionLabel);
        il.Emit(OpCodes.Ldloc, precisionLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        var notTooSmallLabel = il.DefineLabel();
        il.Emit(OpCodes.Bge, notTooSmallLabel);
        il.Emit(OpCodes.Ldstr, "Runtime Error: toPrecision() argument must be between 1 and 100");
        il.Emit(OpCodes.Newobj, _types.Exception.GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(notTooSmallLabel);
        il.Emit(OpCodes.Ldloc, precisionLocal);
        il.Emit(OpCodes.Ldc_I4, 100);
        var notTooLargeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ble, notTooLargeLabel);
        il.Emit(OpCodes.Ldstr, "Runtime Error: toPrecision() argument must be between 1 and 100");
        il.Emit(OpCodes.Newobj, _types.Exception.GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);

        // Handle special values
        il.MarkLabel(notTooLargeLabel);

        // if (double.IsNaN(value)) return "NaN"
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Call, _types.Double.GetMethod("IsNaN", [_types.Double])!);
        il.Emit(OpCodes.Brfalse, notNaNLabel);
        il.Emit(OpCodes.Ldstr, "NaN");
        il.Emit(OpCodes.Ret);

        // if (double.IsPositiveInfinity(value)) return "Infinity"
        il.MarkLabel(notNaNLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Call, _types.Double.GetMethod("IsPositiveInfinity", [_types.Double])!);
        il.Emit(OpCodes.Brfalse, notPosInfLabel);
        il.Emit(OpCodes.Ldstr, "Infinity");
        il.Emit(OpCodes.Ret);

        // if (double.IsNegativeInfinity(value)) return "-Infinity"
        il.MarkLabel(notPosInfLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Call, _types.Double.GetMethod("IsNegativeInfinity", [_types.Double])!);
        il.Emit(OpCodes.Brfalse, formatLabel);
        il.Emit(OpCodes.Ldstr, "-Infinity");
        il.Emit(OpCodes.Ret);

        // return value.ToString($"G{precision}", CultureInfo.InvariantCulture).Replace("E", "e")
        il.MarkLabel(formatLabel);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Ldstr, "G");
        il.Emit(OpCodes.Ldloc, precisionLocal);
        il.Emit(OpCodes.Box, _types.Int32);
        il.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [_types.String, _types.Object])!);
        il.Emit(OpCodes.Call, typeof(CultureInfo).GetProperty("InvariantCulture")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, _types.Double.GetMethod("ToString", [_types.String, typeof(IFormatProvider)])!);
        il.Emit(OpCodes.Ldstr, "E");
        il.Emit(OpCodes.Ldstr, "e");
        il.Emit(OpCodes.Call, _types.String.GetMethod("Replace", [_types.String, _types.String])!);
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
        var digitsLocal = il.DeclareLocal(_types.Int32);
        var notNaNLabel = il.DefineLabel();
        var notPosInfLabel = il.DefineLabel();
        var notNegInfLabel = il.DefineLabel();
        var hasDigitsLabel = il.DefineLabel();
        var digitsFromDoubleLabel = il.DefineLabel();
        var digitsFromIntLabel = il.DefineLabel();
        var validateDigitsLabel = il.DefineLabel();
        var formatWithDigitsLabel = il.DefineLabel();

        // Get value as double (NaN if not double)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        var notDoubleLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notDoubleLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Br, notNaNLabel);

        il.MarkLabel(notDoubleLabel);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Stloc, valueLocal);

        // Handle special values first
        // if (double.IsNaN(value)) return "NaN"
        il.MarkLabel(notNaNLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Call, _types.Double.GetMethod("IsNaN", [_types.Double])!);
        il.Emit(OpCodes.Brfalse, notPosInfLabel);
        il.Emit(OpCodes.Ldstr, "NaN");
        il.Emit(OpCodes.Ret);

        // if (double.IsPositiveInfinity(value)) return "Infinity"
        il.MarkLabel(notPosInfLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Call, _types.Double.GetMethod("IsPositiveInfinity", [_types.Double])!);
        il.Emit(OpCodes.Brfalse, notNegInfLabel);
        il.Emit(OpCodes.Ldstr, "Infinity");
        il.Emit(OpCodes.Ret);

        // if (double.IsNegativeInfinity(value)) return "-Infinity"
        il.MarkLabel(notNegInfLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Call, _types.Double.GetMethod("IsNegativeInfinity", [_types.Double])!);
        il.Emit(OpCodes.Brfalse, hasDigitsLabel);
        il.Emit(OpCodes.Ldstr, "-Infinity");
        il.Emit(OpCodes.Ret);

        // Check if digits is null
        il.MarkLabel(hasDigitsLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brtrue, digitsFromDoubleLabel);

        // digits is null - use default format
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Ldstr, "e");
        il.Emit(OpCodes.Call, typeof(CultureInfo).GetProperty("InvariantCulture")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, _types.Double.GetMethod("ToString", [_types.String, typeof(IFormatProvider)])!);
        il.Emit(OpCodes.Ret);

        // Get digits from arg1
        il.MarkLabel(digitsFromDoubleLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, digitsFromIntLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, digitsLocal);
        il.Emit(OpCodes.Br, validateDigitsLabel);

        il.MarkLabel(digitsFromIntLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Int32);
        var useDefaultDigitsLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, useDefaultDigitsLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.Int32);
        il.Emit(OpCodes.Stloc, digitsLocal);
        il.Emit(OpCodes.Br, validateDigitsLabel);

        il.MarkLabel(useDefaultDigitsLabel);
        il.Emit(OpCodes.Ldc_I4_6); // default 6
        il.Emit(OpCodes.Stloc, digitsLocal);

        // Validate digits 0-100
        il.MarkLabel(validateDigitsLabel);
        il.Emit(OpCodes.Ldloc, digitsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        var notNegativeLabel = il.DefineLabel();
        il.Emit(OpCodes.Bge, notNegativeLabel);
        il.Emit(OpCodes.Ldstr, "Runtime Error: toExponential() argument must be between 0 and 100");
        il.Emit(OpCodes.Newobj, _types.Exception.GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(notNegativeLabel);
        il.Emit(OpCodes.Ldloc, digitsLocal);
        il.Emit(OpCodes.Ldc_I4, 100);
        var notTooLargeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ble, notTooLargeLabel);
        il.Emit(OpCodes.Ldstr, "Runtime Error: toExponential() argument must be between 0 and 100");
        il.Emit(OpCodes.Newobj, _types.Exception.GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);

        // return value.ToString($"e{digits}", CultureInfo.InvariantCulture)
        il.MarkLabel(notTooLargeLabel);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Ldstr, "e");
        il.Emit(OpCodes.Ldloc, digitsLocal);
        il.Emit(OpCodes.Box, _types.Int32);
        il.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [_types.String, _types.Object])!);
        il.Emit(OpCodes.Call, typeof(CultureInfo).GetProperty("InvariantCulture")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, _types.Double.GetMethod("ToString", [_types.String, typeof(IFormatProvider)])!);
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

        // Get value as double (NaN if not double)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        var notDoubleLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notDoubleLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Br, hasRadixLabel);

        il.MarkLabel(notDoubleLabel);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Stloc, valueLocal);

        // Check if radix is null
        il.MarkLabel(hasRadixLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brtrue, radixFromDoubleLabel);

        // radix is null - return value.ToString(CultureInfo.InvariantCulture)
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Call, typeof(CultureInfo).GetProperty("InvariantCulture")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, _types.Double.GetMethod("ToString", [typeof(IFormatProvider)])!);
        il.Emit(OpCodes.Ret);

        // Get radix from arg1
        il.MarkLabel(radixFromDoubleLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, radixFromIntLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, radixLocal);
        il.Emit(OpCodes.Br, validateRadixLabel);

        il.MarkLabel(radixFromIntLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Int32);
        var useDefaultRadixLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, useDefaultRadixLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.Int32);
        il.Emit(OpCodes.Stloc, radixLocal);
        il.Emit(OpCodes.Br, validateRadixLabel);

        il.MarkLabel(useDefaultRadixLabel);
        il.Emit(OpCodes.Ldc_I4, 10);
        il.Emit(OpCodes.Stloc, radixLocal);

        // Validate radix 2-36
        il.MarkLabel(validateRadixLabel);
        il.Emit(OpCodes.Ldloc, radixLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        var radixValidLabel = il.DefineLabel();
        il.Emit(OpCodes.Bge, radixValidLabel);
        il.Emit(OpCodes.Ldstr, "Runtime Error: toString() radix must be between 2 and 36");
        il.Emit(OpCodes.Newobj, _types.Exception.GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(radixValidLabel);
        il.Emit(OpCodes.Ldloc, radixLocal);
        il.Emit(OpCodes.Ldc_I4, 36);
        var radixNotTooLargeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ble, radixNotTooLargeLabel);
        il.Emit(OpCodes.Ldstr, "Runtime Error: toString() radix must be between 2 and 36");
        il.Emit(OpCodes.Newobj, _types.Exception.GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);

        // Handle special values
        il.MarkLabel(radixNotTooLargeLabel);

        // if (double.IsNaN(value)) return "NaN"
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Call, _types.Double.GetMethod("IsNaN", [_types.Double])!);
        il.Emit(OpCodes.Brfalse, notNaNLabel);
        il.Emit(OpCodes.Ldstr, "NaN");
        il.Emit(OpCodes.Ret);

        // if (double.IsPositiveInfinity(value)) return "Infinity"
        il.MarkLabel(notNaNLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Call, _types.Double.GetMethod("IsPositiveInfinity", [_types.Double])!);
        il.Emit(OpCodes.Brfalse, notPosInfLabel);
        il.Emit(OpCodes.Ldstr, "Infinity");
        il.Emit(OpCodes.Ret);

        // if (double.IsNegativeInfinity(value)) return "-Infinity"
        il.MarkLabel(notPosInfLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Call, _types.Double.GetMethod("IsNegativeInfinity", [_types.Double])!);
        il.Emit(OpCodes.Brfalse, convertLabel);
        il.Emit(OpCodes.Ldstr, "-Infinity");
        il.Emit(OpCodes.Ret);

        // if (radix == 10) return value.ToString(InvariantCulture)
        il.MarkLabel(convertLabel);
        il.Emit(OpCodes.Ldloc, radixLocal);
        il.Emit(OpCodes.Ldc_I4, 10);
        var notRadix10Label = il.DefineLabel();
        il.Emit(OpCodes.Bne_Un, notRadix10Label);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Call, typeof(CultureInfo).GetProperty("InvariantCulture")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, _types.Double.GetMethod("ToString", [typeof(IFormatProvider)])!);
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
        il.Emit(OpCodes.Call, _types.Math.GetMethod("Abs", [_types.Double])!);
        il.Emit(OpCodes.Stloc, valueLocal);

        // long intPart = (long)Math.Truncate(value)
        var intPartLocal = il.DeclareLocal(_types.Int64);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Call, _types.Math.GetMethod("Truncate", [_types.Double])!);
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
        il.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [_types.String, _types.String])!);
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
        il.Emit(OpCodes.Newobj, typeof(StringBuilder).GetConstructor(Type.EmptyTypes)!);
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
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("get_Chars", [_types.Int32])!);
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
        il.Emit(OpCodes.Callvirt, typeof(StringBuilder).GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);
    }
}

/// <summary>
/// Static helper methods for Number operations in compiled code.
/// These methods are called by the emitted runtime.
/// </summary>
public static class NumberRuntimeHelpers
{
    private const double MAX_SAFE_INTEGER = 9007199254740991;

    public static double ParseInt(object? strObj, object? radixObj)
    {
        var str = strObj?.ToString() ?? "";
        var radix = radixObj switch
        {
            double d => (int)d,
            int i => i,
            _ => 10
        };

        str = str.Trim();
        if (string.IsNullOrEmpty(str)) return double.NaN;

        int sign = 1;
        int startIndex = 0;
        if (str[0] == '-') { sign = -1; startIndex = 1; }
        else if (str[0] == '+') { startIndex = 1; }

        if (startIndex < str.Length)
        {
            if ((radix == 0 || radix == 16) && str.Length > startIndex + 1 &&
                str[startIndex] == '0' && (str[startIndex + 1] == 'x' || str[startIndex + 1] == 'X'))
            {
                radix = 16;
                startIndex += 2;
            }
            else if (radix == 0)
            {
                radix = 10;
            }
        }

        if (radix < 2 || radix > 36) return double.NaN;

        try
        {
            var numPart = str.Substring(startIndex);
            var validDigits = GetValidDigits(numPart, radix);
            if (string.IsNullOrEmpty(validDigits)) return double.NaN;
            return sign * Convert.ToInt64(validDigits, radix);
        }
        catch
        {
            return double.NaN;
        }
    }

    private static string GetValidDigits(string str, int radix)
    {
        var valid = new StringBuilder();
        foreach (char c in str)
        {
            int digit = GetDigitValue(c);
            if (digit >= 0 && digit < radix)
                valid.Append(c);
            else
                break;
        }
        return valid.ToString();
    }

    private static int GetDigitValue(char c)
    {
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'a' && c <= 'z') return c - 'a' + 10;
        if (c >= 'A' && c <= 'Z') return c - 'A' + 10;
        return -1;
    }

    public static double ParseFloat(object? strObj)
    {
        var str = strObj?.ToString()?.Trim() ?? "";
        if (string.IsNullOrEmpty(str)) return double.NaN;

        var validPart = GetValidFloatPart(str);
        if (string.IsNullOrEmpty(validPart)) return double.NaN;

        if (double.TryParse(validPart, NumberStyles.Float, CultureInfo.InvariantCulture, out double result))
            return result;
        return double.NaN;
    }

    private static string GetValidFloatPart(string str)
    {
        var result = new StringBuilder();
        bool hasDecimal = false;
        bool hasExponent = false;
        bool hasDigit = false;

        for (int i = 0; i < str.Length; i++)
        {
            char c = str[i];

            if (i == 0 && (c == '+' || c == '-'))
            {
                result.Append(c);
                continue;
            }

            if (c >= '0' && c <= '9')
            {
                result.Append(c);
                hasDigit = true;
                continue;
            }

            if (c == '.' && !hasDecimal && !hasExponent)
            {
                result.Append(c);
                hasDecimal = true;
                continue;
            }

            if ((c == 'e' || c == 'E') && hasDigit && !hasExponent)
            {
                result.Append(c);
                hasExponent = true;
                if (i + 1 < str.Length && (str[i + 1] == '+' || str[i + 1] == '-'))
                {
                    result.Append(str[i + 1]);
                    i++;
                }
                continue;
            }

            break;
        }

        return hasDigit ? result.ToString() : "";
    }

    public static bool IsNaN(object? value)
    {
        // Number.isNaN is stricter - only returns true for actual NaN values
        if (value is not double d) return false;
        return double.IsNaN(d);
    }

    public static bool IsFinite(object? value)
    {
        // Number.isFinite is stricter - only returns true for finite numbers
        if (value is not double d) return false;
        return double.IsFinite(d);
    }

    public static bool IsInteger(object? value)
    {
        if (value is not double d) return false;
        return double.IsFinite(d) && Math.Truncate(d) == d;
    }

    public static bool IsSafeInteger(object? value)
    {
        if (value is not double d) return false;
        return double.IsFinite(d) && Math.Truncate(d) == d && Math.Abs(d) <= MAX_SAFE_INTEGER;
    }

    public static bool GlobalIsNaN(object? value)
    {
        // Global isNaN coerces to number first
        if (value is double d) return double.IsNaN(d);
        if (value is string s) return !double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
        if (value is null) return true;
        if (value is bool) return false;
        return true;
    }

    public static bool GlobalIsFinite(object? value)
    {
        // Global isFinite coerces to number first
        if (value is double d) return double.IsFinite(d);
        if (value is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
            return double.IsFinite(parsed);
        if (value is null) return true; // null coerces to 0 which is finite
        if (value is bool) return true; // true=1, false=0, both finite
        return false;
    }

    public static string ToFixed(object? valueObj, object? digitsObj)
    {
        var value = valueObj switch
        {
            double d => d,
            _ => double.NaN
        };

        var digits = digitsObj switch
        {
            double d => (int)d,
            int i => i,
            null => 0,
            _ => 0
        };

        if (digits < 0 || digits > 100)
            throw new Exception("Runtime Error: toFixed() digits argument must be between 0 and 100");

        return value.ToString($"F{digits}", CultureInfo.InvariantCulture);
    }

    public static string ToPrecision(object? valueObj, object? precisionObj)
    {
        var value = valueObj switch
        {
            double d => d,
            _ => double.NaN
        };

        if (precisionObj == null) return value.ToString(CultureInfo.InvariantCulture);

        var precision = precisionObj switch
        {
            double d => (int)d,
            int i => i,
            _ => 0
        };

        if (precision < 1 || precision > 100)
            throw new Exception("Runtime Error: toPrecision() argument must be between 1 and 100");

        if (double.IsNaN(value)) return "NaN";
        if (double.IsPositiveInfinity(value)) return "Infinity";
        if (double.IsNegativeInfinity(value)) return "-Infinity";

        return value.ToString($"G{precision}", CultureInfo.InvariantCulture).Replace("E", "e");
    }

    public static string ToExponential(object? valueObj, object? digitsObj)
    {
        var value = valueObj switch
        {
            double d => d,
            _ => double.NaN
        };

        if (double.IsNaN(value)) return "NaN";
        if (double.IsPositiveInfinity(value)) return "Infinity";
        if (double.IsNegativeInfinity(value)) return "-Infinity";

        if (digitsObj == null)
        {
            return value.ToString("e", CultureInfo.InvariantCulture);
        }

        var fractionDigits = digitsObj switch
        {
            double d => (int)d,
            int i => i,
            _ => 6
        };

        if (fractionDigits < 0 || fractionDigits > 100)
            throw new Exception("Runtime Error: toExponential() argument must be between 0 and 100");

        return value.ToString($"e{fractionDigits}", CultureInfo.InvariantCulture);
    }

    public static string ToStringRadix(object? valueObj, object? radixObj)
    {
        var value = valueObj switch
        {
            double d => d,
            _ => double.NaN
        };

        if (radixObj == null) return value.ToString(CultureInfo.InvariantCulture);

        var radix = radixObj switch
        {
            double d => (int)d,
            int i => i,
            _ => 10
        };

        if (radix < 2 || radix > 36)
            throw new Exception("Runtime Error: toString() radix must be between 2 and 36");

        if (double.IsNaN(value)) return "NaN";
        if (double.IsPositiveInfinity(value)) return "Infinity";
        if (double.IsNegativeInfinity(value)) return "-Infinity";

        if (radix == 10) return value.ToString(CultureInfo.InvariantCulture);

        if (value == 0) return "0";

        bool negative = value < 0;
        value = Math.Abs(value);

        long intPart = (long)Math.Truncate(value);
        string intStr = intPart == 0 ? "0" : ConvertIntToRadix(intPart, radix);

        string result = intStr;
        return negative ? "-" + result : result;
    }

    private static string ConvertIntToRadix(long value, int radix)
    {
        if (value == 0) return "0";

        const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
        var result = new StringBuilder();

        while (value > 0)
        {
            result.Insert(0, digits[(int)(value % radix)]);
            value /= radix;
        }

        return result.ToString();
    }
}


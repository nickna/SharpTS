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
    private static void EmitNumberMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
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

    private static void EmitNumberParseInt(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "NumberParseInt",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(double),
            [typeof(object), typeof(object)]
        );
        runtime.NumberParseInt = method;

        var il = method.GetILGenerator();

        // Call helper method that does the actual parsing
        il.Emit(OpCodes.Ldarg_0); // str
        il.Emit(OpCodes.Ldarg_1); // radix
        il.Emit(OpCodes.Call, typeof(NumberRuntimeHelpers).GetMethod("ParseInt", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitNumberParseFloat(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "NumberParseFloat",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(double),
            [typeof(object)]
        );
        runtime.NumberParseFloat = method;

        var il = method.GetILGenerator();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(NumberRuntimeHelpers).GetMethod("ParseFloat", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitNumberIsNaN(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "NumberIsNaN",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(bool),
            [typeof(object)]
        );
        runtime.NumberIsNaN = method;

        var il = method.GetILGenerator();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(NumberRuntimeHelpers).GetMethod("IsNaN", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitNumberIsFinite(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "NumberIsFinite",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(bool),
            [typeof(object)]
        );
        runtime.NumberIsFinite = method;

        var il = method.GetILGenerator();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(NumberRuntimeHelpers).GetMethod("IsFinite", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitNumberIsInteger(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "NumberIsInteger",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(bool),
            [typeof(object)]
        );
        runtime.NumberIsInteger = method;

        var il = method.GetILGenerator();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(NumberRuntimeHelpers).GetMethod("IsInteger", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitNumberIsSafeInteger(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "NumberIsSafeInteger",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(bool),
            [typeof(object)]
        );
        runtime.NumberIsSafeInteger = method;

        var il = method.GetILGenerator();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(NumberRuntimeHelpers).GetMethod("IsSafeInteger", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitGlobalIsNaN(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GlobalIsNaN",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(bool),
            [typeof(object)]
        );
        runtime.GlobalIsNaN = method;

        var il = method.GetILGenerator();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(NumberRuntimeHelpers).GetMethod("GlobalIsNaN", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitGlobalIsFinite(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GlobalIsFinite",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(bool),
            [typeof(object)]
        );
        runtime.GlobalIsFinite = method;

        var il = method.GetILGenerator();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(NumberRuntimeHelpers).GetMethod("GlobalIsFinite", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitNumberToFixed(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "NumberToFixed",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(string),
            [typeof(object), typeof(object)]
        );
        runtime.NumberToFixed = method;

        var il = method.GetILGenerator();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(NumberRuntimeHelpers).GetMethod("ToFixed", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitNumberToPrecision(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "NumberToPrecision",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(string),
            [typeof(object), typeof(object)]
        );
        runtime.NumberToPrecision = method;

        var il = method.GetILGenerator();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(NumberRuntimeHelpers).GetMethod("ToPrecision", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitNumberToExponential(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "NumberToExponential",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(string),
            [typeof(object), typeof(object)]
        );
        runtime.NumberToExponential = method;

        var il = method.GetILGenerator();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(NumberRuntimeHelpers).GetMethod("ToExponential", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitNumberToStringRadix(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "NumberToStringRadix",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(string),
            [typeof(object), typeof(object)]
        );
        runtime.NumberToStringRadix = method;

        var il = method.GetILGenerator();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(NumberRuntimeHelpers).GetMethod("ToStringRadix", BindingFlags.Public | BindingFlags.Static)!);
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

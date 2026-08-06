using System.Globalization;
using System.Text;
using SharpTS.Execution;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Provides implementations for JavaScript Number object members.
/// Includes static methods (Number.parseInt), static properties (Number.MAX_VALUE),
/// and instance methods ((123).toFixed(2)).
/// </summary>
public static class NumberBuiltIns
{
    // JavaScript Number constants
    public const double MAX_VALUE = double.MaxValue;
    public const double MIN_VALUE = double.Epsilon;  // JS MIN_VALUE = smallest positive number
    public const double POSITIVE_INFINITY = double.PositiveInfinity;
    public const double NEGATIVE_INFINITY = double.NegativeInfinity;
    public const double MAX_SAFE_INTEGER = 9007199254740991;  // 2^53 - 1
    public const double MIN_SAFE_INTEGER = -9007199254740991; // -(2^53 - 1)
    public const double EPSILON = 2.220446049250313e-16;      // 2^-52

    // Static member lookup for Number namespace
    // Number constants use CallableConstant because they're accessed via GetStaticMethod
    // which expects ISharpTSCallable (registry casts result to BuiltInMethod)
    private static readonly BuiltInStaticMemberLookup _staticLookup =
        BuiltInStaticBuilder.Create()
            // Static properties (constants) - wrapped as callable for registry compatibility
            .CallableConstant("MAX_VALUE", MAX_VALUE)
            .CallableConstant("MIN_VALUE", MIN_VALUE)
            .CallableConstant("NaN", double.NaN)
            .CallableConstant("POSITIVE_INFINITY", POSITIVE_INFINITY)
            .CallableConstant("NEGATIVE_INFINITY", NEGATIVE_INFINITY)
            .CallableConstant("MAX_SAFE_INTEGER", MAX_SAFE_INTEGER)
            .CallableConstant("MIN_SAFE_INTEGER", MIN_SAFE_INTEGER)
            .CallableConstant("EPSILON", EPSILON)
            // Static methods (V2 — no boxing)
            .MethodV2("parseInt", 1, 2, ParseIntV2)
            .MethodV2("parseFloat", 1, ParseFloatV2)
            .MethodV2("isNaN", 0, int.MaxValue, specLength: 1, (_, _, args) =>
                RuntimeValue.FromBoolean(!args.IsEmpty
                    && args[0].Kind == ValueKind.Number
                    && double.IsNaN(Interpreter.ToNumber(args[0]))))
            .MethodV2("isFinite", 0, int.MaxValue, specLength: 1, (_, _, args) =>
                RuntimeValue.FromBoolean(!args.IsEmpty
                    && args[0].Kind == ValueKind.Number
                    && double.IsFinite(Interpreter.ToNumber(args[0]))))
            .MethodV2("isInteger", 0, int.MaxValue, specLength: 1, (_, _, args) =>
            {
                if (args.IsEmpty || args[0].Kind != ValueKind.Number) return RuntimeValue.False;
                double d = Interpreter.ToNumber(args[0]);
                return RuntimeValue.FromBoolean(double.IsFinite(d) && Math.Truncate(d) == d);
            })
            .MethodV2("isSafeInteger", 0, int.MaxValue, specLength: 1, (_, _, args) =>
            {
                if (args.IsEmpty || args[0].Kind != ValueKind.Number) return RuntimeValue.False;
                double d = Interpreter.ToNumber(args[0]);
                return RuntimeValue.FromBoolean(double.IsFinite(d) && Math.Truncate(d) == d && Math.Abs(d) <= MAX_SAFE_INTEGER);
            })
            .Build();

    // Instance member lookup for number values
    private static readonly BuiltInTypeMemberLookup<double> _instanceLookup =
        BuiltInTypeBuilder<double>.ForInstanceType()
            // min-arity 0 (the argument is optional) but ECMA-262 §21.1.3 gives each of
            // these a spec `length` of 1, so pass it explicitly rather than letting it
            // default to the min arity.
            .MethodV2("toFixed", 0, 1, 1, ToFixedV2)
            .MethodV2("toPrecision", 0, 1, 1, ToPrecisionV2)
            .MethodV2("toExponential", 0, 1, 1, ToExponentialV2)
            .MethodV2("toString", 0, 1, 1, ToStringMethodV2)
            .MethodV2("toLocaleString", 0, 1, 0, ToStringMethodV2)
            // ECMA-262 §21.1.3.7: Number.prototype.valueOf returns thisNumberValue.
            // Needed so `(new Number(5)).valueOf()` and ToPrimitive(number-wrapper)
            // unwrap to the primitive instead of resolving Object.prototype.valueOf.
            .MethodV2("valueOf", 0, (Interpreter _, double value, ReadOnlySpan<RuntimeValue> _)
                => RuntimeValue.FromNumber(value))
            .Build();

    /// <summary>
    /// Gets a static member (property or method) from the Number namespace.
    /// </summary>
    public static object? GetStaticMember(string name)
        => _staticLookup.GetMember(name);

    /// <summary>Static member names for REPL autocomplete.</summary>
    public static IEnumerable<string> StaticMemberNames => _staticLookup.MemberNames;

    /// <summary>
    /// Gets an instance member for a number value (e.g., (123).toFixed(2)).
    /// </summary>
    public static object? GetInstanceMember(double receiver, string name)
        => _instanceLookup.GetMember(receiver, name);

    /// <summary>
    /// Returns the unbound <see cref="BuiltInMethod"/> for a
    /// Number.prototype.* method. Used by <see cref="Types.SharpTSNumberPrototype"/>
    /// so <c>Number.prototype.toString.call(value)</c> resolves to the same
    /// implementation as <c>(123).toString()</c>.
    /// </summary>
    public static BuiltInMethod? GetPrototypeMethod(string name)
        => _instanceLookup.GetMethod(name);

    // Static method implementations (V2)
    private static RuntimeValue ParseIntV2(Interpreter _, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var str = args[0].Kind == ValueKind.String
            ? args[0].AsString()
            : args[0].ToObject()?.ToString() ?? "";
        var radix = args.Length > 1 && args[1].Kind == ValueKind.Number
            ? (int)Interpreter.ToNumber(args[1])
            : 10;
        return RuntimeValue.FromNumber(ParseInt(str, radix));
    }

    private static RuntimeValue ParseFloatV2(Interpreter _, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var str = args[0].Kind == ValueKind.String
            ? args[0].AsString()
            : args[0].ToObject()?.ToString() ?? "";
        return RuntimeValue.FromNumber(ParseFloat(str));
    }

    // Instance method implementations (V2 — no boxing)
    private static RuntimeValue ToFixedV2(Interpreter _, double value, ReadOnlySpan<RuntimeValue> args)
    {
        var digits = args.Length > 0 ? (int)Interpreter.ToNumber(args[0]) : 0;
        if (digits < 0 || digits > 100)
            throw new Exception("Runtime Error: toFixed() digits argument must be between 0 and 100");
        return RuntimeValue.FromString(value.ToString($"F{digits}", CultureInfo.InvariantCulture));
    }

    private static RuntimeValue ToPrecisionV2(Interpreter _, double value, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0)
            return RuntimeValue.FromString(value.ToString(CultureInfo.InvariantCulture));
        var precision = (int)Interpreter.ToNumber(args[0]);
        if (precision < 1 || precision > 100)
            throw new Exception("Runtime Error: toPrecision() argument must be between 1 and 100");
        return RuntimeValue.FromString(ToPrecisionImpl(value, precision));
    }

    private static RuntimeValue ToExponentialV2(
        Interpreter interpreter, double value, ReadOnlySpan<RuntimeValue> args)
    {
        // ECMA-262 §21.1.3.2 coerces fractionDigits before the NaN/Infinity
        // short-circuits. That ordering is observable when an object conversion throws.
        bool useShortestForm = args.IsEmpty || args[0].IsUndefined;
        double fractionDigits = 0;
        if (!useShortestForm)
        {
            fractionDigits = interpreter.ToNumberWithPrimitive(args[0].ToObject());
            fractionDigits = double.IsNaN(fractionDigits)
                ? 0
                : Math.Truncate(fractionDigits);
        }

        if (double.IsNaN(value)) return RuntimeValue.FromString("NaN");
        if (double.IsPositiveInfinity(value)) return RuntimeValue.FromString("Infinity");
        if (double.IsNegativeInfinity(value)) return RuntimeValue.FromString("-Infinity");

        if (!useShortestForm && (fractionDigits < 0 || fractionDigits > 100))
            throw new ThrowException(new SharpTSRangeError(
                "toExponential() argument must be between 0 and 100"));

        return RuntimeValue.FromString(useShortestForm
            ? FormatShortestExponential(value)
            : FormatExponential(value, (int)fractionDigits));
    }

    private static string FormatShortestExponential(double value)
    {
        if (value == 0) return "0e+0";

        // Fifteen digits cover the shortest forms exercised by the compiled runtime,
        // then trailing fractional zeroes are removed without disturbing the exponent.
        string result = value.ToString("e15", CultureInfo.InvariantCulture);
        result = System.Text.RegularExpressions.Regex.Replace(
            result, @"(\.\d*?)0+(?=e)", "$1");
        result = result.Replace(".e", "e", StringComparison.Ordinal);
        return NormalizeExponent(result);
    }

    private static string FormatExponential(double value, int fractionDigits)
    {
        // ECMA-262 treats -0 as non-negative.
        if (value == 0) value = 0;

        // Double's standard exponential formatter uses ties-to-even. JavaScript chooses
        // the larger decimal on a tie, so perform the mantissa rounding explicitly while
        // Math.Round can represent the requested decimal precision.
        if (fractionDigits <= 15)
        {
            double absolute = Math.Abs(value);
            int exponent = absolute == 0
                ? 0
                : (int)Math.Floor(Math.Log10(absolute));
            double rounded = absolute == 0
                ? 0
                : Math.Round(
                    absolute / Math.Pow(10, exponent),
                    fractionDigits,
                    MidpointRounding.AwayFromZero);

            if (rounded >= 10)
            {
                rounded /= 10;
                exponent++;
            }
            if (value < 0) rounded = -rounded;

            return rounded.ToString($"F{fractionDigits}", CultureInfo.InvariantCulture)
                + (exponent < 0 ? "e-" : "e+")
                + Math.Abs(exponent).ToString(CultureInfo.InvariantCulture);
        }

        return NormalizeExponent(
            value.ToString($"e{fractionDigits}", CultureInfo.InvariantCulture));
    }

    private static string NormalizeExponent(string value)
        => System.Text.RegularExpressions.Regex.Replace(
            value, @"e([+-])0+(?=\d)", "e$1");

    private static RuntimeValue ToStringMethodV2(Interpreter _, double value, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0)
            return RuntimeValue.FromString(Compilation.RuntimeTypes.FormatNumber(value));
        var radix = (int)Interpreter.ToNumber(args[0]);
        if (radix < 2 || radix > 36)
            throw new Exception("Runtime Error: toString() radix must be between 2 and 36");
        return RuntimeValue.FromString(ToStringWithRadix(value, radix));
    }

    /// <summary>
    /// Parses a string as an integer with the specified radix.
    /// Implements JavaScript parseInt semantics.
    /// </summary>
    public static double ParseInt(string str, int radix)
    {
        str = str.Trim();
        if (string.IsNullOrEmpty(str)) return double.NaN;

        // Handle sign
        int sign = 1;
        int startIndex = 0;
        if (str[0] == '-') { sign = -1; startIndex = 1; }
        else if (str[0] == '+') { startIndex = 1; }

        // Auto-detect radix from prefix if radix is 0 or 16
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

    /// <summary>
    /// Parses a string as a floating-point number.
    /// Implements JavaScript parseFloat semantics.
    /// </summary>
    public static double ParseFloat(string str)
    {
        str = str.Trim();
        if (string.IsNullOrEmpty(str)) return double.NaN;

        // JavaScript parseFloat parses as much as it can from the start
        var validPart = GetValidFloatPart(str);
        if (string.IsNullOrEmpty(validPart)) return double.NaN;

        if (double.TryParse(validPart, NumberStyles.Float, CultureInfo.InvariantCulture, out double result))
            return result;
        return double.NaN;
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

    private static string GetValidFloatPart(string str)
    {
        var result = new StringBuilder();
        bool hasDecimal = false;
        bool hasExponent = false;
        bool hasDigit = false;

        for (int i = 0; i < str.Length; i++)
        {
            char c = str[i];

            // Handle sign at start
            if (i == 0 && (c == '+' || c == '-'))
            {
                result.Append(c);
                continue;
            }

            // Handle digits
            if (c >= '0' && c <= '9')
            {
                result.Append(c);
                hasDigit = true;
                continue;
            }

            // Handle decimal point
            if (c == '.' && !hasDecimal && !hasExponent)
            {
                result.Append(c);
                hasDecimal = true;
                continue;
            }

            // Handle exponent
            if ((c == 'e' || c == 'E') && hasDigit && !hasExponent)
            {
                result.Append(c);
                hasExponent = true;
                // Check for sign after exponent
                if (i + 1 < str.Length && (str[i + 1] == '+' || str[i + 1] == '-'))
                {
                    result.Append(str[i + 1]);
                    i++;
                }
                continue;
            }

            // Invalid character - stop parsing
            break;
        }

        return hasDigit ? result.ToString() : "";
    }

    private static string ToStringWithRadix(double value, int radix)
    {
        if (double.IsNaN(value)) return "NaN";
        if (double.IsPositiveInfinity(value)) return "Infinity";
        if (double.IsNegativeInfinity(value)) return "-Infinity";

        if (radix == 10) return Compilation.RuntimeTypes.FormatNumber(value);

        // For non-base-10, handle integer conversion
        if (value == 0) return "0";

        bool negative = value < 0;
        value = Math.Abs(value);

        // Get integer part
        long intPart = (long)Math.Truncate(value);
        double fracPart = value - intPart;

        // Convert integer part
        string intStr = intPart == 0 ? "0" : ConvertIntToRadix(intPart, radix);

        // Convert fractional part if present
        string fracStr = "";
        if (fracPart > 0)
        {
            fracStr = ConvertFracToRadix(fracPart, radix);
        }

        string result = string.IsNullOrEmpty(fracStr) ? intStr : intStr + "." + fracStr;
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

    private static string ConvertFracToRadix(double frac, int radix)
    {
        const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
        var result = new StringBuilder();
        int maxDigits = 16; // Limit precision

        while (frac > 0 && result.Length < maxDigits)
        {
            frac *= radix;
            int digit = (int)frac;
            result.Append(digits[digit]);
            frac -= digit;
        }

        return result.ToString();
    }

    private static string ToPrecisionImpl(double value, int precision)
    {
        if (double.IsNaN(value)) return "NaN";
        if (double.IsPositiveInfinity(value)) return "Infinity";
        if (double.IsNegativeInfinity(value)) return "-Infinity";

        // Use G format for general number format with specified precision
        string result = value.ToString($"G{precision}", CultureInfo.InvariantCulture);

        // JavaScript uses lowercase 'e' for exponential notation
        return result.Replace("E", "e");
    }
}

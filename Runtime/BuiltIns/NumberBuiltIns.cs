using System.Globalization;
using System.Text;
using SharpTS.Execution;

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
            // Static methods
            .Method("parseInt", 1, 2, ParseIntMethod)
            .Method("parseFloat", 1, ParseFloatMethod)
            .Method("isNaN", 1, IsNaNMethod)
            .Method("isFinite", 1, IsFiniteMethod)
            .Method("isInteger", 1, IsIntegerMethod)
            .Method("isSafeInteger", 1, IsSafeIntegerMethod)
            .Build();

    // Instance member lookup for number values
    private static readonly BuiltInTypeMemberLookup<double> _instanceLookup =
        BuiltInTypeBuilder<double>.ForInstanceType()
            .Method("toFixed", 0, 1, ToFixed)
            .Method("toPrecision", 0, 1, ToPrecision)
            .Method("toExponential", 0, 1, ToExponential)
            .Method("toString", 0, 1, ToStringMethod)
            .Build();

    /// <summary>
    /// Gets a static member (property or method) from the Number namespace.
    /// </summary>
    public static object? GetStaticMember(string name)
        => _staticLookup.GetMember(name);

    /// <summary>
    /// Gets an instance member for a number value (e.g., (123).toFixed(2)).
    /// </summary>
    public static object? GetInstanceMember(double receiver, string name)
        => _instanceLookup.GetMember(receiver, name);

    // Static method implementations
    private static object? ParseIntMethod(Interpreter _, List<object?> args)
    {
        var str = args[0]?.ToString() ?? "";
        var radix = args.Count > 1 && args[1] != null ? (int)(double)args[1]! : 10;
        return ParseInt(str, radix);
    }

    private static object? ParseFloatMethod(Interpreter _, List<object?> args)
    {
        var str = args[0]?.ToString() ?? "";
        return ParseFloat(str);
    }

    private static object? IsNaNMethod(Interpreter _, List<object?> args)
    {
        // Number.isNaN only returns true for actual NaN values (stricter than global isNaN)
        if (args[0] is not double d) return false;
        return double.IsNaN(d);
    }

    private static object? IsFiniteMethod(Interpreter _, List<object?> args)
    {
        // Number.isFinite only returns true for finite numbers (stricter than global isFinite)
        if (args[0] is not double d) return false;
        return double.IsFinite(d);
    }

    private static object? IsIntegerMethod(Interpreter _, List<object?> args)
    {
        if (args[0] is not double d) return false;
        return double.IsFinite(d) && Math.Truncate(d) == d;
    }

    private static object? IsSafeIntegerMethod(Interpreter _, List<object?> args)
    {
        if (args[0] is not double d) return false;
        return double.IsFinite(d) &&
               Math.Truncate(d) == d &&
               Math.Abs(d) <= MAX_SAFE_INTEGER;
    }

    // Instance method implementations
    private static object? ToFixed(Interpreter _, double value, List<object?> args)
    {
        var digits = args.Count > 0 && args[0] != null ? (int)(double)args[0]! : 0;
        if (digits < 0 || digits > 100)
            throw new Exception("Runtime Error: toFixed() digits argument must be between 0 and 100");
        return value.ToString($"F{digits}", CultureInfo.InvariantCulture);
    }

    private static object? ToPrecision(Interpreter _, double value, List<object?> args)
    {
        if (args.Count == 0 || args[0] == null) return value.ToString(CultureInfo.InvariantCulture);
        var precision = (int)(double)args[0]!;
        if (precision < 1 || precision > 100)
            throw new Exception("Runtime Error: toPrecision() argument must be between 1 and 100");
        return ToPrecisionImpl(value, precision);
    }

    private static object? ToExponential(Interpreter _, double value, List<object?> args)
    {
        if (double.IsNaN(value)) return "NaN";
        if (double.IsPositiveInfinity(value)) return "Infinity";
        if (double.IsNegativeInfinity(value)) return "-Infinity";

        var fractionDigits = args.Count > 0 && args[0] != null ? (int)(double)args[0]! : -1;
        if (fractionDigits != -1 && (fractionDigits < 0 || fractionDigits > 100))
            throw new Exception("Runtime Error: toExponential() argument must be between 0 and 100");

        if (fractionDigits == -1)
        {
            // No argument: use default precision
            return value.ToString("e", CultureInfo.InvariantCulture);
        }
        return value.ToString($"e{fractionDigits}", CultureInfo.InvariantCulture);
    }

    private static object? ToStringMethod(Interpreter _, double value, List<object?> args)
    {
        if (args.Count == 0 || args[0] == null) return value.ToString(CultureInfo.InvariantCulture);
        var radix = (int)(double)args[0]!;
        if (radix < 2 || radix > 36)
            throw new Exception("Runtime Error: toString() radix must be between 2 and 36");
        return ToStringWithRadix(value, radix);
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

        if (radix == 10) return value.ToString(CultureInfo.InvariantCulture);

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

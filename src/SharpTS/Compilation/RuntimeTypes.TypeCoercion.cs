using System.Globalization;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

public static partial class RuntimeTypes
{
    #region Type Coercion

    public static string Stringify(object? value)
    {
        if (value == null) return "null";
        if (IsUndefined(value)) return "undefined";
        return value switch
        {
            bool b => b ? "true" : "false",
            double d => FormatNumber(d),
            System.Numerics.BigInteger bi => $"{bi}n",
            string s => s,
            object[] arr => "[" + string.Join(", ", arr.Select(Stringify)) + "]",
            List<object?> list => "[" + string.Join(", ", list.Select(Stringify)) + "]",
            System.Collections.IList list => "[" + string.Join(", ", list.Cast<object?>().Select(Stringify)) + "]",
            Dictionary<string, object?> dict => StringifyObject(dict),
            _ => value.ToString() ?? "null"
        };
    }

    /// <summary>
    /// ECMA-262 7.1.12.1 Number::toString(10) — the JS-correct, culture-invariant
    /// number-to-string. Single source of truth shared by the interpreter (this) and
    /// the compiled standalone runtime ($Runtime.FormatNumber emitted IL, which must
    /// stay byte-identical — see RuntimeEmitter.NumberFormat.cs).
    /// </summary>
    internal static string FormatNumber(double d)
    {
        if (double.IsNaN(d)) return "NaN";
        if (double.IsPositiveInfinity(d)) return "Infinity";
        if (double.IsNegativeInfinity(d)) return "-Infinity";

        // Fast path: integers exactly representable as Int64 (|d| < 2^53) render as
        // plain digits (also handles 0 and -0 -> "0"). At/above 2^53 doubles lose
        // integer precision and the spec uses the shortest round-trip (e.g.
        // 12345678901234567000, not the exact long), so those fall through below.
        if (d == Math.Floor(d) && Math.Abs(d) < 9007199254740992.0)
            return ((long)d).ToString(CultureInfo.InvariantCulture);

        // Take the shortest round-trippable decimal (.NET "R" matches V8's shortest)
        // and reposition the decimal point per the spec's plain-vs-exponential
        // thresholds (decimal exponent in [-6, 21) renders plain, else exponential).
        string sign = d < 0 ? "-" : "";
        string r = Math.Abs(d).ToString("R", CultureInfo.InvariantCulture);

        // Decompose into the significant digit string and n = the number of digits to
        // the left of the decimal point (ECMA's n; value = digits x 10^(n - k)).
        string digits;
        int n;
        int eIdx = r.IndexOf('E');
        if (eIdx >= 0)
        {
            string mant = r.Substring(0, eIdx);
            int exp = int.Parse(r.Substring(eIdx + 1), CultureInfo.InvariantCulture);
            int dot = mant.IndexOf('.');
            if (dot < 0) { digits = mant; n = mant.Length + exp; }
            else { digits = mant.Remove(dot, 1); n = dot + exp; }
        }
        else
        {
            int dot = r.IndexOf('.');
            if (dot < 0) { digits = r; n = r.Length; }
            else { digits = r.Remove(dot, 1); n = dot; }
        }

        // Drop leading zeros (each shifts the point left) and trailing zeros.
        int lead = 0;
        while (lead < digits.Length - 1 && digits[lead] == '0') { lead++; n--; }
        digits = digits.Substring(lead).TrimEnd('0');
        if (digits.Length == 0) digits = "0";
        int k = digits.Length;

        if (k <= n && n <= 21)
            return sign + digits + new string('0', n - k);
        if (0 < n && n <= 21)
            return sign + digits.Substring(0, n) + "." + digits.Substring(n);
        if (-6 < n && n <= 0)
            return sign + "0." + new string('0', -n) + digits;
        // Exponential: d.dddde±X (lowercase e, sign, no leading zeros in the exponent).
        string mantOut = k == 1 ? digits : digits.Substring(0, 1) + "." + digits.Substring(1);
        int e = n - 1;
        return sign + mantOut + "e" + (e >= 0 ? "+" : "-") + Math.Abs(e).ToString(CultureInfo.InvariantCulture);
    }

    private static string StringifyObject(Dictionary<string, object?> dict)
    {
        var props = dict.Select(kv => $"{kv.Key}: {Stringify(kv.Value)}");
        return "{ " + string.Join(", ", props) + " }";
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double ToNumber(object? value)
    {
        if (value == null) return 0.0;
        if (IsUndefined(value)) return double.NaN;  // undefined coerces to NaN, not 0
        return value switch
        {
            double d => d,
            int i => i,
            long l => l,
            bool b => b ? 1.0 : 0.0,
            string s when double.TryParse(s, out var d) => d,
            _ => double.NaN
        };
    }

    /// <summary>
    /// Matches JS Number(value) semantics — empty/whitespace strings return 0 (not NaN).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double ConvertToNumber(object? value)
    {
        if (value == null) return 0.0;
        if (IsUndefined(value)) return double.NaN;
        return value switch
        {
            double d => d,
            int i => i,
            long l => l,
            bool b => b ? 1.0 : 0.0,
            string s => ConvertStringToNumber(s),
            _ => double.NaN
        };
    }

    private static double ConvertStringToNumber(string s)
    {
        s = s.Trim();
        if (s.Length == 0) return 0.0;
        // ECMA-262 7.1.4: only the case-sensitive "Infinity"/"+Infinity"/
        // "-Infinity" forms are valid Infinity literals. Double.TryParse
        // would otherwise accept "infinity"/"INFINITY" via NumberStyles.Float.
        if (s == "Infinity" || s == "+Infinity") return double.PositiveInfinity;
        if (s == "-Infinity") return double.NegativeInfinity;
        if (s.Contains("infinity", StringComparison.OrdinalIgnoreCase))
            return double.NaN;
        if (double.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double result))
            return result;
        return double.NaN;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsTruthy(object? value)
    {
        if (value == null || IsUndefined(value)) return false;
        return value switch
        {
            bool b => b,
            double d => d != 0.0 && !double.IsNaN(d),
            string s => s.Length > 0,
            System.Numerics.BigInteger bi => bi != 0,
            // The interpreter wraps bigints in SharpTSBigInt; ToBoolean(0n) is false.
            Runtime.Types.SharpTSBigInt sbi => sbi.Value != 0,
            _ => true
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string TypeOf(object? value)
    {
        if (value == null) return "object"; // typeof null === "object" in JS
        if (IsUndefined(value)) return "undefined";

        // Check for union types using marker interface
        if (value is IUnionType union)
            return TypeOf(union.Value);

        return value switch
        {
            bool => "boolean",
            double or int or long => "number",
            System.Numerics.BigInteger => "bigint",
            string => "string",
            TSFunction => "function",
            Delegate => "function",
            _ => "object"
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool InstanceOf(object? instance, object? classType)
    {
        if (instance == null || classType == null) return false;
        // For compiled code, we need to check if the instance's type matches or inherits from the class type
        var instanceType = instance.GetType();
        var targetType = classType as Type ?? classType.GetType();
        return targetType.IsAssignableFrom(instanceType);
    }

    /// <summary>
    /// Converts arguments to union types using implicit conversion operators if needed.
    /// Used by TSFunction.Invoke for reflection-based invocation.
    /// </summary>
    public static void ConvertArgsForUnionTypes(object?[] args, System.Reflection.ParameterInfo[] parameters)
        => UnionTypeHelper.ConvertArgsForUnionTypes(parameters, args);

    #endregion
}

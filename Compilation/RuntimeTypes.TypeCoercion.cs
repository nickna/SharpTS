using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

public static partial class RuntimeTypes
{
    #region Type Coercion

    public static string Stringify(object? value) => value switch
    {
        null => "null",
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

    private static string FormatNumber(double d)
    {
        if (double.IsNaN(d)) return "NaN";
        if (double.IsPositiveInfinity(d)) return "Infinity";
        if (double.IsNegativeInfinity(d)) return "-Infinity";
        if (d == Math.Floor(d) && Math.Abs(d) < 1e15)
            return ((long)d).ToString();
        return d.ToString("G15");
    }

    private static string StringifyObject(Dictionary<string, object?> dict)
    {
        var props = dict.Select(kv => $"{kv.Key}: {Stringify(kv.Value)}");
        return "{ " + string.Join(", ", props) + " }";
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double ToNumber(object? value) => value switch
    {
        double d => d,
        int i => i,
        long l => l,
        bool b => b ? 1.0 : 0.0,
        string s when double.TryParse(s, out var d) => d,
        null => 0.0,
        _ => double.NaN
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsTruthy(object? value) => value switch
    {
        null => false,
        bool b => b,
        double d => d != 0.0 && !double.IsNaN(d),
        string s => s.Length > 0,
        _ => true
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string TypeOf(object? value) => value switch
    {
        null => "object", // typeof null === "object" in JS
        bool => "boolean",
        double or int or long => "number",
        System.Numerics.BigInteger => "bigint",
        string => "string",
        TSFunction => "function",
        Delegate => "function",
        _ => "object"
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool InstanceOf(object? instance, object? classType)
    {
        if (instance == null || classType == null) return false;
        // For compiled code, we need to check if the instance's type matches or inherits from the class type
        var instanceType = instance.GetType();
        var targetType = classType as Type ?? classType.GetType();
        return targetType.IsAssignableFrom(instanceType);
    }

    #endregion
}

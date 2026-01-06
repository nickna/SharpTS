using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Provides implementations for JavaScript RegExp object members.
/// Includes instance properties (source, flags, global, etc.) and methods (test, exec, toString).
/// </summary>
public static class RegExpBuiltIns
{
    /// <summary>
    /// Gets an instance member (property or method) for a RegExp object.
    /// </summary>
    public static object? GetMember(SharpTSRegExp receiver, string name)
    {
        return name switch
        {
            // ========== Properties ==========
            "source" => receiver.Source,
            "flags" => receiver.Flags,
            "global" => receiver.Global,
            "ignoreCase" => receiver.IgnoreCase,
            "multiline" => receiver.Multiline,
            "lastIndex" => (double)receiver.LastIndex,

            // ========== Methods ==========
            "test" => new BuiltInMethod("test", 1, (_, recv, args) =>
            {
                var regex = (SharpTSRegExp)recv!;
                var str = args[0]?.ToString() ?? "";
                return regex.Test(str);
            }),

            "exec" => new BuiltInMethod("exec", 1, (_, recv, args) =>
            {
                var regex = (SharpTSRegExp)recv!;
                var str = args[0]?.ToString() ?? "";
                return regex.Exec(str);
            }),

            "toString" => new BuiltInMethod("toString", 0, (_, recv, _) =>
                ((SharpTSRegExp)recv!).ToString()),

            _ => null
        };
    }

    /// <summary>
    /// Sets an instance member (property) for a RegExp object.
    /// Only lastIndex is writable.
    /// </summary>
    /// <returns>True if the property was set, false if the property is not writable.</returns>
    public static bool SetMember(SharpTSRegExp receiver, string name, object? value)
    {
        if (name == "lastIndex")
        {
            receiver.LastIndex = (int)(double)value!;
            return true;
        }
        return false;
    }
}

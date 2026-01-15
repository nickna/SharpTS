using SharpTS.Runtime.Types;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

/// <summary>
/// Interpreter-mode implementation of the Node.js 'querystring' module.
/// </summary>
/// <remarks>
/// Provides runtime values for query string parsing and formatting.
/// </remarks>
public static class QuerystringModuleInterpreter
{
    /// <summary>
    /// Gets all exported values for the querystring module.
    /// </summary>
    public static Dictionary<string, object?> GetExports()
    {
        return new Dictionary<string, object?>
        {
            // Methods
            ["parse"] = new BuiltInMethod("parse", 1, 4, Parse),
            ["stringify"] = new BuiltInMethod("stringify", 1, 4, Stringify),
            ["escape"] = new BuiltInMethod("escape", 1, 1, Escape),
            ["unescape"] = new BuiltInMethod("unescape", 1, 1, Unescape),
            // Aliases
            ["decode"] = new BuiltInMethod("decode", 1, 4, Parse),
            ["encode"] = new BuiltInMethod("encode", 1, 4, Stringify)
        };
    }

    /// <summary>
    /// Parses a query string into an object.
    /// parse(str, sep='&', eq='=', options?)
    /// </summary>
    private static object? Parse(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0)
            return new SharpTSObject(new Dictionary<string, object?>());

        var str = args[0]?.ToString() ?? "";
        var sep = args.Count > 1 && args[1] is { } sep1 ? sep1.ToString() ?? "&" : "&";
        var eq = args.Count > 2 && args[2] is { } eq2 ? eq2.ToString() ?? "=" : "=";

        var result = new Dictionary<string, object?>();

        if (string.IsNullOrEmpty(str))
            return new SharpTSObject(result);

        var pairs = str.Split(sep, StringSplitOptions.RemoveEmptyEntries);
        foreach (var pair in pairs)
        {
            var eqIndex = pair.IndexOf(eq, StringComparison.Ordinal);
            string key, value;

            if (eqIndex >= 0)
            {
                key = Uri.UnescapeDataString(pair[..eqIndex].Replace('+', ' '));
                value = Uri.UnescapeDataString(pair[(eqIndex + eq.Length)..].Replace('+', ' '));
            }
            else
            {
                key = Uri.UnescapeDataString(pair.Replace('+', ' '));
                value = "";
            }

            // If key already exists, convert to array or add to existing array
            if (result.TryGetValue(key, out var existing))
            {
                if (existing is SharpTSArray arr)
                {
                    arr.Elements.Add(value);
                }
                else
                {
                    result[key] = new SharpTSArray([existing, value]);
                }
            }
            else
            {
                result[key] = value;
            }
        }

        return new SharpTSObject(result);
    }

    /// <summary>
    /// Serializes an object into a query string.
    /// stringify(obj, sep='&', eq='=', options?)
    /// </summary>
    private static object? Stringify(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0 || args[0] == null)
            return "";

        var sep = args.Count > 1 && args[1] is { } sep1 ? sep1.ToString() ?? "&" : "&";
        var eq = args.Count > 2 && args[2] is { } eq2 ? eq2.ToString() ?? "=" : "=";

        var pairs = new List<string>();
        var obj = args[0];

        if (obj is SharpTSObject tsObj)
        {
            foreach (var kvp in tsObj.Fields)
            {
                AddPairs(pairs, kvp.Key, kvp.Value, sep, eq);
            }
        }
        else if (obj is Dictionary<string, object?> dict)
        {
            foreach (var kvp in dict)
            {
                AddPairs(pairs, kvp.Key, kvp.Value, sep, eq);
            }
        }

        return string.Join(sep, pairs);
    }

    private static void AddPairs(List<string> pairs, string key, object? value, string sep, string eq)
    {
        var encodedKey = Uri.EscapeDataString(key);

        if (value is SharpTSArray arr)
        {
            foreach (var item in arr.Elements)
            {
                var encodedValue = Uri.EscapeDataString(item?.ToString() ?? "");
                pairs.Add($"{encodedKey}{eq}{encodedValue}");
            }
        }
        else
        {
            var encodedValue = Uri.EscapeDataString(value?.ToString() ?? "");
            pairs.Add($"{encodedKey}{eq}{encodedValue}");
        }
    }

    /// <summary>
    /// Percent-encodes a string for use in a URL query string.
    /// </summary>
    private static object? Escape(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0)
            return "";

        var str = args[0]?.ToString() ?? "";
        return Uri.EscapeDataString(str);
    }

    /// <summary>
    /// Decodes a percent-encoded string.
    /// </summary>
    private static object? Unescape(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0)
            return "";

        var str = args[0]?.ToString() ?? "";
        return Uri.UnescapeDataString(str.Replace('+', ' '));
    }
}

using System.Text.Json;
using SharpTS.Runtime.Types;

namespace SharpTS.Compilation;

public static partial class RuntimeTypes
{
    #region JSON Methods

    public static object? JsonParse(object? text)
    {
        var str = text?.ToString() ?? "null";
        try
        {
            using var doc = JsonDocument.Parse(str);
            return ConvertJsonElement(doc.RootElement);
        }
        catch (JsonException)
        {
            throw new Exception("Unexpected token in JSON");
        }
    }

    public static object? JsonParseWithReviver(object? text, object? reviver)
    {
        var parsed = JsonParse(text);
        if (reviver is TSFunction func)
        {
            // ECMA-262 25.5.1.1 — synthesize a root holder { "": parsed }
            // so the reviver receives the wrapper as `this` for the root call,
            // matching the emitted IL path in EmitJsonParseWithReviver.
            var root = new Dictionary<string, object?> { [""] = parsed };
            return ApplyReviver(root, "", func);
        }
        return parsed;
    }

    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Array => element.EnumerateArray()
                .Select(ConvertJsonElement)
                .ToList<object?>(),
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(p => p.Name, p => ConvertJsonElement(p.Value)),
            _ => null
        };
    }

    /// <summary>
    /// ECMA-262 25.5.1.1.1 InternalizeJSONProperty — used by the reflective C#
    /// path. The emitted-IL path lives in <c>RuntimeEmitter.Json.ParseReviver.cs</c>;
    /// kept in sync here so the two routines behave identically when callers
    /// invoke this directly (e.g. tooling, in-process harness).
    /// </summary>
    private static object? ApplyReviver(object? holder, object? key, TSFunction reviver)
    {
        var val = HolderGet(holder, key);

        if (val is List<object?> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var prop = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var newElement = ApplyReviver(val, prop, reviver);
                list[i] = newElement;
            }
        }
        else if (val is Dictionary<string, object?> dict)
        {
            // Snapshot keys — revivers can mutate `this` in place; the spec
            // freezes the iteration list before step 2.c.
            var keys = new List<string>(dict.Keys);
            foreach (var prop in keys)
            {
                var newElement = ApplyReviver(val, prop, reviver);
                if (newElement is null)
                    dict.Remove(prop);
                else
                    dict[prop] = newElement;
            }
        }

        // Step 3: Call reviver. The C# TSFunction.Invoke wrapper does not
        // expose a `this` channel — the emitted-IL path
        // (EmitApplyReviverHelper) is the spec-faithful one and uses
        // <c>$TSFunction.InvokeWithThis</c> to bind <c>this</c> = holder.
        // This C# helper is reachable only if external tooling calls
        // <see cref="JsonParseWithReviver"/> directly (no in-process caller
        // does today); in that fallback path we drop the holder binding,
        // matching pre-existing behavior.
        return reviver.Invoke(key, val);
    }

    private static object? HolderGet(object? holder, object? key)
    {
        var keyStr = key?.ToString() ?? "";
        if (holder is Dictionary<string, object?> dict)
            return dict.TryGetValue(keyStr, out var v) ? v : null;
        if (holder is List<object?> list)
        {
            if (int.TryParse(keyStr, out var idx) && idx >= 0 && idx < list.Count)
                return list[idx];
            return null;
        }
        return null;
    }

    public static object? JsonStringify(object? value)
    {
        return StringifyJsonValue(value, null, null, "", 0);
    }

    public static object? JsonStringifyFull(object? value, object? replacer, object? space)
    {
        // Handle space parameter: number = spaces, string = literal indent string
        string indentStr = "";
        switch (space)
        {
            case double d:
                var count = (int)Math.Min(Math.Max(d, 0), 10);
                indentStr = new string(' ', count);
                break;
            case string s:
                indentStr = s.Length > 10 ? s[..10] : s;
                break;
        }

        TSFunction? replacerFunc = replacer as TSFunction;
        HashSet<string>? allowedKeys = null;

        if (replacer is List<object?> list)
        {
            allowedKeys = list.OfType<string>().ToHashSet();
        }

        return StringifyJsonValue(value, replacerFunc, allowedKeys, indentStr, 0);
    }

    private static string? StringifyJsonValue(object? value, TSFunction? replacer, HashSet<string>? allowedKeys, string indentStr, int depth)
    {
        // Check for toJSON() method before serializing
        value = CallToJsonIfExists(value);

        // Check for BigInt - must throw TypeError
        // Handle both SharpTSBigInt (interpreter) and BigInteger (compiled)
        if (value != null)
        {
            var typeName = value.GetType().Name;
            if (typeName == "SharpTSBigInt" || typeName == "BigInteger")
            {
                throw new Exception("TypeError: BigInt value can't be serialized in JSON");
            }
        }

        // Check for class instances (dynamically emitted types with _fields)
        if (value != null && IsClassInstance(value))
        {
            return StringifyClassInstance(value, replacer, allowedKeys, indentStr, depth);
        }

        return value switch
        {
            null => "null",
            bool b => b ? "true" : "false",
            double d => FormatJsonNumber(d),
            string s => Runtime.BuiltIns.JsonStringEscaper.Quote(s),
            List<object?> arr => StringifyJsonArray(arr, replacer, allowedKeys, indentStr, depth),
            Dictionary<string, object?> obj => StringifyJsonObject(obj, replacer, allowedKeys, indentStr, depth),
            _ => null
        };
    }

    /// <summary>
    /// Checks if the value has a toJSON() method and calls it if present.
    /// </summary>
    private static object? CallToJsonIfExists(object? value)
    {
        if (value == null) return value;

        var type = value.GetType();

        // Check for toJSON method on the object's type
        var toJsonMethod = type.GetMethod("toJSON", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (toJsonMethod != null)
        {
            return toJsonMethod.Invoke(value, null);
        }

        // Check for toJSON in the compiler-emitted $IHasFields dictionary
        // (for objects with a callable toJSON data property).
        if (ManagedEmittedShapeReflection.TryGetFields(value, out var fields))
        {
            if (fields!.TryGetValue("toJSON", out var toJsonFunc) &&
                toJsonFunc is TSFunction func)
            {
                return func.Invoke();
            }
        }

        return value;
    }

    /// <summary>
    /// Checks if an object is a class instance (dynamically emitted type with _fields).
    /// </summary>
    private static bool IsClassInstance(object value)
    {
        var type = value.GetType();
        // Exclude built-in types
        if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal))
            return false;
        if (type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(List<>) ||
                                   type.GetGenericTypeDefinition() == typeof(Dictionary<,>)))
            return false;

        return ManagedEmittedShapeReflection.IsShape(
            type, ManagedEmittedShape.HasFields);
    }

    /// <summary>
    /// Stringifies a class instance by serializing its typed backing fields and _fields dictionary.
    /// </summary>
    private static string StringifyClassInstance(object value, TSFunction? replacer, HashSet<string>? allowedKeys, string indentStr, int depth)
    {
        Dictionary<string, object?> allFields = [];
        if (!ManagedEmittedShapeReflection.TryGetFields(value, out var fields))
        {
            throw new InvalidOperationException(
                "StringifyClassInstance requires a compiler-emitted $IHasFields object.");
        }

        foreach (var kv in fields!)
        {
            if (allowedKeys == null || allowedKeys.Contains(kv.Key))
            {
                allFields[kv.Key] = kv.Value;
            }
        }

        if (allFields.Count == 0) return "{}";

        List<string> parts = [];
        foreach (var kv in allFields)
        {
            var val = kv.Value;
            if (replacer != null)
            {
                val = replacer.Invoke(kv.Key, val);
            }
            var str = StringifyJsonValue(val, replacer, allowedKeys, indentStr, depth + 1);
            if (str != null)
            {
                var escapedKey = Runtime.BuiltIns.JsonStringEscaper.Quote(kv.Key);
                parts.Add($"{escapedKey}:{(indentStr.Length > 0 ? " " : "")}{str}");
            }
        }

        if (parts.Count == 0) return "{}";

        if (indentStr.Length > 0)
        {
            var newline = "\n" + GetIndent(indentStr, depth + 1);
            var close = "\n" + GetIndent(indentStr, depth);
            return "{" + newline + string.Join("," + newline, parts) + close + "}";
        }
        return "{" + string.Join(",", parts) + "}";
    }

    private static string FormatJsonNumber(double d)
    {
        if (double.IsNaN(d) || double.IsInfinity(d)) return "null";
        if (d == Math.Floor(d) && Math.Abs(d) < 1e15)
            return ((long)d).ToString();
        return d.ToString("G15");
    }

    private static string StringifyJsonArray(List<object?> arr, TSFunction? replacer, HashSet<string>? allowedKeys, string indentStr, int depth)
    {
        if (arr.Count == 0) return "[]";

        List<string> parts = [];
        for (int i = 0; i < arr.Count; i++)
        {
            var val = arr[i];
            if (replacer != null)
            {
                val = replacer.Invoke((double)i, val);
            }
            var str = StringifyJsonValue(val, replacer, allowedKeys, indentStr, depth + 1);
            parts.Add(str ?? "null");
        }

        if (indentStr.Length > 0)
        {
            var newline = "\n" + GetIndent(indentStr, depth + 1);
            var close = "\n" + GetIndent(indentStr, depth);
            return "[" + newline + string.Join("," + newline, parts) + close + "]";
        }
        return "[" + string.Join(",", parts) + "]";
    }

    private static string StringifyJsonObject(Dictionary<string, object?> obj, TSFunction? replacer, HashSet<string>? allowedKeys, string indentStr, int depth)
    {
        var fields = obj;
        if (allowedKeys != null)
        {
            fields = obj.Where(kv => allowedKeys.Contains(kv.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        if (fields.Count == 0) return "{}";

        List<string> parts = [];
        foreach (var kv in fields)
        {
            var val = kv.Value;
            if (replacer != null)
            {
                val = replacer.Invoke(kv.Key, val);
            }
            var str = StringifyJsonValue(val, replacer, allowedKeys, indentStr, depth + 1);
            if (str != null)
            {
                var escapedKey = Runtime.BuiltIns.JsonStringEscaper.Quote(kv.Key);
                parts.Add($"{escapedKey}:{(indentStr.Length > 0 ? " " : "")}{str}");
            }
        }

        if (indentStr.Length > 0)
        {
            var newline = "\n" + GetIndent(indentStr, depth + 1);
            var close = "\n" + GetIndent(indentStr, depth);
            return "{" + newline + string.Join("," + newline, parts) + close + "}";
        }
        return "{" + string.Join(",", parts) + "}";
    }

    private static string GetIndent(string indentStr, int depth)
    {
        return string.Concat(Enumerable.Repeat(indentStr, depth));
    }

    #endregion
}

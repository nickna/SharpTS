using System.Text.Json;

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
            return ApplyReviver(parsed, "", func);
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

    private static object? ApplyReviver(object? value, object? key, TSFunction reviver)
    {
        // First, recursively transform children (bottom-up)
        if (value is Dictionary<string, object?> dict)
        {
            var newDict = new Dictionary<string, object?>();
            foreach (var kv in dict)
            {
                // ApplyReviver already calls the reviver for each child
                var result = ApplyReviver(kv.Value, kv.Key, reviver);
                if (result != null) // undefined removes the property
                    newDict[kv.Key] = result;
            }
            value = newDict;
        }
        else if (value is List<object?> list)
        {
            var newList = new List<object?>();
            for (int i = 0; i < list.Count; i++)
            {
                // ApplyReviver already calls the reviver for each element
                var result = ApplyReviver(list[i], (double)i, reviver);
                newList.Add(result);
            }
            value = newList;
        }

        // Then call reviver for THIS node (after children are transformed)
        return reviver.Invoke(key, value);
    }

    public static object? JsonStringify(object? value)
    {
        return StringifyJsonValue(value, null, null, 0, 0);
    }

    public static object? JsonStringifyFull(object? value, object? replacer, object? space)
    {
        int indent = space switch
        {
            double d => (int)Math.Min(d, 10),
            string s => Math.Min(s.Length, 10),
            _ => 0
        };

        TSFunction? replacerFunc = replacer as TSFunction;
        HashSet<string>? allowedKeys = null;

        if (replacer is List<object?> list)
        {
            allowedKeys = list.OfType<string>().ToHashSet();
        }

        return StringifyJsonValue(value, replacerFunc, allowedKeys, indent, 0);
    }

    private static string? StringifyJsonValue(object? value, TSFunction? replacer, HashSet<string>? allowedKeys, int indent, int depth)
    {
        return value switch
        {
            null => "null",
            bool b => b ? "true" : "false",
            double d => FormatJsonNumber(d),
            string s => JsonSerializer.Serialize(s),
            List<object?> arr => StringifyJsonArray(arr, replacer, allowedKeys, indent, depth),
            Dictionary<string, object?> obj => StringifyJsonObject(obj, replacer, allowedKeys, indent, depth),
            _ => null
        };
    }

    private static string FormatJsonNumber(double d)
    {
        if (double.IsNaN(d) || double.IsInfinity(d)) return "null";
        if (d == Math.Floor(d) && Math.Abs(d) < 1e15)
            return ((long)d).ToString();
        return d.ToString("G15");
    }

    private static string StringifyJsonArray(List<object?> arr, TSFunction? replacer, HashSet<string>? allowedKeys, int indent, int depth)
    {
        if (arr.Count == 0) return "[]";

        var parts = new List<string>();
        for (int i = 0; i < arr.Count; i++)
        {
            var val = arr[i];
            if (replacer != null)
            {
                val = replacer.Invoke((double)i, val);
            }
            var str = StringifyJsonValue(val, replacer, allowedKeys, indent, depth + 1);
            parts.Add(str ?? "null");
        }

        if (indent > 0)
        {
            var newline = "\n" + new string(' ', indent * (depth + 1));
            var close = "\n" + new string(' ', indent * depth);
            return "[" + newline + string.Join("," + newline, parts) + close + "]";
        }
        return "[" + string.Join(",", parts) + "]";
    }

    private static string StringifyJsonObject(Dictionary<string, object?> obj, TSFunction? replacer, HashSet<string>? allowedKeys, int indent, int depth)
    {
        var fields = obj;
        if (allowedKeys != null)
        {
            fields = obj.Where(kv => allowedKeys.Contains(kv.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        if (fields.Count == 0) return "{}";

        var parts = new List<string>();
        foreach (var kv in fields)
        {
            var val = kv.Value;
            if (replacer != null)
            {
                val = replacer.Invoke(kv.Key, val);
            }
            var str = StringifyJsonValue(val, replacer, allowedKeys, indent, depth + 1);
            if (str != null)
            {
                var escapedKey = JsonSerializer.Serialize(kv.Key);
                parts.Add($"{escapedKey}:{(indent > 0 ? " " : "")}{str}");
            }
        }

        if (indent > 0)
        {
            var newline = "\n" + new string(' ', indent * (depth + 1));
            var close = "\n" + new string(' ', indent * depth);
            return "{" + newline + string.Join("," + newline, parts) + close + "}";
        }
        return "{" + string.Join(",", parts) + "}";
    }

    #endregion
}

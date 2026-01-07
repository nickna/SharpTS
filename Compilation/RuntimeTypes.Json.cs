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
            string s => JsonSerializer.Serialize(s),
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

        // Check for toJSON in _fields dictionary (for objects with callable toJSON property)
        var fieldsField = type.GetField("_fields", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (fieldsField?.GetValue(value) is Dictionary<string, object?> fields)
        {
            if (fields.TryGetValue("toJSON", out var toJsonFunc) && toJsonFunc is TSFunction func)
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

        // Check for _fields field (indicates a compiled class instance)
        var fieldsField = type.GetField("_fields", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return fieldsField != null;
    }

    /// <summary>
    /// Stringifies a class instance by serializing its typed backing fields and _fields dictionary.
    /// </summary>
    private static string StringifyClassInstance(object value, TSFunction? replacer, HashSet<string>? allowedKeys, string indentStr, int depth)
    {
        var type = value.GetType();
        var allFields = new Dictionary<string, object?>();
        var seenKeys = new HashSet<string>();

        // Get values from typed backing fields (fields starting with __)
        foreach (var backingField in type.GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
        {
            if (backingField.Name.StartsWith("__"))
            {
                string propName = backingField.Name[2..];
                seenKeys.Add(propName);
                if (allowedKeys == null || allowedKeys.Contains(propName))
                {
                    allFields[propName] = backingField.GetValue(value);
                }
            }
        }

        // Also get from _fields dictionary (for dynamic properties and generic type fields)
        var fieldsField = type.GetField("_fields", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (fieldsField?.GetValue(value) is Dictionary<string, object?> fields)
        {
            foreach (var kv in fields)
            {
                if (!seenKeys.Contains(kv.Key) && (allowedKeys == null || allowedKeys.Contains(kv.Key)))
                {
                    allFields[kv.Key] = kv.Value;
                }
            }
        }

        if (allFields.Count == 0) return "{}";

        var parts = new List<string>();
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
                var escapedKey = JsonSerializer.Serialize(kv.Key);
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

        var parts = new List<string>();
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

        var parts = new List<string>();
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
                var escapedKey = JsonSerializer.Serialize(kv.Key);
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

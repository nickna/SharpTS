using System.Text.Json;
using SharpTS.Execution;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Static methods on the JSON namespace (JSON.parse, JSON.stringify)
/// </summary>
public static class JSONBuiltIns
{
    public static object? GetStaticMethod(string name)
    {
        return name switch
        {
            "parse" => new BuiltInMethod("parse", 1, 2, ParseJson),
            "stringify" => new BuiltInMethod("stringify", 1, 3, StringifyJson),
            _ => null
        };
    }

    private static object? ParseJson(Interpreter interp, object? _, List<object?> args)
    {
        var text = args[0]?.ToString() ?? "null";
        var reviver = args.Count > 1 ? args[1] as ISharpTSCallable : null;

        object? parsed;
        try
        {
            using var doc = JsonDocument.Parse(text);
            parsed = ConvertJsonElement(doc.RootElement);
        }
        catch (JsonException)
        {
            throw new Exception("Unexpected token in JSON");
        }

        if (reviver != null)
        {
            parsed = ApplyReviver(interp, parsed, "", reviver);
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
            JsonValueKind.Array => new SharpTSArray(
                element.EnumerateArray().Select(ConvertJsonElement).ToList()),
            JsonValueKind.Object => new SharpTSObject(
                element.EnumerateObject().ToDictionary(
                    p => p.Name,
                    p => ConvertJsonElement(p.Value))),
            _ => null
        };
    }

    private static object? ApplyReviver(Interpreter interp, object? value, object? key, ISharpTSCallable reviver)
    {
        // First, recursively transform children (bottom-up)
        if (value is SharpTSObject obj)
        {
            var newFields = new Dictionary<string, object?>();
            foreach (var kv in obj.Fields)
            {
                // ApplyReviver already calls the reviver for each child
                var result = ApplyReviver(interp, kv.Value, kv.Key, reviver);
                if (result != null) // undefined removes the property
                    newFields[kv.Key] = result;
            }
            value = new SharpTSObject(newFields);
        }
        else if (value is SharpTSArray arr)
        {
            var newElements = new List<object?>();
            for (int i = 0; i < arr.Elements.Count; i++)
            {
                // ApplyReviver already calls the reviver for each element
                var result = ApplyReviver(interp, arr.Elements[i], (double)i, reviver);
                newElements.Add(result);
            }
            value = new SharpTSArray(newElements);
        }

        // Then call reviver for this node (after children are transformed)
        return reviver.Call(interp, [key, value]);
    }

    private static object? StringifyJson(Interpreter interp, object? _, List<object?> args)
    {
        var value = args[0];
        var replacer = args.Count > 1 ? args[1] : null;
        var space = args.Count > 2 ? args[2] : null;

        int indent = space switch
        {
            double d => (int)Math.Min(d, 10),
            string s => Math.Min(s.Length, 10),
            _ => 0
        };

        var replacerFunc = replacer as ISharpTSCallable;
        var replacerArray = replacer as SharpTSArray;
        HashSet<string>? allowedKeys = null;

        if (replacerArray != null)
        {
            allowedKeys = replacerArray.Elements
                .OfType<string>()
                .ToHashSet();
        }

        return StringifyValue(interp, value, "", replacerFunc, allowedKeys, indent, 0);
    }

    private static string? StringifyValue(Interpreter interp, object? value, object? key,
        ISharpTSCallable? replacer, HashSet<string>? allowedKeys, int indent, int depth)
    {
        if (replacer != null)
        {
            value = replacer.Call(interp, [key, value]);
        }

        return value switch
        {
            null => "null",
            bool b => b ? "true" : "false",
            double d => FormatJsonNumber(d),
            string s => JsonSerializer.Serialize(s),
            SharpTSArray arr => StringifyArray(interp, arr, replacer, allowedKeys, indent, depth),
            SharpTSObject obj => StringifyObject(interp, obj, replacer, allowedKeys, indent, depth),
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

    private static string StringifyArray(Interpreter interp, SharpTSArray arr,
        ISharpTSCallable? replacer, HashSet<string>? allowedKeys, int indent, int depth)
    {
        if (arr.Elements.Count == 0) return "[]";

        var parts = new List<string>();
        for (int i = 0; i < arr.Elements.Count; i++)
        {
            var str = StringifyValue(interp, arr.Elements[i], (double)i, replacer, allowedKeys, indent, depth + 1);
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

    private static string StringifyObject(Interpreter interp, SharpTSObject obj,
        ISharpTSCallable? replacer, HashSet<string>? allowedKeys, int indent, int depth)
    {
        var fields = obj.Fields;
        if (allowedKeys != null)
        {
            fields = fields.Where(kv => allowedKeys.Contains(kv.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        if (fields.Count == 0) return "{}";

        var parts = new List<string>();
        foreach (var kv in fields)
        {
            var str = StringifyValue(interp, kv.Value, kv.Key, replacer, allowedKeys, indent, depth + 1);
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
}

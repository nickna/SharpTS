using System.Text;
using System.Text.Json;
using SharpTS.Execution;
using SharpTS.Runtime.Exceptions;
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
            Dictionary<string, object?> newFields = [];
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
            List<object?> newElements = [];
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

        var replacerFunc = replacer as ISharpTSCallable;
        var replacerArray = replacer as SharpTSArray;
        HashSet<string>? allowedKeys = null;

        if (replacerArray != null)
        {
            allowedKeys = replacerArray.Elements
                .OfType<string>()
                .ToHashSet();
        }

        var sb = new StringBuilder();
        if (StringifyValue(interp, value, "", replacerFunc, allowedKeys, indentStr, 0, sb))
        {
            return sb.ToString();
        }

        return null;
    }

    private static bool StringifyValue(Interpreter interp, object? value, object? key,
        ISharpTSCallable? replacer, HashSet<string>? allowedKeys, string indentStr, int depth, StringBuilder sb)
    {
        if (replacer != null)
        {
            value = replacer.Call(interp, [key, value]);
        }

        // Check for toJSON() method before serializing
        value = CallToJsonIfExists(interp, value);

        switch (value)
        {
            case null:
                sb.Append("null");
                return true;
            case bool b:
                sb.Append(b ? "true" : "false");
                return true;
            case double d:
                var numStr = FormatJsonNumber(d);
                if (numStr == "null") sb.Append("null");
                else sb.Append(numStr);
                return true;
            case string s:
                sb.Append(JsonSerializer.Serialize(s));
                return true;
            case SharpTSBigInt:
                throw new ThrowException("TypeError: BigInt value can't be serialized in JSON");
            case SharpTSArray arr:
                StringifyArray(interp, arr, replacer, allowedKeys, indentStr, depth, sb);
                return true;
            case SharpTSObject obj:
                StringifyObject(interp, obj, replacer, allowedKeys, indentStr, depth, sb);
                return true;
            case SharpTSInstance inst:
                StringifyInstance(interp, inst, replacer, allowedKeys, indentStr, depth, sb);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Checks if the value has a toJSON() method and calls it if present.
    /// </summary>
    private static object? CallToJsonIfExists(Interpreter interp, object? value)
    {
        if (value is SharpTSInstance inst)
        {
            var toJson = inst.GetClass().FindMethod("toJSON");
            if (toJson != null)
                return toJson.Bind(inst).Call(interp, []);
        }
        else if (value is SharpTSObject obj && obj.Fields.TryGetValue("toJSON", out var fn))
        {
            if (fn is ISharpTSCallable callable)
                return callable.Call(interp, []);
        }
        return value;
    }

    private static string FormatJsonNumber(double d)
    {
        if (double.IsNaN(d) || double.IsInfinity(d)) return "null";
        if (d == Math.Floor(d) && Math.Abs(d) < 1e15)
            return ((long)d).ToString();
        return d.ToString("G15");
    }

    private static void StringifyArray(Interpreter interp, SharpTSArray arr,
        ISharpTSCallable? replacer, HashSet<string>? allowedKeys, string indentStr, int depth, StringBuilder sb)
    {
        if (arr.Elements.Count == 0)
        {
            sb.Append("[]");
            return;
        }

        sb.Append('[');
        
        bool pretty = indentStr.Length > 0;
        string stepIndent = pretty ? "\n" + GetIndent(indentStr, depth + 1) : "";
        string separator = pretty ? "," + stepIndent : ",";

        if (pretty) sb.Append(stepIndent);

        for (int i = 0; i < arr.Elements.Count; i++)
        {
            if (i > 0) sb.Append(separator);
            
            if (!StringifyValue(interp, arr.Elements[i], (double)i, replacer, allowedKeys, indentStr, depth + 1, sb))
            {
                sb.Append("null");
            }
        }

        if (pretty)
        {
            sb.Append('\n');
            sb.Append(GetIndent(indentStr, depth));
        }
        sb.Append(']');
    }

    private static void StringifyObject(Interpreter interp, SharpTSObject obj,
        ISharpTSCallable? replacer, HashSet<string>? allowedKeys, string indentStr, int depth, StringBuilder sb)
    {
        var fields = obj.Fields;
        if (allowedKeys != null)
        {
            fields = fields.Where(kv => allowedKeys.Contains(kv.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        if (fields.Count == 0)
        {
            sb.Append("{}");
            return;
        }

        sb.Append('{');

        bool pretty = indentStr.Length > 0;
        string stepIndent = pretty ? "\n" + GetIndent(indentStr, depth + 1) : "";
        
        if (pretty) sb.Append(stepIndent);

        bool first = true;
        foreach (var kv in fields)
        {
            int mark = sb.Length;

            if (!first)
            {
                sb.Append(',');
                if (pretty) sb.Append(stepIndent);
            }
            
            sb.Append(JsonSerializer.Serialize(kv.Key));
            sb.Append(':');
            if (pretty) sb.Append(' ');

            if (StringifyValue(interp, kv.Value, kv.Key, replacer, allowedKeys, indentStr, depth + 1, sb))
            {
                first = false;
            }
            else
            {
                // Value is undefined, revert this entry
                sb.Length = mark;
                
                // If we reverted, and it was NOT the first element, we might have added a comma + indentation 
                // at the beginning of this iteration. 
                // Wait, if !first, we added comma. We need to revert that too?
                // The `mark` is taken BEFORE adding the comma. So resetting to `mark` removes the comma too.
                // So `first` remains whatever it was before this iteration.
                // Correct.
            }
        }

        if (pretty)
        {
            sb.Append('\n');
            sb.Append(GetIndent(indentStr, depth));
        }
        sb.Append('}');
    }

    private static void StringifyInstance(Interpreter interp, SharpTSInstance inst,
        ISharpTSCallable? replacer, HashSet<string>? allowedKeys, string indentStr, int depth, StringBuilder sb)
    {
        IEnumerable<string> fieldNames = inst.GetFieldNames();
        if (allowedKeys != null)
        {
            fieldNames = fieldNames.Where(k => allowedKeys.Contains(k));
        }

        var namesList = fieldNames.ToList();
        if (namesList.Count == 0)
        {
            sb.Append("{}");
            return;
        }

        sb.Append('{');

        bool pretty = indentStr.Length > 0;
        string stepIndent = pretty ? "\n" + GetIndent(indentStr, depth + 1) : "";
        
        if (pretty) sb.Append(stepIndent);

        bool first = true;
        foreach (var name in namesList)
        {
            int mark = sb.Length;

            if (!first)
            {
                sb.Append(',');
                if (pretty) sb.Append(stepIndent);
            }
            
            sb.Append(JsonSerializer.Serialize(name));
            sb.Append(':');
            if (pretty) sb.Append(' ');

            var fieldValue = inst.GetRawField(name);
            if (StringifyValue(interp, fieldValue, name, replacer, allowedKeys, indentStr, depth + 1, sb))
            {
                first = false;
            }
            else
            {
                sb.Length = mark;
            }
        }

        if (pretty)
        {
            sb.Append('\n');
            sb.Append(GetIndent(indentStr, depth));
        }
        sb.Append('}');
    }

    private static string GetIndent(string indentStr, int depth)
    {
        // Optimization: Cache small indent strings? 
        // For now, this is acceptable as it's only for pretty printing.
        return string.Concat(Enumerable.Repeat(indentStr, depth));
    }
}

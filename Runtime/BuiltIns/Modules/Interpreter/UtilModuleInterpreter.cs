using System.Text;
using System.Text.Json;
using SharpTS.Runtime.Types;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

/// <summary>
/// Interpreter-mode implementation of the Node.js 'util' module.
/// </summary>
public static class UtilModuleInterpreter
{
    /// <summary>
    /// Gets all exported values for the util module.
    /// </summary>
    public static Dictionary<string, object?> GetExports()
    {
        return new Dictionary<string, object?>
        {
            ["format"] = new BuiltInMethod("format", 0, int.MaxValue, Format),
            ["inspect"] = new BuiltInMethod("inspect", 1, 2, Inspect),
            ["types"] = CreateTypesObject()
        };
    }

    private static SharpTSObject CreateTypesObject()
    {
        return new SharpTSObject(new Dictionary<string, object?>
        {
            ["isArray"] = new BuiltInMethod("isArray", 1, IsArray),
            ["isDate"] = new BuiltInMethod("isDate", 1, IsDate),
            ["isFunction"] = new BuiltInMethod("isFunction", 1, IsFunction),
            ["isNull"] = new BuiltInMethod("isNull", 1, IsNull),
            ["isUndefined"] = new BuiltInMethod("isUndefined", 1, IsUndefined)
        });
    }

    private static object? Format(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0)
            return "";

        var format = args[0]?.ToString() ?? "";
        if (args.Count == 1)
            return format;

        var result = new StringBuilder();
        var argIndex = 1;
        var i = 0;

        while (i < format.Length)
        {
            if (format[i] == '%' && i + 1 < format.Length)
            {
                var specifier = format[i + 1];
                switch (specifier)
                {
                    case 's': // String
                        result.Append(argIndex < args.Count ? args[argIndex++]?.ToString() ?? "undefined" : "%s");
                        i += 2;
                        continue;
                    case 'd': // Integer
                    case 'i':
                        if (argIndex < args.Count && args[argIndex] is double d)
                        {
                            result.Append((int)d);
                            argIndex++;
                        }
                        else
                            result.Append("%").Append(specifier);
                        i += 2;
                        continue;
                    case 'f': // Float
                        if (argIndex < args.Count && args[argIndex] is double f)
                        {
                            result.Append(f);
                            argIndex++;
                        }
                        else
                            result.Append("%f");
                        i += 2;
                        continue;
                    case 'j': // JSON
                        if (argIndex < args.Count)
                        {
                            result.Append(JsonSerializeValue(args[argIndex++]));
                        }
                        else
                            result.Append("%j");
                        i += 2;
                        continue;
                    case 'o': // Object
                    case 'O':
                        if (argIndex < args.Count)
                        {
                            result.Append(InspectValue(args[argIndex++], 2));
                        }
                        else
                            result.Append("%").Append(specifier);
                        i += 2;
                        continue;
                    case '%': // Literal %
                        result.Append('%');
                        i += 2;
                        continue;
                }
            }
            result.Append(format[i]);
            i++;
        }

        // Append remaining arguments
        while (argIndex < args.Count)
        {
            result.Append(' ');
            result.Append(args[argIndex++]?.ToString() ?? "undefined");
        }

        return result.ToString();
    }

    private static object? Inspect(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0)
            return "undefined";

        int depth = 2;
        if (args.Count > 1 && args[1] is SharpTSObject options)
        {
            var depthVal = options.GetProperty("depth");
            if (depthVal is double d)
                depth = (int)d;
        }

        return InspectValue(args[0], depth);
    }

    private static string InspectValue(object? value, int depth, int currentDepth = 0)
    {
        if (value == null)
            return "null";

        if (currentDepth > depth)
            return "[Object]";

        return value switch
        {
            string s => $"'{s}'",
            double d => d.ToString(),
            bool b => b ? "true" : "false",
            SharpTSArray arr => InspectArray(arr, depth, currentDepth),
            SharpTSObject obj => InspectObject(obj, depth, currentDepth),
            SharpTSFunction func => $"[Function]",
            _ => value.ToString() ?? "undefined"
        };
    }

    private static string InspectArray(SharpTSArray arr, int depth, int currentDepth)
    {
        if (currentDepth >= depth)
            return "[Array]";

        var elements = arr.Elements.Select(e => InspectValue(e, depth, currentDepth + 1));
        return $"[ {string.Join(", ", elements)} ]";
    }

    private static string InspectObject(SharpTSObject obj, int depth, int currentDepth)
    {
        if (currentDepth >= depth)
            return "[Object]";

        var props = obj.Fields.Select(kv =>
            $"{kv.Key}: {InspectValue(kv.Value, depth, currentDepth + 1)}");
        return $"{{ {string.Join(", ", props)} }}";
    }

    private static string JsonSerializeValue(object? value)
    {
        try
        {
            return JsonSerializer.Serialize(value);
        }
        catch
        {
            return "undefined";
        }
    }

    // Type checking functions
    private static object? IsArray(Interp interpreter, object? receiver, List<object?> args)
        => args.Count > 0 && args[0] is SharpTSArray;

    private static object? IsDate(Interp interpreter, object? receiver, List<object?> args)
        => args.Count > 0 && args[0] is SharpTSDate;

    private static object? IsFunction(Interp interpreter, object? receiver, List<object?> args)
        => args.Count > 0 && (args[0] is SharpTSFunction or BuiltInMethod);

    private static object? IsNull(Interp interpreter, object? receiver, List<object?> args)
        => args.Count > 0 && args[0] == null;

    private static object? IsUndefined(Interp interpreter, object? receiver, List<object?> args)
        => args.Count > 0 && args[0] == null; // In SharpTS, undefined is represented as null
}

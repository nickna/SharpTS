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
            ["deprecate"] = new BuiltInMethod("deprecate", 2, 3, Deprecate),
            ["callbackify"] = new BuiltInMethod("callbackify", 1, Callbackify),
            ["inherits"] = new BuiltInMethod("inherits", 2, Inherits),
            ["TextEncoder"] = new BuiltInMethod("TextEncoder", 0, CreateTextEncoder),
            ["TextDecoder"] = new BuiltInMethod("TextDecoder", 0, 2, CreateTextDecoder),
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
            ["isUndefined"] = new BuiltInMethod("isUndefined", 1, IsUndefined),
            ["isPromise"] = new BuiltInMethod("isPromise", 1, IsPromise),
            ["isRegExp"] = new BuiltInMethod("isRegExp", 1, IsRegExp),
            ["isMap"] = new BuiltInMethod("isMap", 1, IsMap),
            ["isSet"] = new BuiltInMethod("isSet", 1, IsSet),
            ["isTypedArray"] = new BuiltInMethod("isTypedArray", 1, IsTypedArray)
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
        => args.Count > 0 && (args[0] is SharpTSFunction or SharpTSArrowFunction or BuiltInMethod);

    private static object? IsNull(Interp interpreter, object? receiver, List<object?> args)
        => args.Count > 0 && args[0] == null;

    private static object? IsUndefined(Interp interpreter, object? receiver, List<object?> args)
        => args.Count > 0 && args[0] is SharpTSUndefined;

    private static object? IsPromise(Interp interpreter, object? receiver, List<object?> args)
        => args.Count > 0 && args[0] is SharpTSPromise;

    private static object? IsRegExp(Interp interpreter, object? receiver, List<object?> args)
        => args.Count > 0 && args[0] is SharpTSRegExp;

    private static object? IsMap(Interp interpreter, object? receiver, List<object?> args)
        => args.Count > 0 && args[0] is SharpTSMap;

    private static object? IsSet(Interp interpreter, object? receiver, List<object?> args)
        => args.Count > 0 && args[0] is SharpTSSet;

    private static object? IsTypedArray(Interp interpreter, object? receiver, List<object?> args)
        => args.Count > 0 && args[0] is SharpTSBuffer;

    private static object? Deprecate(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count < 2)
            throw new Exception("util.deprecate requires at least 2 arguments: fn, message");

        var fn = args[0];
        var message = args[1]?.ToString() ?? "";

        if (fn is ISharpTSCallable callable)
        {
            return new SharpTSDeprecatedFunction(callable, message);
        }

        if (fn is BuiltInMethod method)
        {
            return new SharpTSDeprecatedFunction(method, message);
        }

        throw new Exception("util.deprecate: first argument must be a function");
    }

    private static object? Callbackify(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count < 1)
            throw new Exception("util.callbackify requires 1 argument: fn");

        var fn = args[0];

        if (fn is ISharpTSCallable callable)
        {
            return new SharpTSCallbackifiedFunction(callable);
        }

        if (fn is BuiltInMethod method)
        {
            return new SharpTSCallbackifiedFunction(method);
        }

        throw new Exception("util.callbackify: argument must be a function");
    }

    private static object? Inherits(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count < 2)
            throw new Exception("util.inherits requires 2 arguments: constructor, superConstructor");

        var ctor = args[0];
        var superCtor = args[1];

        // Set constructor.super_ = superConstructor
        if (ctor is SharpTSClass ctorClass)
        {
            ctorClass.SetStaticProperty("super_", superCtor);
        }
        else if (ctor is SharpTSFunction ctorFunc)
        {
            // For plain functions, we can't easily add properties
            // This is a limitation - real Node.js modifies the prototype chain
        }

        return null;
    }

    private static object? CreateTextEncoder(Interp interpreter, object? receiver, List<object?> args)
    {
        return new SharpTSTextEncoder();
    }

    private static object? CreateTextDecoder(Interp interpreter, object? receiver, List<object?> args)
    {
        var encoding = "utf-8";
        var fatal = false;
        var ignoreBOM = false;

        if (args.Count > 0 && args[0] != null)
        {
            encoding = args[0]!.ToString() ?? "utf-8";
        }

        if (args.Count > 1 && args[1] is SharpTSObject options)
        {
            var fatalVal = options.GetProperty("fatal");
            if (fatalVal is true)
                fatal = true;

            var ignoreBOMVal = options.GetProperty("ignoreBOM");
            if (ignoreBOMVal is true)
                ignoreBOM = true;
        }

        return new SharpTSTextDecoder(encoding, fatal, ignoreBOM);
    }
}

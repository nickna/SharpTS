using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits util module helper methods.
    /// </summary>
    private void EmitUtilMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitUtilFormat(typeBuilder, runtime);
        EmitUtilInspect(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits: public static string UtilFormat(object[] args)
    /// Calls into UtilHelpers.Format for proper format specifier handling.
    /// </summary>
    private void EmitUtilFormat(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "UtilFormat",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.ObjectArray]);
        runtime.UtilFormat = method;

        var il = method.GetILGenerator();

        // Call UtilHelpers.Format(args)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(UtilHelpers).GetMethod(nameof(UtilHelpers.Format))!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static string UtilInspect(object obj, object options)
    /// Calls into UtilHelpers.Inspect for proper formatting.
    /// </summary>
    private void EmitUtilInspect(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "UtilInspect",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object, _types.Object]);
        runtime.UtilInspect = method;

        var il = method.GetILGenerator();

        // Call UtilHelpers.Inspect(obj, options)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(UtilHelpers).GetMethod(nameof(UtilHelpers.Inspect))!);
        il.Emit(OpCodes.Ret);
    }
}

/// <summary>
/// Static helper methods for util module, called from emitted IL.
/// These match the interpreter's behavior for parity.
/// </summary>
public static class UtilHelpers
{
    /// <summary>
    /// util.types.isArray - checks if value is an array.
    /// </summary>
    public static bool IsArray(object? value) => value is IList<object?>;

    /// <summary>
    /// util.types.isFunction - checks if value is a function.
    /// </summary>
    public static bool IsFunction(object? value) => value is Delegate;

    /// <summary>
    /// util.types.isNull - checks if value is null.
    /// </summary>
    public static bool IsNull(object? value) => value is null;

    /// <summary>
    /// util.types.isUndefined - checks if value is undefined (null in SharpTS).
    /// </summary>
    public static bool IsUndefined(object? value) => value is null;

    /// <summary>
    /// util.types.isDate - checks if value is a date.
    /// </summary>
    public static bool IsDate(object? value) => value is DateTime or DateTimeOffset;

    /// <summary>
    /// Implements util.format() with proper format specifier handling.
    /// </summary>
    public static string Format(object?[] args)
    {
        if (args.Length == 0)
            return "";

        var format = args[0]?.ToString() ?? "";
        if (args.Length == 1)
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
                        result.Append(argIndex < args.Length ? args[argIndex++]?.ToString() ?? "undefined" : "%s");
                        i += 2;
                        continue;
                    case 'd': // Integer
                    case 'i':
                        if (argIndex < args.Length && args[argIndex] is double d)
                        {
                            result.Append((int)d);
                            argIndex++;
                        }
                        else
                            result.Append('%').Append(specifier);
                        i += 2;
                        continue;
                    case 'f': // Float
                        if (argIndex < args.Length && args[argIndex] is double f)
                        {
                            result.Append(f);
                            argIndex++;
                        }
                        else
                            result.Append("%f");
                        i += 2;
                        continue;
                    case 'j': // JSON
                        if (argIndex < args.Length)
                        {
                            result.Append(System.Text.Json.JsonSerializer.Serialize(args[argIndex++]));
                        }
                        else
                            result.Append("%j");
                        i += 2;
                        continue;
                    case 'o': // Object
                    case 'O':
                        if (argIndex < args.Length)
                        {
                            result.Append(InspectValue(args[argIndex++], 2, 0));
                        }
                        else
                            result.Append('%').Append(specifier);
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
        while (argIndex < args.Length)
        {
            result.Append(' ');
            result.Append(args[argIndex++]?.ToString() ?? "undefined");
        }

        return result.ToString();
    }

    /// <summary>
    /// Implements util.inspect() with proper value formatting.
    /// </summary>
    public static string Inspect(object? obj, object? options)
    {
        int depth = 2;
        if (options is IDictionary<string, object?> dict && dict.TryGetValue("depth", out var depthVal) && depthVal is double d)
            depth = (int)d;

        return InspectValue(obj, depth, 0);
    }

    private static string InspectValue(object? value, int depth, int currentDepth)
    {
        if (value == null)
            return "null";

        if (currentDepth > depth)
            return "[Object]";

        return value switch
        {
            string s => $"'{s}'",
            double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            IList<object?> list => InspectArray(list, depth, currentDepth),
            IDictionary<string, object?> dict => InspectObject(dict, depth, currentDepth),
            Delegate => "[Function]",
            _ => value.ToString() ?? "undefined"
        };
    }

    private static string InspectArray(IList<object?> arr, int depth, int currentDepth)
    {
        if (currentDepth >= depth)
            return "[Array]";

        var elements = arr.Select(e => InspectValue(e, depth, currentDepth + 1));
        return $"[ {string.Join(", ", elements)} ]";
    }

    private static string InspectObject(IDictionary<string, object?> obj, int depth, int currentDepth)
    {
        if (currentDepth >= depth)
            return "[Object]";

        var props = obj.Select(kv => $"{kv.Key}: {InspectValue(kv.Value, depth, currentDepth + 1)}");
        return $"{{ {string.Join(", ", props)} }}";
    }
}

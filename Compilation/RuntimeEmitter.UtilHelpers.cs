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
    /// Checks for both emitted $Array (implements IList) and interpreter SharpTSArray.
    /// </summary>
    public static bool IsArray(object? value) =>
        value is IList<object?> || value is SharpTS.Runtime.Types.SharpTSArray;

    /// <summary>
    /// util.types.isFunction - checks if value is a function.
    /// Checks for Delegate, TSFunction (from RuntimeTypes), and $TSFunction (emitted into compiled DLLs).
    /// </summary>
    public static bool IsFunction(object? value)
    {
        if (value is null) return false;
        if (value is Delegate) return true;
        if (value is TSFunction) return true;
        // Check for emitted $TSFunction type (or $BoundTSFunction)
        var typeName = value.GetType().Name;
        return typeName is "$TSFunction" or "$BoundTSFunction";
    }

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
    /// util.types.isPromise - checks if value is a Promise.
    /// Supports both interpreter (SharpTSPromise) and compiled (Task) modes.
    /// </summary>
    public static bool IsPromise(object? value)
    {
        if (value is null) return false;
        if (value is SharpTS.Runtime.Types.SharpTSPromise) return true;
        // In compiled mode, promises are represented as Task<object?> or Task
        var type = value.GetType();
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(System.Threading.Tasks.Task<>)
            || value is System.Threading.Tasks.Task;
    }

    /// <summary>
    /// util.types.isRegExp - checks if value is a RegExp.
    /// Supports both interpreter (SharpTSRegExp) and compiled ($RegExp or Regex) modes.
    /// </summary>
    public static bool IsRegExp(object? value)
    {
        if (value is null) return false;
        if (value is SharpTS.Runtime.Types.SharpTSRegExp) return true;
        if (value is System.Text.RegularExpressions.Regex) return true;
        // In compiled mode, regex is represented as $RegExp emitted type
        var typeName = value.GetType().Name;
        return typeName == "$RegExp";
    }

    /// <summary>
    /// util.types.isMap - checks if value is a Map.
    /// Supports both interpreter (SharpTSMap) and compiled (Dictionary) modes.
    /// </summary>
    public static bool IsMap(object? value)
    {
        if (value is null) return false;
        if (value is SharpTS.Runtime.Types.SharpTSMap) return true;
        // In compiled mode, maps are represented as Dictionary<object, object?>
        var type = value.GetType();
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>);
    }

    /// <summary>
    /// util.types.isSet - checks if value is a Set.
    /// Supports both interpreter (SharpTSSet) and compiled (HashSet) modes.
    /// </summary>
    public static bool IsSet(object? value)
    {
        if (value is null) return false;
        if (value is SharpTS.Runtime.Types.SharpTSSet) return true;
        // In compiled mode, sets are represented as HashSet<object>
        var type = value.GetType();
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(HashSet<>);
    }

    /// <summary>
    /// util.types.isTypedArray - checks if value is a typed array (Buffer).
    /// </summary>
    public static bool IsTypedArray(object? value) => value is SharpTS.Runtime.Types.SharpTSBuffer;

    /// <summary>
    /// util.deprecate - wraps a function to log a deprecation warning on first call.
    /// Returns a DeprecatedFunction wrapper that can be invoked by compiled code.
    /// </summary>
    public static DeprecatedFunction Deprecate(object fn, string message)
    {
        return new DeprecatedFunction(fn, message);
    }

    /// <summary>
    /// util.callbackify - wraps a function to use callback-style error handling.
    /// The returned function takes original args + a callback as the last argument.
    /// Callback is called with (error, result).
    /// </summary>
    public static Func<object?[], object?> Callbackify(Delegate fn)
    {
        return args =>
        {
            if (args.Length == 0)
                throw new Exception("callbackified function requires at least a callback argument");

            // Last argument is the callback
            var callback = args[^1] as Delegate
                ?? throw new Exception("Last argument to callbackified function must be a callback");

            // Get original args (all except last)
            var originalArgs = args.Take(args.Length - 1).ToArray();

            try
            {
                var result = fn.DynamicInvoke(originalArgs);
                callback.DynamicInvoke(new object?[] { new object?[] { null, result } });
            }
            catch (Exception ex)
            {
                var errorMessage = ex.InnerException?.Message ?? ex.Message;
                callback.DynamicInvoke(new object?[] { new object?[] { errorMessage, null } });
            }

            return null;
        };
    }

    /// <summary>
    /// util.inherits - sets constructor.super_ = superConstructor.
    /// This is a legacy Node.js pattern for pseudo-classical inheritance.
    /// </summary>
    public static void Inherits(object ctor, object superCtor)
    {
        // In compiled mode, we use a dictionary to track the super_ property
        // This is a no-op for actual prototype chain manipulation
        // Real classes in .NET don't use this pattern
        if (ctor is IDictionary<string, object?> dict)
        {
            dict["super_"] = superCtor;
        }
    }

    /// <summary>
    /// Creates a new TextEncoder instance.
    /// TextEncoder always uses UTF-8 encoding.
    /// </summary>
    public static SharpTS.Runtime.Types.SharpTSTextEncoder CreateTextEncoder()
    {
        return new SharpTS.Runtime.Types.SharpTSTextEncoder();
    }

    /// <summary>
    /// Creates a new TextDecoder instance with the specified options.
    /// </summary>
    public static SharpTS.Runtime.Types.SharpTSTextDecoder CreateTextDecoder(string? encoding = null, bool fatal = false, bool ignoreBOM = false)
    {
        return new SharpTS.Runtime.Types.SharpTSTextDecoder(encoding ?? "utf-8", fatal, ignoreBOM);
    }

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

/// <summary>
/// Wrapper for deprecated functions that logs a warning on first invocation.
/// Used by util.deprecate() in compiled mode.
/// Has an Invoke method that can be called by the compiled code's InvokeValue.
/// </summary>
public class DeprecatedFunction
{
    private readonly object _wrapped;
    private readonly string _message;
    private bool _warned;

    public DeprecatedFunction(object fn, string message)
    {
        _wrapped = fn ?? throw new ArgumentNullException(nameof(fn));
        _message = message ?? "";
        _warned = false;
    }

    /// <summary>
    /// Invoke the wrapped function, logging a deprecation warning on first call.
    /// This method signature matches what InvokeValue looks for via reflection.
    /// </summary>
    public object? Invoke(params object?[] args)
    {
        if (!_warned)
        {
            _warned = true;
            Console.Error.WriteLine($"DeprecationWarning: {_message}");
        }

        // Handle different callable types
        if (_wrapped is TSFunction tsFunc)
        {
            return tsFunc.Invoke(args);
        }

        if (_wrapped is Delegate del)
        {
            return del.DynamicInvoke(new object?[] { args });
        }

        // Try to find an Invoke method via reflection (for $TSFunction and other callable types)
        var invokeMethod = _wrapped.GetType().GetMethod("Invoke");
        if (invokeMethod != null)
        {
            // Call Invoke(args) on the wrapped object
            return invokeMethod.Invoke(_wrapped, [args]);
        }

        throw new InvalidOperationException($"Cannot invoke deprecated function: wrapped value is not callable ({_wrapped.GetType().Name})");
    }

    public override string ToString() => "[Function: deprecated]";
}

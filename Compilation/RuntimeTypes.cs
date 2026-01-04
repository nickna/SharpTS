using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace SharpTS.Compilation;

/// <summary>
/// Represents a TypeScript function value that can be stored, passed, and invoked.
/// </summary>
/// <remarks>
/// Runtime wrapper for function references in compiled assemblies. Supports both static
/// methods (non-capturing functions) and instance methods (closures with display class).
/// Handles argument padding for default parameters and packing for rest parameters.
/// Used when functions are passed as values or stored in variables.
/// </remarks>
/// <seealso cref="RuntimeTypes"/>
/// <seealso cref="EmittedRuntime"/>
public class TSFunction
{
    private readonly object? _target;      // Display class instance (null for static)
    private readonly MethodInfo _method;   // The actual method to invoke

    public TSFunction(object? target, MethodInfo method)
    {
        _target = target;
        _method = method;
    }

    /// <summary>
    /// Creates a TSFunction for a static method.
    /// </summary>
    public TSFunction(MethodInfo method) : this(null, method)
    {
    }

    /// <summary>
    /// Invoke the function with the given arguments.
    /// Missing arguments are padded with null to support default parameters.
    /// Excess arguments are packed into an array for rest parameters.
    /// </summary>
    public object? Invoke(params object?[] args)
    {
        try
        {
            var paramCount = _method.GetParameters().Length;

            if (args.Length < paramCount)
            {
                // Pad with nulls for default parameters
                var paddedArgs = new object?[paramCount];
                Array.Copy(args, paddedArgs, args.Length);
                return _method.Invoke(_target, paddedArgs);
            }
            else if (args.Length > paramCount)
            {
                // More args than params - JavaScript semantics: ignore excess args
                var trimmedArgs = new object?[paramCount];
                Array.Copy(args, trimmedArgs, paramCount);
                return _method.Invoke(_target, trimmedArgs);
            }

            return _method.Invoke(_target, args);
        }
        catch (TargetInvocationException ex)
        {
            throw ex.InnerException ?? ex;
        }
    }

    /// <summary>
    /// Get the number of expected parameters.
    /// </summary>
    public int Arity => _method.GetParameters().Length;

    public override string ToString() => "[Function]";
}

/// <summary>
/// Runtime support methods emitted into each compiled assembly.
/// </summary>
/// <remarks>
/// Provides TypeScript runtime semantics for compiled DLLs: console.log, type coercion
/// (Stringify, ToNumber, IsTruthy), operators (Add, Equals), array/object operations,
/// dynamic invocation, and Math functions. Methods are copied into generated assemblies
/// to enable standalone execution without SharpTS.dll dependency.
/// </remarks>
/// <seealso cref="EmittedRuntime"/>
/// <seealso cref="ILCompiler"/>
public static class RuntimeTypes
{
    private static readonly Random _random = new();
    private static readonly Dictionary<string, Type> _compiledTypes = [];

    // Symbol-keyed property storage: object -> (symbol -> value)
    private static readonly ConditionalWeakTable<object, Dictionary<object, object?>> _symbolStorage = new();

    private static Dictionary<object, object?> GetSymbolDict(object obj)
    {
        return _symbolStorage.GetOrCreateValue(obj);
    }

    public static void RegisterType(string name, Type type)
    {
        _compiledTypes[name] = type;
    }

    #region Console

    public static void ConsoleLog(object? value)
    {
        Console.WriteLine(Stringify(value));
    }

    public static void ConsoleLogMultiple(object?[] values)
    {
        Console.WriteLine(string.Join(" ", values.Select(Stringify)));
    }

    #endregion

    #region Type Coercion

    public static string Stringify(object? value) => value switch
    {
        null => "null",
        bool b => b ? "true" : "false",
        double d => FormatNumber(d),
        string s => s,
        object[] arr => "[" + string.Join(", ", arr.Select(Stringify)) + "]",
        List<object?> list => "[" + string.Join(", ", list.Select(Stringify)) + "]",
        System.Collections.IList list => "[" + string.Join(", ", list.Cast<object?>().Select(Stringify)) + "]",
        Dictionary<string, object?> dict => StringifyObject(dict),
        _ => value.ToString() ?? "null"
    };

    private static string FormatNumber(double d)
    {
        if (double.IsNaN(d)) return "NaN";
        if (double.IsPositiveInfinity(d)) return "Infinity";
        if (double.IsNegativeInfinity(d)) return "-Infinity";
        if (d == Math.Floor(d) && Math.Abs(d) < 1e15)
            return ((long)d).ToString();
        return d.ToString("G15");
    }

    private static string StringifyObject(Dictionary<string, object?> dict)
    {
        var props = dict.Select(kv => $"{kv.Key}: {Stringify(kv.Value)}");
        return "{ " + string.Join(", ", props) + " }";
    }

    public static double ToNumber(object? value) => value switch
    {
        double d => d,
        int i => i,
        long l => l,
        bool b => b ? 1.0 : 0.0,
        string s when double.TryParse(s, out var d) => d,
        null => 0.0,
        _ => double.NaN
    };

    public static bool IsTruthy(object? value) => value switch
    {
        null => false,
        bool b => b,
        double d => d != 0.0 && !double.IsNaN(d),
        string s => s.Length > 0,
        _ => true
    };

    public static string TypeOf(object? value) => value switch
    {
        null => "object", // typeof null === "object" in JS
        bool => "boolean",
        double or int or long => "number",
        string => "string",
        TSFunction => "function",
        Delegate => "function",
        _ => "object"
    };

    public static bool InstanceOf(object? instance, object? classType)
    {
        if (instance == null || classType == null) return false;
        // For compiled code, we need to check if the instance's type matches or inherits from the class type
        var instanceType = instance.GetType();
        var targetType = classType as Type ?? classType.GetType();
        return targetType.IsAssignableFrom(instanceType);
    }

    #endregion

    #region Operators

    public static object Add(object? left, object? right)
    {
        // String concatenation if either operand is a string
        if (left is string || right is string)
        {
            return Stringify(left) + Stringify(right);
        }
        return ToNumber(left) + ToNumber(right);
    }

    public static new bool Equals(object? left, object? right)
    {
        if (left == null && right == null) return true;
        if (left == null || right == null) return false;

        // Same type comparison
        if (left.GetType() == right.GetType())
        {
            return left.Equals(right);
        }

        // Numeric comparison
        if (IsNumeric(left) && IsNumeric(right))
        {
            return ToNumber(left) == ToNumber(right);
        }

        return left.Equals(right);
    }

    private static bool IsNumeric(object? value) =>
        value is double or int or long;

    #endregion

    #region Arrays

    public static List<object?> CreateArray(object[] elements)
    {
        return [.. elements];
    }

    public static int GetLength(object? obj) => obj switch
    {
        List<object?> list => list.Count,
        object[] arr => arr.Length,
        string s => s.Length,
        _ => 0
    };

    public static object? GetElement(object? obj, int index) => obj switch
    {
        List<object?> list when index >= 0 && index < list.Count => list[index],
        object[] arr when index >= 0 && index < arr.Length => arr[index],
        string s when index >= 0 && index < s.Length => s[index].ToString(),
        _ => null
    };

    public static List<object?> GetKeys(object? obj)
    {
        if (obj is Dictionary<string, object?> dict)
        {
            return dict.Keys.Select(k => (object?)k).ToList();
        }
        if (obj is List<object?> list)
        {
            return Enumerable.Range(0, list.Count).Select(i => (object?)i.ToString()).ToList();
        }
        // For compiled class instances, get keys from _fields
        if (obj != null)
        {
            var type = obj.GetType();
            var field = type.GetField("_fields", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null && field.GetValue(obj) is Dictionary<string, object?> fields)
            {
                return fields.Keys.Select(k => (object?)k).ToList();
            }
        }
        return [];
    }

    public static List<object?> SpreadArray(object? arr)
    {
        if (arr is List<object?> list)
        {
            return [.. list];
        }
        return [];
    }

    public static List<object?> ConcatArrays(params object?[] arrays)
    {
        var result = new List<object?>();
        foreach (var arr in arrays)
        {
            if (arr is List<object?> list)
            {
                result.AddRange(list);
            }
        }
        return result;
    }

    public static object?[] ExpandCallArgs(object?[] argsWithSpreads, bool[] isSpread)
    {
        var result = new List<object?>();
        for (int i = 0; i < argsWithSpreads.Length; i++)
        {
            if (isSpread[i] && argsWithSpreads[i] is List<object?> list)
            {
                result.AddRange(list);
            }
            else
            {
                result.Add(argsWithSpreads[i]);
            }
        }
        return [.. result];
    }

    public static object? ArrayPop(List<object?> list)
    {
        if (list.Count == 0) return null;
        var last = list[^1];
        list.RemoveAt(list.Count - 1);
        return last;
    }

    public static object? ArrayShift(List<object?> list)
    {
        if (list.Count == 0) return null;
        var first = list[0];
        list.RemoveAt(0);
        return first;
    }

    public static double ArrayUnshift(List<object?> list, object? element)
    {
        list.Insert(0, element);
        return list.Count;
    }

    public static List<object?> ArraySlice(List<object?> list, object?[] args)
    {
        int start = args.Length > 0 ? (int)ToNumber(args[0]) : 0;
        int end = args.Length > 1 ? (int)ToNumber(args[1]) : list.Count;

        // Handle negative indices
        if (start < 0) start = Math.Max(0, list.Count + start);
        if (end < 0) end = Math.Max(0, list.Count + end);
        if (start > list.Count) start = list.Count;
        if (end > list.Count) end = list.Count;
        if (end <= start) return [];

        return list.GetRange(start, end - start);
    }

    public static List<object?> ArrayMap(List<object?> list, object? callback)
    {
        var result = new List<object?>();
        for (int i = 0; i < list.Count; i++)
        {
            var callResult = InvokeValue(callback, [list[i], (double)i, list]);
            result.Add(callResult);
        }
        return result;
    }

    public static List<object?> ArrayFilter(List<object?> list, object? callback)
    {
        var result = new List<object?>();
        for (int i = 0; i < list.Count; i++)
        {
            var callResult = InvokeValue(callback, [list[i], (double)i, list]);
            if (IsTruthy(callResult))
            {
                result.Add(list[i]);
            }
        }
        return result;
    }

    public static void ArrayForEach(List<object?> list, object? callback)
    {
        for (int i = 0; i < list.Count; i++)
        {
            InvokeValue(callback, [list[i], (double)i, list]);
        }
    }

    #endregion

    #region Objects

    public static void MergeIntoObject(Dictionary<string, object?> target, object? source)
    {
        if (source is Dictionary<string, object?> dict)
        {
            foreach (var kv in dict)
            {
                target[kv.Key] = kv.Value;
            }
        }
        else if (source != null)
        {
            // For class instances, get their fields
            var type = source.GetType();
            var field = type.GetField("_fields", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null && field.GetValue(source) is Dictionary<string, object> fields)
            {
                foreach (var kv in fields)
                {
                    target[kv.Key] = kv.Value;
                }
            }
        }
    }

    public static Dictionary<string, object?> CreateObject(Dictionary<string, object> fields)
    {
        var result = new Dictionary<string, object?>();
        foreach (var kv in fields)
        {
            result[kv.Key] = kv.Value;
        }
        return result;
    }

    public static object? GetProperty(object? obj, string name)
    {
        if (obj == null) return null;

        // Dictionary (object literal)
        if (obj is Dictionary<string, object?> dict)
        {
            return dict.TryGetValue(name, out var value) ? value : null;
        }

        // List (array)
        if (obj is List<object?> list)
        {
            return name == "length" ? (double)list.Count : null;
        }

        // String
        if (obj is string s)
        {
            return name == "length" ? (double)s.Length : null;
        }

        // Class instance - use reflection
        var type = obj.GetType();

        // Check for getter method first (get_<propertyName>)
        var getterMethod = type.GetMethod($"get_{name}");
        if (getterMethod != null)
        {
            return getterMethod.Invoke(obj, null);
        }

        var field = type.GetField("_fields", BindingFlags.NonPublic | BindingFlags.Instance);
        if (field != null)
        {
            var fields = field.GetValue(obj) as Dictionary<string, object>;
            if (fields != null && fields.TryGetValue(name, out var value))
            {
                return value;
            }
        }

        // Try method
        var method = type.GetMethod(name);
        if (method != null)
        {
            return CreateBoundMethod(obj, method);
        }

        return null;
    }

    public static void SetProperty(object? obj, string name, object? value)
    {
        if (obj == null) return;

        // Dictionary
        if (obj is Dictionary<string, object?> dict)
        {
            dict[name] = value;
            return;
        }

        // Class instance
        var type = obj.GetType();

        // Check for setter method first (set_<propertyName>)
        var setterMethod = type.GetMethod($"set_{name}");
        if (setterMethod != null)
        {
            setterMethod.Invoke(obj, [value]);
            return;
        }

        var field = type.GetField("_fields", BindingFlags.NonPublic | BindingFlags.Instance);
        if (field != null)
        {
            var fields = field.GetValue(obj) as Dictionary<string, object>;
            if (fields != null)
            {
                fields[name] = value!;
            }
        }
    }

    public static object? GetIndex(object? obj, object? index)
    {
        if (obj == null) return null;

        // Object/Dictionary with string key
        if (obj is Dictionary<string, object?> dict && index is string key)
        {
            return dict.TryGetValue(key, out var value) ? value : null;
        }

        // Number key on dictionary (convert to string)
        if (obj is Dictionary<string, object?> numDict && index is double numKey)
        {
            return numDict.TryGetValue(numKey.ToString(), out var numValue) ? numValue : null;
        }

        // Symbol key - use separate storage
        if (index != null && IsSymbol(index))
        {
            var symbolDict = GetSymbolDict(obj);
            return symbolDict.TryGetValue(index, out var symValue) ? symValue : null;
        }

        // Numeric index for arrays/strings
        if (index is double or int or long)
        {
            int idx = (int)ToNumber(index);

            if (obj is List<object?> list && idx >= 0 && idx < list.Count)
            {
                return list[idx];
            }

            if (obj is string s && idx >= 0 && idx < s.Length)
            {
                return s[idx].ToString();
            }
        }

        return null;
    }

    private static bool IsSymbol(object obj)
    {
        return obj.GetType().Name == "TSSymbol" || obj.GetType().Name == "$TSSymbol";
    }

    public static void SetIndex(object? obj, object? index, object? value)
    {
        if (obj == null) return;

        // Object/Dictionary with string key
        if (obj is Dictionary<string, object?> dict && index is string key)
        {
            dict[key] = value;
            return;
        }

        // Number key on dictionary (convert to string)
        if (obj is Dictionary<string, object?> numDict && index is double numKey)
        {
            numDict[numKey.ToString()] = value;
            return;
        }

        // Symbol key - use separate storage
        if (index != null && IsSymbol(index))
        {
            var symbolDict = GetSymbolDict(obj);
            symbolDict[index] = value;
            return;
        }

        // Numeric index for arrays
        if (index is double or int or long)
        {
            int idx = (int)ToNumber(index);

            if (obj is List<object?> list && idx >= 0 && idx < list.Count)
            {
                list[idx] = value;
            }
        }
    }

    #endregion

    #region Methods

    public static object? InvokeMethod(object? receiver, string methodName, int argCount)
    {
        // This is a simplified implementation
        // In practice, arguments would need to be passed differently
        if (receiver == null) return null;

        // Array methods
        if (receiver is List<object?> list)
        {
            return methodName switch
            {
                "push" => null, // Would need args
                "pop" => list.Count > 0 ? list[^1] : null,
                "length" => (double)list.Count,
                _ => null
            };
        }

        // String methods
        if (receiver is string s)
        {
            return methodName switch
            {
                "toUpperCase" => s.ToUpperInvariant(),
                "toLowerCase" => s.ToLowerInvariant(),
                "trim" => s.Trim(),
                "length" => (double)s.Length,
                _ => null
            };
        }

        return null;
    }

    public static object? InvokeValue(object? value, object?[] args)
    {
        // If value is a TSFunction, call its Invoke method
        if (value is TSFunction func)
        {
            return func.Invoke(args);
        }

        // If value is a bound method (from CreateBoundMethod)
        if (value is Func<object?[], object?> boundMethod)
        {
            return boundMethod(args);
        }

        // Handle Delegate types (for bound methods created dynamically)
        if (value is Delegate del)
        {
            return del.DynamicInvoke(new object[] { args });
        }

        // If value is null, return null
        if (value == null)
        {
            return null;
        }

        // For other callable types (shouldn't normally happen)
        throw new InvalidOperationException($"Cannot invoke value of type {value.GetType().Name}");
    }

    private static object CreateBoundMethod(object receiver, MethodInfo method)
    {
        // Create a delegate bound to the receiver
        return new Func<object?[], object?>(args =>
        {
            return method.Invoke(receiver, args);
        });
    }

    public static object? GetSuperMethod(object? instance, string methodName)
    {
        if (instance == null) return null;

        var type = instance.GetType();
        var baseType = type.BaseType;
        if (baseType == null || baseType == typeof(object)) return null;

        var method = baseType.GetMethod(methodName);
        if (method != null)
        {
            return CreateBoundMethod(instance, method);
        }

        return null;
    }

    #endregion

    #region Instantiation

    public static object? CreateInstance(object?[] args, string className)
    {
        if (_compiledTypes.TryGetValue(className, out var type))
        {
            try
            {
                // Find the constructor and pad args with nulls for default parameters
                var ctors = type.GetConstructors();
                if (ctors.Length > 0)
                {
                    var ctor = ctors[0];
                    var paramCount = ctor.GetParameters().Length;

                    if (args.Length < paramCount)
                    {
                        var paddedArgs = new object?[paramCount];
                        Array.Copy(args, paddedArgs, args.Length);
                        return ctor.Invoke(paddedArgs);
                    }
                    return ctor.Invoke(args);
                }
                return Activator.CreateInstance(type);
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    #endregion

    #region Exceptions

    public static Exception CreateException(object? value)
    {
        return new Exception(Stringify(value));
    }

    public static object WrapException(Exception ex)
    {
        return new Dictionary<string, object?>
        {
            ["message"] = ex.Message,
            ["name"] = ex.GetType().Name
        };
    }

    #endregion

    #region Math

    public static double Random()
    {
        return _random.NextDouble();
    }

    #endregion

    #region Enums

    // Cache for enum reverse mappings: enumName -> (value -> memberName)
    private static readonly Dictionary<string, Dictionary<double, string>> _enumReverseCache = [];

    /// <summary>
    /// Get enum member name by value with caching.
    /// Keys and values arrays define the reverse mapping (passed once, cached by enumName).
    /// </summary>
    public static string GetEnumMemberName(string enumName, double value, double[] keys, string[] values)
    {
        if (!_enumReverseCache.TryGetValue(enumName, out var reverse))
        {
            reverse = new Dictionary<double, string>();
            for (int i = 0; i < keys.Length; i++)
            {
                reverse[keys[i]] = values[i];
            }
            _enumReverseCache[enumName] = reverse;
        }

        return reverse.TryGetValue(value, out var name) ? name : throw new Exception($"Value {value} not found in enum '{enumName}'");
    }

    #endregion

    #region Template Literals

    public static string ConcatTemplate(object?[] parts)
    {
        return string.Concat(parts.Select(Stringify));
    }

    #endregion

    #region Object Methods

    public static List<object?> GetValues(object? obj)
    {
        if (obj is Dictionary<string, object?> dict)
        {
            return dict.Values.ToList();
        }
        return [];
    }

    public static List<object?> GetEntries(object? obj)
    {
        if (obj is Dictionary<string, object?> dict)
        {
            return dict.Select(kv => (object?)new List<object?> { kv.Key, kv.Value }).ToList();
        }
        return [];
    }

    #endregion

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

    #region Type Emission

    public static void EmitAll(ModuleBuilder moduleBuilder)
    {
        // Runtime types are static methods in this class
        // No additional types need to be emitted - we use the existing RuntimeTypes class
    }

    #endregion
}

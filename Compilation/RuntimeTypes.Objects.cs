using System.Reflection;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

public static partial class RuntimeTypes
{
    #region Objects

    // Helper to safely cast object to Dictionary<string, object?>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryGetDictionary(object? obj, out Dictionary<string, object?>? dict)
    {
        if (obj is Dictionary<string, object?> d)
        {
            dict = d;
            return true;
        }
        dict = null;
        return false;
    }

    // Helper to get _fields from a class instance using ReflectionCache
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Dictionary<string, object?>? TryGetFields(object obj)
    {
        // Try to get cached field info
        var field = ReflectionCache.GetField(obj.GetType(), "_fields");
        if (field != null)
        {
            var val = field.GetValue(obj);
            // Safe unsafe cast because we only read/write compatible types (object)
            // _fields is emitted as Dictionary<string, object>, we treat it as Dictionary<string, object?>
            // effectively the same at runtime for reference types
            return Unsafe.As<Dictionary<string, object?>>(val);
        }
        return null;
    }

    public static void MergeIntoObject(Dictionary<string, object?> target, object? source)
    {
        if (TryGetDictionary(source, out var dict))
        {
            foreach (var kv in dict!)
            {
                target[kv.Key] = kv.Value;
            }
        }
        else if (source != null)
        {
            // For class instances, get their fields
            if (TryGetFields(source) is { } fields)
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
        Dictionary<string, object?> result = [];
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
        if (TryGetDictionary(obj, out var dict))
        {
            if (dict!.TryGetValue(name, out var value))
            {
                // If it's a TSFunction with a display class, bind 'this' to the dictionary
                // This handles object method shorthand: { fn() { return this.x; } }
                if (value is TSFunction func)
                {
                    func.BindThis(dict);
                }
                return value;
            }
            return null;
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

        // Check for getter method first (get_<PascalCaseName>)
        var getterMethod = ReflectionCache.GetGetter(type, name);
        if (getterMethod != null)
        {
            var invoker = ReflectionCache.GetInvoker(getterMethod);
            return invoker.Invoke(obj, default(Span<object?>));
        }

        // Check _fields dictionary
        if (TryGetFields(obj) is { } fields && fields.TryGetValue(name, out var val))
        {
            return val;
        }

        // Try method
        var method = ReflectionCache.GetMethod(type, name);
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
        if (TryGetDictionary(obj, out var dict))
        {
            dict![name] = value;
            return;
        }

        // Class instance
        var type = obj.GetType();

        // Check for setter method first (set_<PascalCaseName>)
        var setterMethod = ReflectionCache.GetSetter(type, name);
        if (setterMethod != null)
        {
            var invoker = ReflectionCache.GetInvoker(setterMethod);
            invoker.Invoke(obj, new Span<object?>([value]));
            return;
        }

        // Check _fields dictionary
        if (TryGetFields(obj) is { } fields)
        {
            fields[name] = value!;
        }
    }

    public static object? GetIndex(object? obj, object? index)
    {
        if (obj == null) return null;

        // Object/Dictionary with string key
        if (TryGetDictionary(obj, out var dict) && index is string key)
        {
            if (dict!.TryGetValue(key, out var value))
            {
                // Bind 'this' for object method shorthand functions
                if (value is TSFunction func)
                {
                    func.BindThis(dict);
                }
                return value;
            }
            return null;
        }

        // Number key on dictionary (convert to string)
        if (TryGetDictionary(obj, out var numDict) && index is double numKey)
        {
            return numDict!.TryGetValue(numKey.ToString(), out var numValue) ? numValue : null;
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
        // Optimized check
        var name = obj.GetType().Name;
        return name is "TSSymbol" or "$TSSymbol";
    }

    public static void SetIndex(object? obj, object? index, object? value)
    {
        if (obj == null) return;

        // Object/Dictionary with string key
        if (TryGetDictionary(obj, out var dict) && index is string key)
        {
            dict![key] = value;
            return;
        }

        // Number key on dictionary (convert to string)
        if (TryGetDictionary(obj, out var numDict) && index is double numKey)
        {
            numDict![numKey.ToString()] = value;
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

    #region Object Methods

    public static List<object?> GetValues(object? obj)
    {
        if (TryGetDictionary(obj, out var dict))
        {
            return dict!.Values.ToList();
        }
        // For compiled class instances, get values from typed backing fields AND _fields dictionary
        if (obj != null)
        {
            List<object?> values = [];
            var type = obj.GetType();
            HashSet<string> seenKeys = [];

            // Get values from typed backing fields (fields starting with __)
            foreach (var backingField in ReflectionCache.GetBackingFields(type))
            {
                string propName = backingField.Name[2..];
                seenKeys.Add(propName);
                values.Add(backingField.GetValue(obj));
            }

            // Also get values from _fields dictionary (for dynamic properties and generic type fields)
            if (TryGetFields(obj) is { } fields)
            {
                foreach (var kv in fields)
                {
                    if (!seenKeys.Contains(kv.Key))
                    {
                        values.Add(kv.Value);
                    }
                }
            }

            return values;
        }
        return [];
    }

    public static List<object?> GetEntries(object? obj)
    {
        if (TryGetDictionary(obj, out var dict))
        {
            return dict!.Select(kv => (object?)new List<object?> { kv.Key, kv.Value }).ToList();
        }
        // For compiled class instances, get entries from typed backing fields AND _fields dictionary
        if (obj != null)
        {
            List<object?> entries = [];
            var type = obj.GetType();
            HashSet<string> seenKeys = [];

            // Get entries from typed backing fields (fields starting with __)
            foreach (var backingField in ReflectionCache.GetBackingFields(type))
            {
                string propName = backingField.Name[2..];
                seenKeys.Add(propName);
                entries.Add(new List<object?> { propName, backingField.GetValue(obj) });
            }

            // Also get entries from _fields dictionary (for dynamic properties and generic type fields)
            if (TryGetFields(obj) is { } fields)
            {
                foreach (var kv in fields)
                {
                    if (!seenKeys.Contains(kv.Key))
                    {
                        entries.Add(new List<object?> { kv.Key, kv.Value });
                    }
                }
            }

            return entries;
        }
        return [];
    }

    #endregion
}
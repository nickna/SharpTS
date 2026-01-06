using System.Reflection;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

public static partial class RuntimeTypes
{
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

    #region Object Methods

    public static List<object?> GetValues(object? obj)
    {
        if (obj is Dictionary<string, object?> dict)
        {
            return dict.Values.ToList();
        }
        // For compiled class instances, get values from _fields
        if (obj != null)
        {
            var type = obj.GetType();
            var field = type.GetField("_fields", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null && field.GetValue(obj) is Dictionary<string, object?> fields)
            {
                return fields.Values.ToList();
            }
        }
        return [];
    }

    public static List<object?> GetEntries(object? obj)
    {
        if (obj is Dictionary<string, object?> dict)
        {
            return dict.Select(kv => (object?)new List<object?> { kv.Key, kv.Value }).ToList();
        }
        // For compiled class instances, get entries from _fields
        if (obj != null)
        {
            var type = obj.GetType();
            var field = type.GetField("_fields", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null && field.GetValue(obj) is Dictionary<string, object?> fields)
            {
                return fields.Select(kv => (object?)new List<object?> { kv.Key, kv.Value }).ToList();
            }
        }
        return [];
    }

    #endregion
}

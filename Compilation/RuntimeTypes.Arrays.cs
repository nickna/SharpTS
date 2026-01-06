using System.Reflection;

namespace SharpTS.Compilation;

public static partial class RuntimeTypes
{
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
            var field = type.GetField("_fields", BindingFlags.NonPublic | BindingFlags.Instance);
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
}

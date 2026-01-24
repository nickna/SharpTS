using SharpTS.Execution;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

public static class ArrayBuiltIns
{
    // Cache unbound methods to avoid allocation of the delegate and method definition on every access
    private static readonly BuiltInMethod _push = new("push", 1, int.MaxValue, Push); // variadic: push(item1, item2, ...)
    private static readonly BuiltInMethod _pop = new("pop", 0, Pop);
    private static readonly BuiltInMethod _shift = new("shift", 0, Shift);
    private static readonly BuiltInMethod _unshift = new("unshift", 1, Unshift);
    private static readonly BuiltInMethod _slice = new("slice", 0, 2, Slice);
    private static readonly BuiltInMethod _map = new("map", 1, Map);
    private static readonly BuiltInMethod _filter = new("filter", 1, Filter);
    private static readonly BuiltInMethod _forEach = new("forEach", 1, ForEach);
    private static readonly BuiltInMethod _find = new("find", 1, Find);
    private static readonly BuiltInMethod _findIndex = new("findIndex", 1, FindIndex);
    private static readonly BuiltInMethod _some = new("some", 1, Some);
    private static readonly BuiltInMethod _every = new("every", 1, Every);
    private static readonly BuiltInMethod _reduce = new("reduce", 1, 2, Reduce);
    private static readonly BuiltInMethod _includes = new("includes", 1, Includes);
    private static readonly BuiltInMethod _indexOf = new("indexOf", 1, IndexOf);
    private static readonly BuiltInMethod _join = new("join", 0, 1, Join);
    private static readonly BuiltInMethod _concat = new("concat", 1, Concat);
    private static readonly BuiltInMethod _reverse = new("reverse", 0, Reverse);
    private static readonly BuiltInMethod _flat = new("flat", 0, 1, Flat);
    private static readonly BuiltInMethod _flatMap = new("flatMap", 1, FlatMap);
    private static readonly BuiltInMethod _sort = new("sort", 0, 1, Sort);
    private static readonly BuiltInMethod _toSorted = new("toSorted", 0, 1, ToSorted);
    private static readonly BuiltInMethod _splice = new("splice", 0, int.MaxValue, Splice);
    private static readonly BuiltInMethod _toSpliced = new("toSpliced", 0, int.MaxValue, ToSpliced);

    public static object? GetMember(SharpTSArray receiver, string name)
    {
        return name switch
        {
            "length" => (double)receiver.Elements.Count,

            "push" => _push.Bind(receiver),
            "pop" => _pop.Bind(receiver),
            "shift" => _shift.Bind(receiver),
            "unshift" => _unshift.Bind(receiver),
            "slice" => _slice.Bind(receiver),
            "map" => _map.Bind(receiver),
            "filter" => _filter.Bind(receiver),
            "forEach" => _forEach.Bind(receiver),
            "find" => _find.Bind(receiver),
            "findIndex" => _findIndex.Bind(receiver),
            "some" => _some.Bind(receiver),
            "every" => _every.Bind(receiver),
            "reduce" => _reduce.Bind(receiver),
            "includes" => _includes.Bind(receiver),
            "indexOf" => _indexOf.Bind(receiver),
            "join" => _join.Bind(receiver),
            "concat" => _concat.Bind(receiver),
            "reverse" => _reverse.Bind(receiver),
            "flat" => _flat.Bind(receiver),
            "flatMap" => _flatMap.Bind(receiver),
            "sort" => _sort.Bind(receiver),
            "toSorted" => _toSorted.Bind(receiver),
            "splice" => _splice.Bind(receiver),
            "toSpliced" => _toSpliced.Bind(receiver),

            _ => null
        };
    }

    private static object? Push(Interpreter i, object? r, List<object?> args)
    {
        var arr = (SharpTSArray)r!;
        // Frozen/sealed arrays cannot have elements added
        if (arr.IsFrozen || arr.IsSealed)
        {
            return (double)arr.Elements.Count;
        }
        // Add all arguments (variadic push)
        foreach (var arg in args)
        {
            arr.Elements.Add(arg);
        }
        return (double)arr.Elements.Count;
    }

    private static object? Pop(Interpreter i, object? r, List<object?> args)
    {
        var arr = (SharpTSArray)r!;
        // Frozen/sealed arrays cannot have elements removed
        if (arr.IsFrozen || arr.IsSealed)
        {
            return null;
        }
        if (arr.Elements.Count == 0) return null;
        var last = arr.Elements[^1];
        arr.Elements.RemoveAt(arr.Elements.Count - 1);
        return last;
    }

    private static object? Shift(Interpreter i, object? r, List<object?> args)
    {
        var arr = (SharpTSArray)r!;
        // Frozen/sealed arrays cannot have elements removed
        if (arr.IsFrozen || arr.IsSealed)
        {
            return null;
        }
        if (arr.Elements.Count == 0) return null;
        var first = arr.Elements[0];
        arr.Elements.RemoveAt(0);
        return first;
    }

    private static object? Unshift(Interpreter i, object? r, List<object?> args)
    {
        var arr = (SharpTSArray)r!;
        // Frozen/sealed arrays cannot have elements added
        if (arr.IsFrozen || arr.IsSealed)
        {
            return (double)arr.Elements.Count;
        }
        arr.Elements.Insert(0, args[0]);
        return (double)arr.Elements.Count;
    }

    private static object? Slice(Interpreter i, object? r, List<object?> args)
    {
        var arr = (SharpTSArray)r!;
        var start = args.Count > 0 ? (int)(double)args[0]! : 0;
        var end = args.Count > 1 ? (int)(double)args[1]! : arr.Elements.Count;

        // Handle negative indices
        if (start < 0) start = Math.Max(0, arr.Elements.Count + start);
        if (end < 0) end = Math.Max(0, arr.Elements.Count + end);
        if (start > arr.Elements.Count) start = arr.Elements.Count;
        if (end > arr.Elements.Count) end = arr.Elements.Count;
        if (end <= start) return new SharpTSArray([]);

        var sliced = arr.Elements.GetRange(start, end - start);
        return new SharpTSArray(new List<object?>(sliced));
    }

    private static object? Map(Interpreter interp, object? r, List<object?> args)
    {
        var arr = (SharpTSArray)r!;
        var callback = args[0] as ISharpTSCallable
            ?? throw new Exception("Runtime Error: map requires a function argument.");

        List<object?> result = [];
        var callbackArgs = new List<object?>(3) { null, null, arr };
        for (int i = 0; i < arr.Elements.Count; i++)
        {
            callbackArgs[0] = arr.Elements[i];
            callbackArgs[1] = (double)i;
            var callResult = callback.Call(interp, callbackArgs);
            result.Add(callResult);
        }
        return new SharpTSArray(result);
    }

    private static object? Filter(Interpreter interp, object? r, List<object?> args)
    {
        var arr = (SharpTSArray)r!;
        var callback = args[0] as ISharpTSCallable
            ?? throw new Exception("Runtime Error: filter requires a function argument.");

        List<object?> result = [];
        var callbackArgs = new List<object?>(3) { null, null, arr };
        for (int i = 0; i < arr.Elements.Count; i++)
        {
            callbackArgs[0] = arr.Elements[i];
            callbackArgs[1] = (double)i;
            var callResult = callback.Call(interp, callbackArgs);
            if (IsTruthy(callResult))
            {
                result.Add(arr.Elements[i]);
            }
        }
        return new SharpTSArray(result);
    }

    private static object? ForEach(Interpreter interp, object? r, List<object?> args)
    {
        var arr = (SharpTSArray)r!;
        var callback = args[0] as ISharpTSCallable
            ?? throw new Exception("Runtime Error: forEach requires a function argument.");

        var callbackArgs = new List<object?>(3) { null, null, arr };
        for (int i = 0; i < arr.Elements.Count; i++)
        {
            callbackArgs[0] = arr.Elements[i];
            callbackArgs[1] = (double)i;
            callback.Call(interp, callbackArgs);
        }
        return null;
    }

    private static object? Find(Interpreter interp, object? r, List<object?> args)
    {
        var arr = (SharpTSArray)r!;
        var callback = args[0] as ISharpTSCallable
            ?? throw new Exception("Runtime Error: find requires a function argument.");

        var callbackArgs = new List<object?>(3) { null, null, arr };
        for (int i = 0; i < arr.Elements.Count; i++)
        {
            callbackArgs[0] = arr.Elements[i];
            callbackArgs[1] = (double)i;
            var result = callback.Call(interp, callbackArgs);
            if (IsTruthy(result))
            {
                return arr.Elements[i];
            }
        }
        return null;
    }

    private static object? FindIndex(Interpreter interp, object? r, List<object?> args)
    {
        var arr = (SharpTSArray)r!;
        var callback = args[0] as ISharpTSCallable
            ?? throw new Exception("Runtime Error: findIndex requires a function argument.");

        var callbackArgs = new List<object?>(3) { null, null, arr };
        for (int i = 0; i < arr.Elements.Count; i++)
        {
            callbackArgs[0] = arr.Elements[i];
            callbackArgs[1] = (double)i;
            var result = callback.Call(interp, callbackArgs);
            if (IsTruthy(result))
            {
                return (double)i;
            }
        }
        return -1.0;
    }

    private static object? Some(Interpreter interp, object? r, List<object?> args)
    {
        var arr = (SharpTSArray)r!;
        var callback = args[0] as ISharpTSCallable
            ?? throw new Exception("Runtime Error: some requires a function argument.");

        var callbackArgs = new List<object?>(3) { null, null, arr };
        for (int i = 0; i < arr.Elements.Count; i++)
        {
            callbackArgs[0] = arr.Elements[i];
            callbackArgs[1] = (double)i;
            var result = callback.Call(interp, callbackArgs);
            if (IsTruthy(result))
            {
                return true;
            }
        }
        return false;
    }

    private static object? Every(Interpreter interp, object? r, List<object?> args)
    {
        var arr = (SharpTSArray)r!;
        var callback = args[0] as ISharpTSCallable
            ?? throw new Exception("Runtime Error: every requires a function argument.");

        var callbackArgs = new List<object?>(3) { null, null, arr };
        for (int i = 0; i < arr.Elements.Count; i++)
        {
            callbackArgs[0] = arr.Elements[i];
            callbackArgs[1] = (double)i;
            var result = callback.Call(interp, callbackArgs);
            if (!IsTruthy(result))
            {
                return false;
            }
        }
        return true;
    }

    private static object? Reduce(Interpreter interp, object? r, List<object?> args)
    {
        var arr = (SharpTSArray)r!;
        var callback = args[0] as ISharpTSCallable
            ?? throw new Exception("Runtime Error: reduce requires a function argument.");

        int startIndex = 0;
        object? accumulator;

        if (args.Count > 1)
        {
            accumulator = args[1];
        }
        else
        {
            if (arr.Elements.Count == 0)
            {
                throw new Exception("Runtime Error: reduce of empty array with no initial value.");
            }
            accumulator = arr.Elements[0];
            startIndex = 1;
        }

        var callbackArgs = new List<object?>(4) { null, null, null, arr };
        for (int i = startIndex; i < arr.Elements.Count; i++)
        {
            callbackArgs[0] = accumulator;
            callbackArgs[1] = arr.Elements[i];
            callbackArgs[2] = (double)i;
            accumulator = callback.Call(interp, callbackArgs);
        }
        return accumulator;
    }

    private static object? Includes(Interpreter i, object? r, List<object?> args)
    {
        var arr = (SharpTSArray)r!;
        var searchElement = args[0];

        foreach (var element in arr.Elements)
        {
            if (IsEqual(element, searchElement))
            {
                return true;
            }
        }
        return false;
    }

    private static object? IndexOf(Interpreter i, object? r, List<object?> args)
    {
        var arr = (SharpTSArray)r!;
        var searchElement = args[0];

        for (int idx = 0; idx < arr.Elements.Count; idx++)
        {
            if (IsEqual(arr.Elements[idx], searchElement))
            {
                return (double)idx;
            }
        }
        return -1.0;
    }

    private static object? Join(Interpreter i, object? r, List<object?> args)
    {
        var arr = (SharpTSArray)r!;
        var separator = args.Count > 0 ? Stringify(args[0]) : ",";

        var parts = arr.Elements.Select(e => Stringify(e));
        return string.Join(separator, parts);
    }

    private static object? Concat(Interpreter i, object? r, List<object?> args)
    {
        var arr = (SharpTSArray)r!;
        var result = new List<object?>(arr.Elements);

        if (args[0] is SharpTSArray otherArr)
        {
            result.AddRange(otherArr.Elements);
        }
        else
        {
            result.Add(args[0]);
        }

        return new SharpTSArray(result);
    }

    private static object? Reverse(Interpreter i, object? r, List<object?> args)
    {
        var arr = (SharpTSArray)r!;
        // Frozen arrays cannot be modified; sealed arrays allow in-place modifications
        if (arr.IsFrozen)
        {
            return arr;
        }
        arr.Elements.Reverse();
        return arr;
    }

    private static object? Flat(Interpreter i, object? r, List<object?> args)
    {
        var arr = (SharpTSArray)r!;
        // Default depth is 1, handle Infinity for complete flatten
        var depth = args.Count > 0 && args[0] is double d
            ? (double.IsPositiveInfinity(d) ? int.MaxValue : (int)d)
            : 1;

        var result = new List<object?>();
        FlattenRecursive(arr.Elements, result, depth);
        return new SharpTSArray(result);
    }

    private static void FlattenRecursive(List<object?> source, List<object?> result, int depth)
    {
        foreach (var item in source)
        {
            if (depth > 0 && item is SharpTSArray nestedArray)
            {
                FlattenRecursive(nestedArray.Elements, result, depth - 1);
            }
            else
            {
                result.Add(item);
            }
        }
    }

    private static object? FlatMap(Interpreter interp, object? r, List<object?> args)
    {
        var arr = (SharpTSArray)r!;
        var callback = args[0] as ISharpTSCallable
            ?? throw new Exception("Runtime Error: flatMap requires a function argument.");

        var result = new List<object?>();
        var callbackArgs = new List<object?>(3) { null, null, arr };
        for (int i = 0; i < arr.Elements.Count; i++)
        {
            callbackArgs[0] = arr.Elements[i];
            callbackArgs[1] = (double)i;
            var callResult = callback.Call(interp, callbackArgs);

            // flatMap flattens by 1 level only
            if (callResult is SharpTSArray mappedArray)
            {
                result.AddRange(mappedArray.Elements);
            }
            else
            {
                result.Add(callResult);
            }
        }
        return new SharpTSArray(result);
    }

    private static object? Sort(Interpreter interp, object? r, List<object?> args)
    {
        var arr = (SharpTSArray)r!;
        // Frozen arrays cannot be modified; silent fail (matches reverse behavior)
        if (arr.IsFrozen) return arr;

        ISharpTSCallable? compareFn = args.Count > 0 ? args[0] as ISharpTSCallable : null;

        // Partition undefined to end (JS behavior)
        var defined = new List<(object? Element, int Index)>();
        int undefinedCount = 0;
        for (int i = 0; i < arr.Elements.Count; i++)
        {
            if (IsUndefined(arr.Elements[i]))
                undefinedCount++;
            else
                defined.Add((arr.Elements[i], i));
        }

        var sorted = StableSort(defined, compareFn, interp);

        arr.Elements.Clear();
        arr.Elements.AddRange(sorted);
        for (int i = 0; i < undefinedCount; i++)
            arr.Elements.Add(SharpTSUndefined.Instance);

        return arr;
    }

    private static object? ToSorted(Interpreter interp, object? r, List<object?> args)
    {
        var arr = (SharpTSArray)r!;
        ISharpTSCallable? compareFn = args.Count > 0 ? args[0] as ISharpTSCallable : null;

        // Same logic but returns NEW array
        var defined = new List<(object? Element, int Index)>();
        int undefinedCount = 0;
        for (int i = 0; i < arr.Elements.Count; i++)
        {
            if (IsUndefined(arr.Elements[i]))
                undefinedCount++;
            else
                defined.Add((arr.Elements[i], i));
        }

        var sorted = StableSort(defined, compareFn, interp);
        for (int i = 0; i < undefinedCount; i++)
            sorted.Add(SharpTSUndefined.Instance);

        return new SharpTSArray(sorted);
    }

    /// <summary>
    /// Performs a stable sort using LINQ OrderBy (which is stable).
    /// </summary>
    private static List<object?> StableSort(
        List<(object? Element, int Index)> items,
        ISharpTSCallable? compareFn,
        Interpreter interp)
    {
        if (items.Count <= 1)
            return items.Select(x => x.Element).ToList();

        IEnumerable<(object? Element, int Index)> sorted;
        if (compareFn != null)
        {
            sorted = items.OrderBy(x => x, new CompareFnComparer(compareFn, interp));
        }
        else
        {
            // Default lexicographic sort (JavaScript behavior: numbers sorted as strings)
            sorted = items.OrderBy(x => Stringify(x.Element), StringComparer.Ordinal)
                          .ThenBy(x => x.Index);
        }

        return sorted.Select(x => x.Element).ToList();
    }

    /// <summary>
    /// Comparer that uses a user-provided comparison function.
    /// </summary>
    private class CompareFnComparer : IComparer<(object? Element, int Index)>
    {
        private readonly ISharpTSCallable _fn;
        private readonly Interpreter _interp;
        private readonly List<object?> _compareArgs = new(2) { null, null };

        public CompareFnComparer(ISharpTSCallable fn, Interpreter interp)
            => (_fn, _interp) = (fn, interp);

        public int Compare((object? Element, int Index) x, (object? Element, int Index) y)
        {
            _compareArgs[0] = x.Element;
            _compareArgs[1] = y.Element;
            var result = _fn.Call(_interp, _compareArgs);
            if (result is double d && !double.IsNaN(d) && d != 0)
                return d < 0 ? -1 : 1;
            // Stability tie-breaker: preserve original order
            return x.Index.CompareTo(y.Index);
        }
    }

    /// <summary>
    /// Implements JavaScript's ToIntegerOrInfinity algorithm (ECMA-262 7.1.5).
    /// Converts a value to an integer, handling NaN, Infinity, and null.
    /// </summary>
    private static int ToIntegerOrInfinity(object? value, int defaultValue)
    {
        if (value == null) return defaultValue;
        if (value is int i) return i;
        if (value is double d)
        {
            if (double.IsNaN(d)) return 0;
            if (double.IsPositiveInfinity(d)) return int.MaxValue;
            if (double.IsNegativeInfinity(d)) return int.MinValue;
            return (int)Math.Truncate(d);
        }
        return defaultValue;
    }

    private static object? Splice(Interpreter i, object? r, List<object?> args)
    {
        var arr = (SharpTSArray)r!;
        int len = arr.Elements.Count;

        // Frozen/sealed arrays throw TypeError
        if (arr.IsFrozen || arr.IsSealed)
            throw new Exception("TypeError: Cannot modify a frozen or sealed array");

        // If no arguments, return empty array (no elements deleted or inserted)
        if (args.Count == 0)
            return new SharpTSArray([]);

        // Parse start with negative handling (RelativeIndex to ActualIndex)
        int relStart = ToIntegerOrInfinity(args[0], 0);
        int actualStart = relStart < 0 ? Math.Max(len + relStart, 0) : Math.Min(relStart, len);

        // Parse deleteCount
        int actualDeleteCount;
        if (args.Count == 1)
        {
            // No deleteCount argument = delete to end
            actualDeleteCount = len - actualStart;
        }
        else
        {
            int dc = ToIntegerOrInfinity(args[1], 0);
            actualDeleteCount = Math.Max(0, Math.Min(dc, len - actualStart));
        }

        // Collect deleted elements
        var deleted = arr.Elements.GetRange(actualStart, actualDeleteCount);

        // Remove then insert
        arr.Elements.RemoveRange(actualStart, actualDeleteCount);
        if (args.Count > 2)
        {
            var itemsToInsert = args.Skip(2).ToList();
            arr.Elements.InsertRange(actualStart, itemsToInsert);
        }

        return new SharpTSArray(new List<object?>(deleted));
    }

    private static object? ToSpliced(Interpreter i, object? r, List<object?> args)
    {
        var arr = (SharpTSArray)r!;
        int len = arr.Elements.Count;

        // toSpliced works on frozen/sealed arrays (creates new array)

        // If no arguments, return a copy of the array
        if (args.Count == 0)
            return new SharpTSArray(new List<object?>(arr.Elements));

        // Parse start with negative handling
        int relStart = ToIntegerOrInfinity(args[0], 0);
        int actualStart = relStart < 0 ? Math.Max(len + relStart, 0) : Math.Min(relStart, len);

        // Parse skipCount (deleteCount equivalent)
        int actualSkipCount;
        if (args.Count == 1)
        {
            // No skipCount argument = skip to end
            actualSkipCount = len - actualStart;
        }
        else
        {
            int sc = ToIntegerOrInfinity(args[1], 0);
            actualSkipCount = Math.Max(0, Math.Min(sc, len - actualStart));
        }

        // Build new array: before + items + after
        var result = new List<object?>();
        result.AddRange(arr.Elements.Take(actualStart));
        if (args.Count > 2)
            result.AddRange(args.Skip(2));
        result.AddRange(arr.Elements.Skip(actualStart + actualSkipCount));

        return new SharpTSArray(result);
    }

    private static bool IsUndefined(object? obj)
    {
        return obj is SharpTSUndefined;
    }

    private static bool IsTruthy(object? obj)
    {
        if (obj == null) return false;
        if (obj is bool b) return b;
        return true;
    }

    private static bool IsEqual(object? a, object? b)
    {
        if (a == null && b == null) return true;
        if (a == null) return false;
        return a.Equals(b);
    }

    private static string Stringify(object? obj)
    {
        if (obj == null) return "null";
        if (obj is double d)
        {
            string text = d.ToString();
            if (text.EndsWith(".0"))
            {
                text = text[..^2];
            }
            return text;
        }
        if (obj is bool b) return b ? "true" : "false";
        return obj.ToString() ?? "null";
    }
}

using SharpTS.Execution;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

public static class ArrayBuiltIns
{
    private static readonly BuiltInTypeMemberLookup<SharpTSArray> _lookup =
        BuiltInTypeBuilder<SharpTSArray>.ForInstanceType()
            .Property("length", arr => (double)arr.Elements.Count)
            .Method("push", 1, int.MaxValue, Push)
            .Method("pop", 0, Pop)
            .Method("shift", 0, Shift)
            .Method("unshift", 1, Unshift)
            .Method("slice", 0, 2, Slice)
            .Method("map", 1, Map)
            .Method("filter", 1, Filter)
            .Method("forEach", 1, ForEach)
            .Method("find", 1, Find)
            .Method("findIndex", 1, FindIndex)
            .Method("some", 1, Some)
            .Method("every", 1, Every)
            .Method("reduce", 1, 2, Reduce)
            .Method("includes", 1, Includes)
            .Method("indexOf", 1, IndexOf)
            .Method("join", 0, 1, Join)
            .Method("concat", 1, Concat)
            .Method("reverse", 0, Reverse)
            .Method("flat", 0, 1, Flat)
            .Method("flatMap", 1, FlatMap)
            .Method("sort", 0, 1, Sort)
            .Method("toSorted", 0, 1, ToSorted)
            .Method("splice", 0, int.MaxValue, Splice)
            .Method("toSpliced", 0, int.MaxValue, ToSpliced)
            .Method("findLast", 1, FindLast)
            .Method("findLastIndex", 1, FindLastIndex)
            .Method("toReversed", 0, ToReversed)
            .Method("with", 2, With)
            .Build();

    public static object? GetMember(SharpTSArray receiver, string name)
        => _lookup.GetMember(receiver, name);

    private static object? Push(Interpreter _, SharpTSArray arr, List<object?> args)
    {
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

    private static object? Pop(Interpreter _, SharpTSArray arr, List<object?> args)
    {
        // Frozen/sealed arrays cannot have elements removed
        if (arr.IsFrozen || arr.IsSealed)
        {
            return null;
        }
        if (arr.Elements.Count == 0) return null;
        return arr.Elements.RemoveLast();  // O(1) with Deque
    }

    private static object? Shift(Interpreter _, SharpTSArray arr, List<object?> args)
    {
        // Frozen/sealed arrays cannot have elements removed
        if (arr.IsFrozen || arr.IsSealed)
        {
            return null;
        }
        if (arr.Elements.Count == 0) return null;
        return arr.Elements.RemoveFirst();  // O(1) with Deque
    }

    private static object? Unshift(Interpreter _, SharpTSArray arr, List<object?> args)
    {
        // Frozen/sealed arrays cannot have elements added
        if (arr.IsFrozen || arr.IsSealed)
        {
            return (double)arr.Elements.Count;
        }
        arr.Elements.AddFirst(args[0]);  // O(1) with Deque
        return (double)arr.Elements.Count;
    }

    private static object? Slice(Interpreter _, SharpTSArray arr, List<object?> args)
    {
        var start = args.Count > 0 ? (int)(double)args[0]! : 0;
        var end = args.Count > 1 ? (int)(double)args[1]! : arr.Elements.Count;

        // Handle negative indices
        if (start < 0) start = Math.Max(0, arr.Elements.Count + start);
        if (end < 0) end = Math.Max(0, arr.Elements.Count + end);
        if (start > arr.Elements.Count) start = arr.Elements.Count;
        if (end > arr.Elements.Count) end = arr.Elements.Count;
        if (end <= start) return new SharpTSArray([]);

        var sliced = arr.Elements.GetRange(start, end - start);
        return new SharpTSArray(new Deque<object?>(sliced));
    }

    private static object? Map(Interpreter interp, SharpTSArray arr, List<object?> args)
    {
        var callback = args[0] as ISharpTSCallable
            ?? throw new Exception("Runtime Error: map requires a function argument.");

        List<object?> result = [];
        var callbackArgs = ArgumentListPool.Rent();
        try
        {
            callbackArgs.Add(null);
            callbackArgs.Add(null);
            callbackArgs.Add(arr);
            for (int i = 0; i < arr.Elements.Count; i++)
            {
                callbackArgs[0] = arr.Elements[i];
                callbackArgs[1] = (double)i;
                var callResult = callback.Call(interp, callbackArgs);
                result.Add(callResult);
            }
            return new SharpTSArray(result);
        }
        finally
        {
            ArgumentListPool.Return(callbackArgs);
        }
    }

    private static object? Filter(Interpreter interp, SharpTSArray arr, List<object?> args)
    {
        var callback = args[0] as ISharpTSCallable
            ?? throw new Exception("Runtime Error: filter requires a function argument.");

        List<object?> result = [];
        var callbackArgs = ArgumentListPool.Rent();
        try
        {
            callbackArgs.Add(null);
            callbackArgs.Add(null);
            callbackArgs.Add(arr);
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
        finally
        {
            ArgumentListPool.Return(callbackArgs);
        }
    }

    private static object? ForEach(Interpreter interp, SharpTSArray arr, List<object?> args)
    {
        var callback = args[0] as ISharpTSCallable
            ?? throw new Exception("Runtime Error: forEach requires a function argument.");

        var callbackArgs = ArgumentListPool.Rent();
        try
        {
            callbackArgs.Add(null);
            callbackArgs.Add(null);
            callbackArgs.Add(arr);
            for (int i = 0; i < arr.Elements.Count; i++)
            {
                callbackArgs[0] = arr.Elements[i];
                callbackArgs[1] = (double)i;
                callback.Call(interp, callbackArgs);
            }
            return null;
        }
        finally
        {
            ArgumentListPool.Return(callbackArgs);
        }
    }

    private static object? Find(Interpreter interp, SharpTSArray arr, List<object?> args)
    {
        var callback = args[0] as ISharpTSCallable
            ?? throw new Exception("Runtime Error: find requires a function argument.");

        var callbackArgs = ArgumentListPool.Rent();
        try
        {
            callbackArgs.Add(null);
            callbackArgs.Add(null);
            callbackArgs.Add(arr);
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
        finally
        {
            ArgumentListPool.Return(callbackArgs);
        }
    }

    private static object? FindIndex(Interpreter interp, SharpTSArray arr, List<object?> args)
    {
        var callback = args[0] as ISharpTSCallable
            ?? throw new Exception("Runtime Error: findIndex requires a function argument.");

        var callbackArgs = ArgumentListPool.Rent();
        try
        {
            callbackArgs.Add(null);
            callbackArgs.Add(null);
            callbackArgs.Add(arr);
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
        finally
        {
            ArgumentListPool.Return(callbackArgs);
        }
    }

    private static object? Some(Interpreter interp, SharpTSArray arr, List<object?> args)
    {
        var callback = args[0] as ISharpTSCallable
            ?? throw new Exception("Runtime Error: some requires a function argument.");

        var callbackArgs = ArgumentListPool.Rent();
        try
        {
            callbackArgs.Add(null);
            callbackArgs.Add(null);
            callbackArgs.Add(arr);
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
        finally
        {
            ArgumentListPool.Return(callbackArgs);
        }
    }

    private static object? Every(Interpreter interp, SharpTSArray arr, List<object?> args)
    {
        var callback = args[0] as ISharpTSCallable
            ?? throw new Exception("Runtime Error: every requires a function argument.");

        var callbackArgs = ArgumentListPool.Rent();
        try
        {
            callbackArgs.Add(null);
            callbackArgs.Add(null);
            callbackArgs.Add(arr);
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
        finally
        {
            ArgumentListPool.Return(callbackArgs);
        }
    }

    private static object? Reduce(Interpreter interp, SharpTSArray arr, List<object?> args)
    {
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

        var callbackArgs = ArgumentListPool.Rent();
        try
        {
            callbackArgs.Add(null);
            callbackArgs.Add(null);
            callbackArgs.Add(null);
            callbackArgs.Add(arr);
            for (int i = startIndex; i < arr.Elements.Count; i++)
            {
                callbackArgs[0] = accumulator;
                callbackArgs[1] = arr.Elements[i];
                callbackArgs[2] = (double)i;
                accumulator = callback.Call(interp, callbackArgs);
            }
            return accumulator;
        }
        finally
        {
            ArgumentListPool.Return(callbackArgs);
        }
    }

    private static object? Includes(Interpreter _, SharpTSArray arr, List<object?> args)
    {
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

    private static object? IndexOf(Interpreter _, SharpTSArray arr, List<object?> args)
    {
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

    private static object? Join(Interpreter _, SharpTSArray arr, List<object?> args)
    {
        var separator = args.Count > 0 ? Stringify(args[0]) : ",";

        var parts = arr.Elements.Select(e => Stringify(e));
        return string.Join(separator, parts);
    }

    private static object? Concat(Interpreter _, SharpTSArray arr, List<object?> args)
    {
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

    private static object? Reverse(Interpreter _, SharpTSArray arr, List<object?> args)
    {
        // Frozen arrays cannot be modified; sealed arrays allow in-place modifications
        if (arr.IsFrozen)
        {
            return arr;
        }
        arr.Elements.Reverse();
        return arr;
    }

    private static object? Flat(Interpreter _, SharpTSArray arr, List<object?> args)
    {
        // Default depth is 1, handle Infinity for complete flatten
        var depth = args.Count > 0 && args[0] is double d
            ? (double.IsPositiveInfinity(d) ? int.MaxValue : (int)d)
            : 1;

        var result = new List<object?>();
        FlattenRecursive(arr.Elements, result, depth);
        return new SharpTSArray(result);
    }

    private static void FlattenRecursive(IEnumerable<object?> source, List<object?> result, int depth)
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

    private static object? FlatMap(Interpreter interp, SharpTSArray arr, List<object?> args)
    {
        var callback = args[0] as ISharpTSCallable
            ?? throw new Exception("Runtime Error: flatMap requires a function argument.");

        var result = new List<object?>();
        var callbackArgs = ArgumentListPool.Rent();
        try
        {
            callbackArgs.Add(null);
            callbackArgs.Add(null);
            callbackArgs.Add(arr);
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
        finally
        {
            ArgumentListPool.Return(callbackArgs);
        }
    }

    private static object? Sort(Interpreter interp, SharpTSArray arr, List<object?> args)
    {
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

    private static object? ToSorted(Interpreter interp, SharpTSArray arr, List<object?> args)
    {
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

    private static object? Splice(Interpreter _, SharpTSArray arr, List<object?> args)
    {
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

        // Collect deleted elements directly into a Deque (single allocation)
        var deleted = new Deque<object?>(arr.Elements.GetRange(actualStart, actualDeleteCount));

        // Remove then insert
        arr.Elements.RemoveRange(actualStart, actualDeleteCount);
        if (args.Count > 2)
        {
            var itemsToInsert = args.Skip(2).ToList();
            arr.Elements.InsertRange(actualStart, itemsToInsert);
        }

        return new SharpTSArray(deleted);
    }

    private static object? ToSpliced(Interpreter _, SharpTSArray arr, List<object?> args)
    {
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

    private static object? FindLast(Interpreter interp, SharpTSArray arr, List<object?> args)
    {
        var callback = args[0] as ISharpTSCallable
            ?? throw new Exception("Runtime Error: findLast requires a function argument.");

        var callbackArgs = new List<object?>(3) { null, null, arr };
        for (int i = arr.Elements.Count - 1; i >= 0; i--)
        {
            callbackArgs[0] = arr.Elements[i];
            callbackArgs[1] = (double)i;
            if (IsTruthy(callback.Call(interp, callbackArgs)))
                return arr.Elements[i];
        }
        return null;
    }

    private static object? FindLastIndex(Interpreter interp, SharpTSArray arr, List<object?> args)
    {
        var callback = args[0] as ISharpTSCallable
            ?? throw new Exception("Runtime Error: findLastIndex requires a function argument.");

        var callbackArgs = new List<object?>(3) { null, null, arr };
        for (int i = arr.Elements.Count - 1; i >= 0; i--)
        {
            callbackArgs[0] = arr.Elements[i];
            callbackArgs[1] = (double)i;
            if (IsTruthy(callback.Call(interp, callbackArgs)))
                return (double)i;
        }
        return -1.0;
    }

    private static object? ToReversed(Interpreter _, SharpTSArray arr, List<object?> args)
    {
        var result = new List<object?>(arr.Elements.Count);
        for (int i = arr.Elements.Count - 1; i >= 0; i--)
            result.Add(arr.Elements[i]);
        return new SharpTSArray(result);
    }

    private static object? With(Interpreter _, SharpTSArray arr, List<object?> args)
    {
        int len = arr.Elements.Count;
        int index = ToIntegerOrInfinity(args[0], 0);
        int actualIndex = index < 0 ? len + index : index;
        if (actualIndex < 0 || actualIndex >= len)
            throw new Exception("RangeError: Invalid index for with()");
        var result = new List<object?>(arr.Elements);
        result[actualIndex] = args[1];
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

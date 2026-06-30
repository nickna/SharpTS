using SharpTS.Compilation;
using SharpTS.Execution;
using SharpTS.Runtime;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

public static class ArrayBuiltIns
{
    private static readonly BuiltInTypeMemberLookup<SharpTSArray> _lookup =
        BuiltInTypeBuilder<SharpTSArray>.ForInstanceType()
            .Property("length", arr => (double)arr.LongLength)
            .MethodV2("push", 1, int.MaxValue, PushV2)
            .MethodV2("pop", 0, PopV2)
            .MethodV2("shift", 0, ShiftV2)
            .MethodV2("unshift", 1, int.MaxValue, UnshiftV2)
            // Spec lengths (ECMA-262 §23.1.3) differ from MinArity for
            // variadic / optional-trailing-arg methods. Pass explicit
            // specLength when (a) the spec mandates a value other than
            // MinArity, AND (b) MinArity isn't already that value.
            .MethodV2("slice", 0, 2, specLength: 2, SliceV2)
            // Callback-taking methods accept (callback, thisArg) per ECMA-262
            // §23.1.3. thisArg is forwarded as the callback's `this`.
            // CallbackIterator.Create reads args[1] and BindThis-es a
            // SharpTSFunction callback when present.
            .MethodV2("map", 1, 2, specLength: 1, MapV2)
            .MethodV2("filter", 1, 2, specLength: 1, FilterV2)
            .MethodV2("forEach", 1, 2, specLength: 1, ForEachV2)
            .MethodV2("find", 1, 2, specLength: 1, FindV2)
            .MethodV2("findIndex", 1, 2, specLength: 1, FindIndexV2)
            .MethodV2("some", 1, 2, specLength: 1, SomeV2)
            .MethodV2("every", 1, 2, specLength: 1, EveryV2)
            .MethodV2("reduce", 1, 2, ReduceV2)
            .MethodV2("reduceRight", 1, 2, ReduceRightV2)
            .MethodV2("includes", 1, IncludesV2)
            .MethodV2("indexOf", 1, 2, IndexOfV2)
            .MethodV2("lastIndexOf", 1, 2, LastIndexOfV2)
            .MethodV2("join", 0, 1, specLength: 1, JoinV2)
            // Array.prototype.toString = join() with ","; distinct from the debug ToString().
            .MethodV2("toString", 0, static (interp, arr, _) => RuntimeValue.FromString(ToJsString(interp, arr)))
            // Array.prototype.concat accepts any number of args (variadic).
            .MethodV2("concat", 0, int.MaxValue, specLength: 1, ConcatV2)
            .MethodV2("reverse", 0, ReverseV2)
            .MethodV2("flat", 0, 1, FlatV2)
            .MethodV2("flatMap", 1, 2, specLength: 1, FlatMapV2)
            .MethodV2("sort", 0, 1, specLength: 1, SortV2)
            .MethodV2("toSorted", 0, 1, specLength: 1, ToSortedV2)
            .MethodV2("splice", 0, int.MaxValue, specLength: 2, SpliceV2)
            .MethodV2("toSpliced", 0, int.MaxValue, specLength: 2, ToSplicedV2)
            .MethodV2("findLast", 1, 2, specLength: 1, FindLastV2)
            .MethodV2("findLastIndex", 1, 2, specLength: 1, FindLastIndexV2)
            .MethodV2("toReversed", 0, ToReversedV2)
            .MethodV2("with", 2, WithV2)
            .MethodV2("at", 1, AtV2)
            .MethodV2("fill", 1, 3, FillV2)
            .MethodV2("copyWithin", 1, 3, specLength: 2, CopyWithinV2)
            .MethodV2("entries", 0, (_, arr, _) => RuntimeValue.FromObject(new SharpTSIterator(EnumerateEntries(arr))))
            .MethodV2("keys", 0, (_, arr, _) => RuntimeValue.FromObject(new SharpTSIterator(EnumerateKeys(arr))))
            .MethodV2("values", 0, (_, arr, _) => RuntimeValue.FromObject(new SharpTSIterator(EnumerateValues(arr))))
            .Build();

    public static object? GetMember(SharpTSArray receiver, string name)
        => _lookup.GetMember(receiver, name);

    /// <summary>
    /// Returns the unbound <see cref="BuiltInMethod"/> for an Array.prototype
    /// method, or null if no such method exists. Used by
    /// <see cref="SharpTSArrayPrototype"/> to expose the full instance-method
    /// set — so <c>Array.prototype.every.call(arr, cb)</c> in user code
    /// resolves to the same implementation as <c>arr.every(cb)</c>.
    /// </summary>
    public static BuiltInMethod? GetPrototypeMethod(string name)
        => _lookup.GetMethod(name);

    private static object? Flat(Interpreter _, SharpTSArray arr, List<object?> args)
    {
        // ECMA-262 23.1.3.13: skips holes.
        var depth = args.Count > 0 && args[0] is double d
            ? (double.IsPositiveInfinity(d) ? int.MaxValue : (int)d)
            : 1;

        var result = new List<object?>();
        FlattenArray(arr, result, depth);
        return new SharpTSArray(result);
    }

    private static void FlattenArray(SharpTSArray source, List<object?> result, int depth)
    {
        int len = source.Length;
        for (int i = 0; i < len; i++)
        {
            if (!source.HasIndex(i)) continue;  // skip holes per spec
            var item = source[i];
            if (depth > 0 && item is SharpTSArray nested)
                FlattenArray(nested, result, depth - 1);
            else
                result.Add(item);
        }
    }

    private static object? FlatMap(Interpreter interp, SharpTSArray arr, List<object?> args)
    {
        // ECMA-262 23.1.3.12: skips holes.
        using var iter = CallbackIterator.Create(args, arr, "flatMap");
        var result = new List<object?>();
        int len = arr.Length;
        for (int i = 0; i < len; i++)
        {
            if (!arr.HasIndex(i)) continue;
            var callResult = iter.InvokeRV(interp, arr[i], i).ToObject();
            // flatMap flattens by 1 level only
            if (callResult is SharpTSArray mappedArray)
            {
                // Spec: inner arrays also have their holes skipped during the
                // single-level flatten. (CreateDataPropertyOrThrow only fires
                // when kPresent is true.)
                int innerLen = mappedArray.Length;
                for (int j = 0; j < innerLen; j++)
                {
                    if (mappedArray.HasIndex(j))
                        result.Add(mappedArray[j]);
                }
            }
            else
            {
                result.Add(callResult);
            }
        }
        return new SharpTSArray(result);
    }

    private static object? Sort(Interpreter interp, SharpTSArray arr, List<object?> args)
    {
        // Frozen arrays cannot be modified; silent fail (matches reverse behavior)
        if (arr.IsFrozen) return arr;

        ISharpTSCallable? compareFn = args.Count > 0 ? args[0] as ISharpTSCallable : null;

        // Partition undefined to end (JS behavior)
        var defined = new List<(object? Element, int Index)>();
        int undefinedCount = 0;
        for (int i = 0; i < arr.Length; i++)
        {
            if (IsUndefined(arr[i]))
                undefinedCount++;
            else
                defined.Add((arr[i], i));
        }

        var sorted = StableSort(defined, compareFn, interp);

        arr.Clear();
        arr.AddRange(sorted);
        for (int i = 0; i < undefinedCount; i++)
            arr.Add(SharpTSUndefined.Instance);

        return arr;
    }

    private static object? ToSorted(Interpreter interp, SharpTSArray arr, List<object?> args)
    {
        ISharpTSCallable? compareFn = args.Count > 0 ? args[0] as ISharpTSCallable : null;

        // Same logic but returns NEW array
        var defined = new List<(object? Element, int Index)>();
        int undefinedCount = 0;
        for (int i = 0; i < arr.Length; i++)
        {
            if (IsUndefined(arr[i]))
                undefinedCount++;
            else
                defined.Add((arr[i], i));
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
            sorted = items.OrderBy(x => CoerceToJsString(interp, x.Element), StringComparer.Ordinal)
                          .ThenBy(x => x.Index);
        }

        try
        {
            return sorted.Select(x => x.Element).ToList();
        }
        catch (InvalidOperationException ex) when (ex.InnerException is Exceptions.ThrowException te)
        {
            // LINQ's sort wraps a comparator's guest throw in InvalidOperationException
            // ("Failed to compare two elements in the array."). Re-surface the original guest
            // throw so it reaches the guest catch (#921). Compiled mode's IL merge sort already
            // propagates it natively.
            throw te;
        }
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
            // NOTE: deliberately stays on the legacy boxed Call rather than CallV2. For the
            // common trivial comparator (`(a,b)=>a-b`), eagerly converting both boxed args to
            // RuntimeValue here (FromBoxed) costs more than this near-free reference copy and
            // measured ~13% SLOWER on a 100k interpreter sort — the boxed Call lets the
            // comparator body unbox lazily. Revisit only with a non-boxing comparator path.
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
        int len = arr.Length;

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
        var deleted = new Deque<object?>(arr.GetRange(actualStart, actualDeleteCount));

        // Remove then insert
        arr.RemoveRange(actualStart, actualDeleteCount);
        if (args.Count > 2)
        {
            // InsertRange accepts IEnumerable - no need to materialize to list
            arr.InsertRange(actualStart, args.Skip(2));
        }

        return new SharpTSArray(deleted);
    }

    private static object? ToSpliced(Interpreter _, SharpTSArray arr, List<object?> args)
    {
        int len = arr.Length;

        // toSpliced works on frozen/sealed arrays (creates new array)

        // If no arguments, return a copy of the array
        if (args.Count == 0)
            return new SharpTSArray(new List<object?>(arr));

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
        // Pre-size to avoid reallocations: before(actualStart) + inserted(args.Count-2) + after(len - actualStart - actualSkipCount)
        int insertCount = args.Count > 2 ? args.Count - 2 : 0;
        int afterCount = len - actualStart - actualSkipCount;
        var result = new List<object?>(actualStart + insertCount + afterCount);

        // Add elements before splice point
        for (int i = 0; i < actualStart; i++)
            result.Add(arr[i]);

        // Add inserted elements
        for (int i = 2; i < args.Count; i++)
            result.Add(args[i]);

        // Add elements after splice point
        for (int i = actualStart + actualSkipCount; i < len; i++)
            result.Add(arr[i]);

        return new SharpTSArray(result);
    }

    #region V2 Implementations (RuntimeValue — no boxing)

    private static RuntimeValue PushV2(Interpreter _, SharpTSArray arr, ReadOnlySpan<RuntimeValue> args)
    {
        if (arr.IsFrozen || arr.IsSealed || !arr.IsExtensible)
            return RuntimeValue.FromNumber(arr.Length);
        foreach (var arg in args)
            arr.Add(arg.ToObject());
        return RuntimeValue.FromNumber(arr.Length);
    }

    private static RuntimeValue PopV2(Interpreter _, SharpTSArray arr, ReadOnlySpan<RuntimeValue> args)
    {
        if (arr.IsFrozen || arr.IsSealed || arr.Length == 0)
            return RuntimeValue.Null;
        return RuntimeValue.FromBoxed(arr.RemoveLast());
    }

    private static RuntimeValue ShiftV2(Interpreter _, SharpTSArray arr, ReadOnlySpan<RuntimeValue> args)
    {
        if (arr.IsFrozen || arr.IsSealed || arr.Length == 0)
            return RuntimeValue.Null;
        return RuntimeValue.FromBoxed(arr.RemoveFirst());
    }

    private static RuntimeValue UnshiftV2(Interpreter _, SharpTSArray arr, ReadOnlySpan<RuntimeValue> args)
    {
        if (arr.IsFrozen || arr.IsSealed || !arr.IsExtensible)
            return RuntimeValue.FromNumber(arr.Length);
        // JS variadic: unshift(a, b, c) on [x, y] yields [a, b, c, x, y].
        // AddFirst preserves insertion position, so walk args in reverse.
        for (int i = args.Length - 1; i >= 0; i--)
            arr.AddFirst(args[i].ToObject());
        return RuntimeValue.FromNumber(arr.Length);
    }

    private static RuntimeValue SliceV2(Interpreter _, SharpTSArray arr, ReadOnlySpan<RuntimeValue> args)
    {
        var start = args.Length > 0 ? (int)args[0].AsNumber() : 0;
        var end = args.Length > 1 ? (int)args[1].AsNumber() : arr.Length;
        if (start < 0) start = Math.Max(0, arr.Length + start);
        if (end < 0) end = Math.Max(0, arr.Length + end);
        if (start > arr.Length) start = arr.Length;
        if (end > arr.Length) end = arr.Length;
        if (end <= start) return RuntimeValue.FromObject(new SharpTSArray([]));
        var sliced = arr.GetRange(start, end - start);
        return RuntimeValue.FromObject(new SharpTSArray(new Deque<object?>(sliced)));
    }

    private static RuntimeValue IncludesV2(Interpreter _, SharpTSArray arr, ReadOnlySpan<RuntimeValue> args)
    {
        // ECMA-262 23.1.3.14: does NOT skip holes — holes compare as undefined
        // under SameValueZero, so [,].includes(undefined) === true.
        var searchElement = args[0].ToObject();
        int len = arr.Length;
        for (int i = 0; i < len; i++)
        {
            if (IsEqual(arr[i], searchElement))  // arr[i] unhole's to undefined
                return RuntimeValue.True;
        }
        return RuntimeValue.False;
    }

    private static RuntimeValue IndexOfV2(Interpreter _, SharpTSArray arr, ReadOnlySpan<RuntimeValue> args)
    {
        // ECMA-262 23.1.3.17: skips holes. Uses strict equality (===) which never
        // matches a hole. Optional `fromIndex` clamps the starting index; negative
        // values are relative to length.
        var searchElement = args[0].ToObject();
        int len = arr.Length;
        int start = 0;
        if (args.Length > 1)
        {
            double fromIndex = ToIntegerOrInfinity(args[1].ToObject());
            if (double.IsPositiveInfinity(fromIndex)) return RuntimeValue.FromNumber(-1);
            if (double.IsNegativeInfinity(fromIndex)) start = 0;
            else if (fromIndex >= 0) start = (int)Math.Min(fromIndex, len);
            else start = (int)Math.Max(len + fromIndex, 0);
        }
        for (int idx = start; idx < len; idx++)
        {
            if (!arr.HasIndex(idx)) continue;
            if (IsEqual(arr[idx], searchElement))
                return RuntimeValue.FromNumber(idx);
        }
        return RuntimeValue.FromNumber(-1);
    }

    private static RuntimeValue LastIndexOfV2(Interpreter _, SharpTSArray arr, ReadOnlySpan<RuntimeValue> args)
    {
        // ECMA-262 23.1.3.18: skips holes. Searches backwards from `fromIndex`
        // (default: length - 1). Negative fromIndex is relative to length;
        // NaN becomes 0 → returns -1.
        var searchElement = args[0].ToObject();
        int len = arr.Length;
        int start;
        if (args.Length > 1)
        {
            double fromIndex = ToIntegerOrInfinity(args[1].ToObject());
            if (double.IsNegativeInfinity(fromIndex)) return RuntimeValue.FromNumber(-1);
            if (fromIndex >= 0) start = (int)Math.Min(fromIndex, len - 1);
            else start = (int)(len + fromIndex);
        }
        else
        {
            start = len - 1;
        }
        for (int idx = start; idx >= 0; idx--)
        {
            if (!arr.HasIndex(idx)) continue;
            if (IsEqual(arr[idx], searchElement))
                return RuntimeValue.FromNumber(idx);
        }
        return RuntimeValue.FromNumber(-1);
    }

    /// <summary>
    /// ECMA-262 7.1.5 ToIntegerOrInfinity — coerces to integer, keeping
    /// ±Infinity and mapping NaN to 0.
    /// </summary>
    private static double ToIntegerOrInfinity(object? value)
    {
        double d = value switch
        {
            double n => n,
            bool b => b ? 1.0 : 0.0,
            string s => double.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : double.NaN,
            null => 0,
            SharpTSUndefined => double.NaN,
            _ => double.NaN,
        };
        if (double.IsNaN(d)) return 0;
        if (double.IsInfinity(d)) return d;
        return Math.Truncate(d);
    }

    private static RuntimeValue JoinV2(Interpreter interp, SharpTSArray arr, ReadOnlySpan<RuntimeValue> args)
    {
        // ECMA-262 23.1.3.16: holes (and null/undefined values) render as empty
        // string. Separator defaults to "," when absent OR explicitly
        // undefined — `arr.join(undefined)` is equivalent to `arr.join()` per
        // step 3 of the spec.
        string separator;
        if (args.Length == 0 || args[0].ToObject() is SharpTSUndefined)
            separator = ",";
        else
            separator = CoerceToJsString(interp, args[0].ToObject());
        int len = arr.Length;
        if (len == 0) return RuntimeValue.EmptyString;
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < len; i++)
        {
            if (i > 0) sb.Append(separator);
            // Holes AND null/undefined elements stringify to empty per spec.
            if (!arr.HasIndex(i)) continue;
            var v = arr[i];
            if (v is null or SharpTSUndefined) continue;
            sb.Append(CoerceToJsString(interp, v));
        }
        return RuntimeValue.FromString(sb.ToString());
    }

    /// <summary>
    /// ECMA-262 23.1.3.36 Array.prototype.toString = join() with the default ","
    /// separator — the string an array coerces to in string contexts (<c>+</c>,
    /// template literals, <c>String()</c>), distinct from the console/debug
    /// <see cref="SharpTSArray.ToString"/> format ("[1, 2, 3]").
    /// </summary>
    internal static string ToJsString(Interpreter interp, SharpTSArray arr)
        => JoinV2(interp, arr, ReadOnlySpan<RuntimeValue>.Empty).AsString();

    /// <summary>
    /// Copies [0, length) of <paramref name="src"/> into <paramref name="dst"/>,
    /// preserving holes as <see cref="ArrayHole"/>.<c>Instance</c> entries. Used
    /// by concat / with / toReversed to honor ECMA-262's hole-preserving semantics.
    /// </summary>
    private static void AppendPreservingHoles(SharpTSArray src, List<object?> dst)
    {
        int len = src.Length;
        for (int i = 0; i < len; i++)
        {
            if (src.HasIndex(i))
                dst.Add(src[i]);
            else
                dst.Add(ArrayHole.Instance);
        }
    }

    private static RuntimeValue ConcatV2(Interpreter _, SharpTSArray arr, ReadOnlySpan<RuntimeValue> args)
    {
        // ECMA-262 23.1.3.2: preserves holes from array arguments; non-array args
        // are appended as single elements.
        var result = new List<object?>(arr.Length);
        AppendPreservingHoles(arr, result);
        for (int a = 0; a < args.Length; a++)
        {
            var arg = args[a].ToObject();
            if (arg is SharpTSArray otherArr)
                AppendPreservingHoles(otherArr, result);
            else
                result.Add(arg);
        }
        return RuntimeValue.FromObject(new SharpTSArray(result));
    }

    private static RuntimeValue ReverseV2(Interpreter _, SharpTSArray arr, ReadOnlySpan<RuntimeValue> args)
    {
        // ECMA-262 23.1.3.26: preserves holes. Implemented via hole-aware swap
        // so reverse([1,,3]) === [3,,1] (middle stays a hole).
        if (arr.IsFrozen)
            return RuntimeValue.FromObject(arr);
        int len = arr.Length;
        int lower = 0, upper = len - 1;
        while (lower < upper)
        {
            bool lowerPresent = arr.HasIndex(lower);
            bool upperPresent = arr.HasIndex(upper);
            var lowerValue = lowerPresent ? arr[lower] : null;
            var upperValue = upperPresent ? arr[upper] : null;
            if (upperPresent) arr[lower] = upperValue;
            else arr.DeleteAt(lower);
            if (lowerPresent) arr[upper] = lowerValue;
            else arr.DeleteAt(upper);
            lower++;
            upper--;
        }
        return RuntimeValue.FromObject(arr);
    }

    private static RuntimeValue ToReversedV2(Interpreter _, SharpTSArray arr, ReadOnlySpan<RuntimeValue> args)
    {
        // ECMA-262 23.1.3.33: produces a dense array — holes are fetched via Get
        // (returns undefined) and assigned via CreateDataPropertyOrThrow. So
        // toReversed FILLS holes with undefined. (This is different from reverse,
        // which preserves holes.)
        int len = arr.Length;
        var result = new List<object?>(len);
        for (int i = len - 1; i >= 0; i--)
            result.Add(arr[i]);  // user-facing read: holes become undefined
        return RuntimeValue.FromObject(new SharpTSArray(result));
    }

    private static RuntimeValue WithV2(Interpreter _, SharpTSArray arr, ReadOnlySpan<RuntimeValue> args)
    {
        // ECMA-262 23.1.3.39: produces a dense array with the modified element.
        // Holes in the source become undefined in the output.
        int len = arr.Length;
        int index = (int)args[0].AsNumber();
        int actualIndex = index < 0 ? len + index : index;
        if (actualIndex < 0 || actualIndex >= len)
            throw new Exception("RangeError: Invalid index for with()");
        var result = new List<object?>(len);
        for (int i = 0; i < len; i++)
            result.Add(i == actualIndex ? args[1].ToObject() : arr[i]);
        return RuntimeValue.FromObject(new SharpTSArray(result));
    }

    private static RuntimeValue AtV2(Interpreter _, SharpTSArray arr, ReadOnlySpan<RuntimeValue> args)
    {
        int len = arr.Length;
        int index = (int)args[0].AsNumber();
        int actualIndex = index < 0 ? len + index : index;
        if (actualIndex < 0 || actualIndex >= len)
            return RuntimeValue.Undefined;
        return RuntimeValue.FromBoxed(arr[actualIndex]);
    }

    private static RuntimeValue FillV2(Interpreter _, SharpTSArray arr, ReadOnlySpan<RuntimeValue> args)
    {
        // ECMA-262 23.1.3.9: Fill WRITES every position in [start, end) — holes
        // are filled, not preserved.
        if (arr.IsFrozen)
            return RuntimeValue.FromObject(arr);

        int len = arr.Length;
        if (len == 0) return RuntimeValue.FromObject(arr);

        var value = args.Length > 0 ? args[0].ToObject() : null;

        int relStart = args.Length > 1 ? ToIntegerOrInfinity(args[1].ToObject(), 0) : 0;
        int actualStart = relStart < 0 ? Math.Max(len + relStart, 0) : Math.Min(relStart, len);

        int relEnd = args.Length > 2 ? ToIntegerOrInfinity(args[2].ToObject(), len) : len;
        int actualEnd = relEnd < 0 ? Math.Max(len + relEnd, 0) : Math.Min(relEnd, len);

        for (int i = actualStart; i < actualEnd; i++)
            arr[i] = value;

        return RuntimeValue.FromObject(arr);
    }

    private static RuntimeValue CopyWithinV2(Interpreter _, SharpTSArray arr, ReadOnlySpan<RuntimeValue> args)
    {
        if (arr.IsFrozen)
            return RuntimeValue.FromObject(arr);

        int len = arr.Length;
        if (len == 0) return RuntimeValue.FromObject(arr);

        int relTarget = args.Length > 0 ? (int)args[0].AsNumber() : 0;
        int to = relTarget < 0 ? Math.Max(len + relTarget, 0) : Math.Min(relTarget, len);

        int relStart = args.Length > 1 ? (int)args[1].AsNumber() : 0;
        int from = relStart < 0 ? Math.Max(len + relStart, 0) : Math.Min(relStart, len);

        int relEnd = args.Length > 2 ? (int)args[2].AsNumber() : len;
        int final_ = relEnd < 0 ? Math.Max(len + relEnd, 0) : Math.Min(relEnd, len);

        int count = Math.Min(final_ - from, len - to);

        if (count > 0)
        {
            // ECMA-262 23.1.3.4: if source is a hole, DELETE target (make hole).
            // Otherwise copy the value. Order (forward/backward) matters only when
            // source and dest ranges overlap.
            if (from < to && to < from + count)
            {
                for (int i = count - 1; i >= 0; i--)
                    CopyOrHole(arr, from + i, to + i);
            }
            else
            {
                for (int i = 0; i < count; i++)
                    CopyOrHole(arr, from + i, to + i);
            }
        }

        return RuntimeValue.FromObject(arr);
    }

    private static void CopyOrHole(SharpTSArray arr, int fromIdx, int toIdx)
    {
        if (arr.HasIndex(fromIdx))
            arr[toIdx] = arr[fromIdx];
        else
            arr.DeleteAt(toIdx);
    }

    // --- Callback-based V2 methods ---

    private static RuntimeValue MapV2(Interpreter interp, SharpTSArray arr, ReadOnlySpan<RuntimeValue> args)
    {
        // ECMA-262 23.1.3.18: skip holes (only invoke callback for present indices)
        // but preserve them in the output array at the same position.
        using var iter = CallbackIterator.CreateFromRV(args, arr, "map");
        int len = arr.Length;
        List<object?> result = new(len);
        for (int i = 0; i < len; i++)
        {
            if (arr.HasIndex(i))
                result.Add(iter.Invoke(interp, arr[i], i));
            else
                result.Add(ArrayHole.Instance);  // preserve hole
        }
        return RuntimeValue.FromObject(new SharpTSArray(result));
    }

    private static RuntimeValue FilterV2(Interpreter interp, SharpTSArray arr, ReadOnlySpan<RuntimeValue> args)
    {
        // ECMA-262 23.1.3.8: skip holes. Output is always dense.
        using var iter = CallbackIterator.CreateFromRV(args, arr, "filter");
        List<object?> result = [];
        int len = arr.Length;
        for (int i = 0; i < len; i++)
        {
            if (!arr.HasIndex(i)) continue;
            if (iter.InvokeRV(interp, arr[i], i).IsTruthy())
                result.Add(arr[i]);
        }
        return RuntimeValue.FromObject(new SharpTSArray(result));
    }

    private static RuntimeValue ForEachV2(Interpreter interp, SharpTSArray arr, ReadOnlySpan<RuntimeValue> args)
    {
        // ECMA-262 23.1.3.15: skip holes.
        using var iter = CallbackIterator.CreateFromRV(args, arr, "forEach");
        int len = arr.Length;
        for (int i = 0; i < len; i++)
        {
            if (!arr.HasIndex(i)) continue;
            iter.InvokeRV(interp, arr[i], i);
        }
        return RuntimeValue.Undefined;
    }

    private static RuntimeValue FindV2(Interpreter interp, SharpTSArray arr, ReadOnlySpan<RuntimeValue> args)
    {
        // ECMA-262 23.1.3.10: DOES call callback on holes (no HasProperty check).
        using var iter = CallbackIterator.CreateFromRV(args, arr, "find");
        int len = arr.Length;
        for (int i = 0; i < len; i++)
        {
            if (iter.InvokeRV(interp, arr[i], i).IsTruthy())
                return RuntimeValue.FromBoxed(arr[i]);
        }
        return RuntimeValue.Undefined;
    }

    private static RuntimeValue FindIndexV2(Interpreter interp, SharpTSArray arr, ReadOnlySpan<RuntimeValue> args)
    {
        // ECMA-262 23.1.3.11: DOES call callback on holes (no HasProperty check).
        using var iter = CallbackIterator.CreateFromRV(args, arr, "findIndex");
        int len = arr.Length;
        for (int i = 0; i < len; i++)
        {
            if (iter.InvokeRV(interp, arr[i], i).IsTruthy())
                return RuntimeValue.FromNumber(i);
        }
        return RuntimeValue.FromNumber(-1);
    }

    private static RuntimeValue SomeV2(Interpreter interp, SharpTSArray arr, ReadOnlySpan<RuntimeValue> args)
    {
        // ECMA-262 23.1.3.29: skip holes.
        using var iter = CallbackIterator.CreateFromRV(args, arr, "some");
        int len = arr.Length;
        for (int i = 0; i < len; i++)
        {
            if (!arr.HasIndex(i)) continue;
            if (iter.InvokeRV(interp, arr[i], i).IsTruthy())
                return RuntimeValue.True;
        }
        return RuntimeValue.False;
    }

    private static RuntimeValue EveryV2(Interpreter interp, SharpTSArray arr, ReadOnlySpan<RuntimeValue> args)
    {
        // ECMA-262 23.1.3.6: skip holes.
        using var iter = CallbackIterator.CreateFromRV(args, arr, "every");
        int len = arr.Length;
        for (int i = 0; i < len; i++)
        {
            if (!arr.HasIndex(i)) continue;
            if (!iter.InvokeRV(interp, arr[i], i).IsTruthy())
                return RuntimeValue.False;
        }
        return RuntimeValue.True;
    }

    private static RuntimeValue ReduceV2(Interpreter interp, SharpTSArray arr, ReadOnlySpan<RuntimeValue> args)
    {
        // ECMA-262 23.1.3.24: skip holes. Initial accumulator, if none supplied,
        // is the first PRESENT element. TypeError if the array has no present
        // elements and no initial value is provided.
        var callback = args[0].ToObject() as ISharpTSCallable
            ?? throw new Exception("Runtime Error: reduce requires a function argument.");

        int len = arr.Length;
        int startIndex = 0;
        object? accumulator;

        if (args.Length > 1)
        {
            accumulator = args[1].ToObject();
        }
        else
        {
            // Find first present index.
            int firstPresent = -1;
            for (int i = 0; i < len; i++)
            {
                if (arr.HasIndex(i)) { firstPresent = i; break; }
            }
            if (firstPresent < 0)
                throw new Exception("TypeError: Reduce of empty array with no initial value.");
            accumulator = arr[firstPresent];
            startIndex = firstPresent + 1;
        }

        var callbackArgs = ArgumentListPool.Rent();
        try
        {
            callbackArgs.Add(null);
            callbackArgs.Add(null);
            callbackArgs.Add(null);
            callbackArgs.Add(arr);
            for (int i = startIndex; i < len; i++)
            {
                if (!arr.HasIndex(i)) continue;
                callbackArgs[0] = accumulator;
                callbackArgs[1] = arr[i];
                callbackArgs[2] = (double)i;
                accumulator = callback.Call(interp, callbackArgs);
            }
            return RuntimeValue.FromBoxed(accumulator);
        }
        finally
        {
            ArgumentListPool.Return(callbackArgs);
        }
    }

    private static RuntimeValue ReduceRightV2(Interpreter interp, SharpTSArray arr, ReadOnlySpan<RuntimeValue> args)
    {
        // ECMA-262 23.1.3.25: skip holes; symmetric to reduce.
        var callback = args[0].ToObject() as ISharpTSCallable
            ?? throw new Exception("Runtime Error: reduceRight requires a function argument.");

        int len = arr.Length;
        int startIndex;
        object? accumulator;

        if (args.Length > 1)
        {
            accumulator = args[1].ToObject();
            startIndex = len - 1;
        }
        else
        {
            int lastPresent = -1;
            for (int i = len - 1; i >= 0; i--)
            {
                if (arr.HasIndex(i)) { lastPresent = i; break; }
            }
            if (lastPresent < 0)
                throw new Exception("TypeError: Reduce of empty array with no initial value.");
            accumulator = arr[lastPresent];
            startIndex = lastPresent - 1;
        }

        var callbackArgs = ArgumentListPool.Rent();
        try
        {
            callbackArgs.Add(null);
            callbackArgs.Add(null);
            callbackArgs.Add(null);
            callbackArgs.Add(arr);
            for (int i = startIndex; i >= 0; i--)
            {
                if (!arr.HasIndex(i)) continue;
                callbackArgs[0] = accumulator;
                callbackArgs[1] = arr[i];
                callbackArgs[2] = (double)i;
                accumulator = callback.Call(interp, callbackArgs);
            }
            return RuntimeValue.FromBoxed(accumulator);
        }
        finally
        {
            ArgumentListPool.Return(callbackArgs);
        }
    }

    private static RuntimeValue FindLastV2(Interpreter interp, SharpTSArray arr, ReadOnlySpan<RuntimeValue> args)
    {
        using var iter = CallbackIterator.CreateFromRV(args, arr, "findLast");
        for (int i = arr.Length - 1; i >= 0; i--)
        {
            if (iter.InvokeRV(interp, arr[i], i).IsTruthy())
                return RuntimeValue.FromBoxed(arr[i]);
        }
        // ECMA-262 23.1.3.11: return undefined when no element matches.
        return RuntimeValue.Undefined;
    }

    private static RuntimeValue FindLastIndexV2(Interpreter interp, SharpTSArray arr, ReadOnlySpan<RuntimeValue> args)
    {
        using var iter = CallbackIterator.CreateFromRV(args, arr, "findLastIndex");
        for (int i = arr.Length - 1; i >= 0; i--)
        {
            if (iter.InvokeRV(interp, arr[i], i).IsTruthy())
                return RuntimeValue.FromNumber(i);
        }
        return RuntimeValue.FromNumber(-1);
    }

    #endregion

    private static bool IsUndefined(object? obj)
    {
        return obj is SharpTSUndefined;
    }

    private static bool IsEqual(object? a, object? b)
    {
        if (a == null && b == null) return true;
        if (a == null) return false;
        return a.Equals(b);
    }

    /// <summary>
    /// ECMA-262 ToString of an array element / separator / default-sort key (used by
    /// join, Array.prototype.toString and the default sort comparator). A nested array
    /// renders via its own join (recursive, default ","); every other value — class
    /// instances (incl. Errors, dispatching their <c>toString</c>), plain objects
    /// ("[object Object]"), boxed wrappers (unwrapped) and primitives — goes through the
    /// interpreter's ToString. Replaces a bare <c>obj.ToString()</c> that skipped
    /// <c>toString</c> and leaked the console/debug array/object format (#922 follow-up).
    /// </summary>
    private static string CoerceToJsString(Interpreter interp, object? v)
        => v is SharpTSArray a ? ToJsString(interp, a) : interp.ToStringForStringCall(v);

    #region Iterator Methods

    private static IEnumerable<object?> EnumerateEntries(SharpTSArray arr)
    {
        for (int i = 0; i < arr.Length; i++)
        {
            yield return new SharpTSArray([(double)i, arr[i]]);
        }
    }

    private static IEnumerable<object?> EnumerateKeys(SharpTSArray arr)
    {
        for (int i = 0; i < arr.Length; i++)
        {
            yield return (double)i;
        }
    }

    private static IEnumerable<object?> EnumerateValues(SharpTSArray arr)
    {
        foreach (var element in arr)
        {
            yield return element;
        }
    }

    #endregion

    private readonly struct CallbackIterator : IDisposable
    {
        private readonly ISharpTSCallable _callback;
        private readonly RuntimeValue[] _argsV2;

        private CallbackIterator(ISharpTSCallable callback, RuntimeValue arrRV)
        {
            _callback = callback;
            _argsV2 = [default, default, arrRV];
        }

        public static CallbackIterator Create(List<object?> args, SharpTSArray arr, string methodName)
        {
            var callback = args[0] as ISharpTSCallable
                ?? throw new Exception($"Runtime Error: {methodName} requires a function argument.");
            // ECMA-262 §23.1.3 callback methods accept (cb, thisArg). If thisArg
            // is supplied, re-bind regular functions and function expressions.
            // Arrow functions (HasOwnThis=false) ignore the binding per spec.
            if (args.Count >= 2)
                callback = BindCallbackThis(callback, args[1]);
            return new CallbackIterator(callback, RuntimeValue.FromObject(arr));
        }

        public static CallbackIterator CreateFromRV(ReadOnlySpan<RuntimeValue> args, SharpTSArray arr, string methodName)
        {
            var callback = args[0].ToObject() as ISharpTSCallable
                ?? throw new Exception($"Runtime Error: {methodName} requires a function argument.");
            if (args.Length >= 2)
                callback = BindCallbackThis(callback, args[1].ToObject());
            return new CallbackIterator(callback, RuntimeValue.FromObject(arr));
        }

        public object? Invoke(Interpreter interp, object? element, int index)
        {
            _argsV2[0] = RuntimeValue.FromBoxed(element);
            _argsV2[1] = RuntimeValue.FromNumber(index);
            return _callback.CallV2(interp, _argsV2).ToObject();
        }

        /// <summary>
        /// V2-native invoke — returns RuntimeValue without boxing at return boundary.
        /// </summary>
        public RuntimeValue InvokeRV(Interpreter interp, object? element, int index)
        {
            _argsV2[0] = RuntimeValue.FromBoxed(element);
            _argsV2[1] = RuntimeValue.FromNumber(index);
            return _callback.CallV2(interp, _argsV2);
        }

        public void Dispose() { }

        /// <summary>
        /// Re-binds the callback's `this` to <paramref name="thisValue"/> if the
        /// callback is a regular function (`SharpTSFunction`) or a function
        /// expression (`SharpTSArrowFunction` with HasOwnThis). True arrow
        /// functions (HasOwnThis=false) ignore the thisArg per ECMA-262 spec.
        /// Other callable shapes (BuiltInMethod etc.) are returned unchanged.
        /// </summary>
        private static ISharpTSCallable BindCallbackThis(ISharpTSCallable callback, object? thisValue)
        {
            return callback switch
            {
                SharpTSFunction fn => fn.BindThis(thisValue),
                SharpTSArrowFunction afn when afn.HasOwnThis && thisValue is not null
                    => afn.Bind(thisValue),
                _ => callback,
            };
        }
    }

    // ===================== V2 Wrappers (RuntimeValue boundary) =====================

    private static RuntimeValue FlatV2(Interpreter interp, SharpTSArray arr, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromBoxed(Flat(interp, arr, CallableInterop.ToBoxedList(args)));

    private static RuntimeValue FlatMapV2(Interpreter interp, SharpTSArray arr, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromBoxed(FlatMap(interp, arr, CallableInterop.ToBoxedList(args)));

    private static RuntimeValue SortV2(Interpreter interp, SharpTSArray arr, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromBoxed(Sort(interp, arr, CallableInterop.ToBoxedList(args)));

    private static RuntimeValue ToSortedV2(Interpreter interp, SharpTSArray arr, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromBoxed(ToSorted(interp, arr, CallableInterop.ToBoxedList(args)));

    private static RuntimeValue SpliceV2(Interpreter interp, SharpTSArray arr, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromBoxed(Splice(interp, arr, CallableInterop.ToBoxedList(args)));

    private static RuntimeValue ToSplicedV2(Interpreter interp, SharpTSArray arr, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromBoxed(ToSpliced(interp, arr, CallableInterop.ToBoxedList(args)));
}

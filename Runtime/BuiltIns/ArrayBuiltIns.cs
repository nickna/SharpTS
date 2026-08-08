using SharpTS.Compilation;
using SharpTS.Execution;
using SharpTS.Runtime;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

public static class ArrayBuiltIns
{
    private static readonly BuiltInTypeMemberLookup<SharpTSArray> _lookup =
        BuiltInTypeBuilder<SharpTSArray>.ForInstanceType()
            .Property("length", arr => (double)arr.LongLength)
            .MethodV2("push", 0, int.MaxValue, 1, PushV2)
            .MethodV2("pop", 0, PopV2)
            .MethodV2("shift", 0, ShiftV2)
            .MethodV2("unshift", 0, int.MaxValue, 1, UnshiftV2)
            // Spec lengths (ECMA-262 §23.1.3) differ from MinArity for
            // variadic / optional-trailing-arg methods. Pass explicit
            // specLength when (a) the spec mandates a value other than
            // MinArity, AND (b) MinArity isn't already that value.
            .MethodV2("slice", 0, 2, specLength: 2, SliceV2)
            // Callback-taking methods accept (callback, thisArg) per ECMA-262
            // §23.1.3. thisArg is forwarded as the callback's `this`.
            // CallbackIterator.Create reads args[1] and BindThis-es a
            // SharpTSFunction callback when present.
            .MethodV2("map", 0, int.MaxValue, specLength: 1, MapV2)
            .MethodV2("filter", 0, int.MaxValue, specLength: 1, FilterV2)
            .MethodV2("forEach", 0, int.MaxValue, specLength: 1, ForEachV2)
            .MethodV2("find", 0, int.MaxValue, specLength: 1, FindV2)
            .MethodV2("findIndex", 0, int.MaxValue, specLength: 1, FindIndexV2)
            .MethodV2("some", 0, int.MaxValue, specLength: 1, SomeV2)
            .MethodV2("every", 0, int.MaxValue, specLength: 1, EveryV2)
            .MethodV2("reduce", 0, int.MaxValue, specLength: 1, ReduceV2)
            .MethodV2("reduceRight", 0, int.MaxValue, specLength: 1, ReduceRightV2)
            .MethodV2("includes", 0, 2, specLength: 1, IncludesV2)
            .MethodV2("indexOf", 1, 2, IndexOfV2)
            .MethodV2("lastIndexOf", 1, 2, LastIndexOfV2)
            .MethodV2("join", 0, 1, specLength: 1, JoinV2)
            // Array.prototype.toString = join() with ","; distinct from the debug ToString().
            .MethodV2("toString", 0, static (interp, arr, _) => RuntimeValue.FromString(ToJsString(interp, arr)))
            .MethodV2("toLocaleString", 0, static (interp, arr, _) => RuntimeValue.FromString(ToJsString(interp, arr)))
            // Array.prototype.concat accepts any number of args (variadic).
            .MethodV2("concat", 0, int.MaxValue, specLength: 1, ConcatV2)
            .MethodV2("reverse", 0, ReverseV2)
            .MethodV2("flat", 0, 1, FlatV2)
            .MethodV2("flatMap", 0, int.MaxValue, specLength: 1, FlatMapV2)
            .MethodV2("sort", 0, 1, specLength: 1, SortV2)
            .MethodV2("toSorted", 0, 1, specLength: 1, ToSortedV2)
            .MethodV2("splice", 0, int.MaxValue, specLength: 2, SpliceV2)
            .MethodV2("toSpliced", 0, int.MaxValue, specLength: 2, ToSplicedV2)
            .MethodV2("findLast", 0, int.MaxValue, specLength: 1, FindLastV2)
            .MethodV2("findLastIndex", 0, int.MaxValue, specLength: 1, FindLastIndexV2)
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
    private static int ToIntegerOrInfinityAsInt(Interpreter interpreter, object? value)
    {
        double integer = ToIntegerOrInfinity(interpreter, value);
        if (double.IsPositiveInfinity(integer) || integer >= int.MaxValue)
            return int.MaxValue;
        if (double.IsNegativeInfinity(integer) || integer <= int.MinValue)
            return int.MinValue;
        return (int)integer;
    }

    private static object? Splice(Interpreter interpreter, SharpTSArray arr, List<object?> args)
    {
        int len = arr.Length;

        // Frozen/sealed arrays throw TypeError
        if (arr.IsFrozen || arr.IsSealed)
            throw new Exception("TypeError: Cannot modify a frozen or sealed array");

        // If no arguments, return empty array (no elements deleted or inserted)
        if (args.Count == 0)
            return new SharpTSArray([]);

        // Parse start with negative handling (RelativeIndex to ActualIndex)
        int relStart = ToIntegerOrInfinityAsInt(interpreter, args[0]);
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
            int dc = ToIntegerOrInfinityAsInt(interpreter, args[1]);
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

    private static object? ToSpliced(Interpreter interpreter, SharpTSArray arr, List<object?> args)
    {
        int len = arr.Length;

        // toSpliced works on frozen/sealed arrays (creates new array)

        // If no arguments, return a copy of the array
        if (args.Count == 0)
            return new SharpTSArray(new List<object?>(arr));

        // Parse start with negative handling
        int relStart = ToIntegerOrInfinityAsInt(interpreter, args[0]);
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
            int sc = ToIntegerOrInfinityAsInt(interpreter, args[1]);
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

    private static RuntimeValue PushV2(Interpreter interpreter, SharpTSArray arr, ReadOnlySpan<RuntimeValue> args)
    {
        var items = new object?[args.Length];
        for (int i = 0; i < args.Length; i++)
            items[i] = args[i].ToObject();
        return RuntimeValue.FromNumber(PushArrayLike(interpreter, arr, items));
    }

    private static RuntimeValue PopV2(Interpreter interpreter, SharpTSArray arr, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromBoxed(PopArrayLike(interpreter, arr));

    private static RuntimeValue ShiftV2(Interpreter interpreter, SharpTSArray arr, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromBoxed(ShiftArrayLike(interpreter, arr));

    private static RuntimeValue UnshiftV2(Interpreter interpreter, SharpTSArray arr, ReadOnlySpan<RuntimeValue> args)
    {
        var items = new object?[args.Length];
        for (int i = 0; i < args.Length; i++)
            items[i] = args[i].ToObject();
        return RuntimeValue.FromNumber(UnshiftArrayLike(interpreter, arr, items));
    }

    private static RuntimeValue SliceV2(Interpreter _, SharpTSArray arr, ReadOnlySpan<RuntimeValue> args)
    {
        var start = args.Length > 0 ? (int)Interpreter.ToNumber(args[0]) : 0;
        var end = args.Length > 1 ? (int)Interpreter.ToNumber(args[1]) : arr.Length;
        if (start < 0) start = Math.Max(0, arr.Length + start);
        if (end < 0) end = Math.Max(0, arr.Length + end);
        if (start > arr.Length) start = arr.Length;
        if (end > arr.Length) end = arr.Length;
        if (end <= start) return RuntimeValue.FromObject(new SharpTSArray([]));
        var sliced = arr.GetRange(start, end - start);
        return RuntimeValue.FromObject(new SharpTSArray(new Deque<object?>(sliced)));
    }

    private static RuntimeValue IncludesV2(Interpreter interpreter, SharpTSArray arr, ReadOnlySpan<RuntimeValue> args)
    {
        // ECMA-262 23.1.3.14: does NOT skip holes — holes compare as undefined
        // under SameValueZero, so [,].includes(undefined) === true.
        int len = arr.Length;
        if (len == 0) return RuntimeValue.False;

        var searchElement = args.Length > 0
            ? args[0].ToObject()
            : SharpTSUndefined.Instance;
        double fromIndex = args.Length > 1
            ? ToIntegerOrInfinity(interpreter, args[1].ToObject())
            : 0;
        if (double.IsPositiveInfinity(fromIndex) || fromIndex >= len)
            return RuntimeValue.False;
        int start = double.IsNegativeInfinity(fromIndex) || fromIndex < -len
            ? 0
            : fromIndex >= 0 ? (int)fromIndex : (int)(len + fromIndex);

        for (int i = start; i < len; i++)
        {
            if (IsEqual(arr[i], searchElement))  // arr[i] unhole's to undefined
                return RuntimeValue.True;
        }
        return RuntimeValue.False;
    }

    private static RuntimeValue IndexOfV2(
        Interpreter interpreter, SharpTSArray arr, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromNumber(SearchArrayLike(
            interpreter,
            arr,
            args.Length > 0 ? args[0].ToObject() : SharpTSUndefined.Instance,
            args.Length > 1,
            args.Length > 1 ? args[1].ToObject() : null,
            fromEnd: false));

    private static RuntimeValue LastIndexOfV2(
        Interpreter interpreter, SharpTSArray arr, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromNumber(SearchArrayLike(
            interpreter,
            arr,
            args.Length > 0 ? args[0].ToObject() : SharpTSUndefined.Instance,
            args.Length > 1,
            args.Length > 1 ? args[1].ToObject() : null,
            fromEnd: true));

    /// <summary>
    /// ECMA-262 23.1.3.17/18 generic array-like search. Length is captured once,
    /// but HasProperty/Get are performed for every visited index so getters and
    /// mutations caused by <c>fromIndex</c> coercion or an earlier indexed getter
    /// remain observable.
    /// </summary>
    internal static double SearchArrayLike(
        Interpreter interpreter,
        object receiver,
        IReadOnlyList<object?> args,
        bool fromEnd)
        => SearchArrayLike(
            interpreter,
            receiver,
            args.Count > 0 ? args[0] : SharpTSUndefined.Instance,
            args.Count > 1,
            args.Count > 1 ? args[1] : null,
            fromEnd);

    private static double SearchArrayLike(
        Interpreter interpreter,
        object receiver,
        object? searchElement,
        bool hasFromIndex,
        object? fromIndexValue,
        bool fromEnd)
    {
        long len = ToLength(interpreter.GetPropertyValue(receiver, "length"), interpreter);
        if (len == 0) return -1;

        double fromIndex = hasFromIndex
            ? ToIntegerOrInfinity(interpreter, fromIndexValue)
            : fromEnd ? len - 1 : 0;

        long index;
        if (fromEnd)
        {
            if (double.IsNegativeInfinity(fromIndex) || fromIndex < -len) return -1;
            index = double.IsPositiveInfinity(fromIndex) || fromIndex >= len
                ? len - 1
                : fromIndex >= 0
                    ? (long)fromIndex
                    : (long)(len + fromIndex);
        }
        else
        {
            if (double.IsPositiveInfinity(fromIndex) || fromIndex >= len) return -1;
            index = double.IsNegativeInfinity(fromIndex) || fromIndex < -len
                ? 0
                : fromIndex >= 0
                    ? (long)fromIndex
                    : (long)(len + fromIndex);
        }

        long step = fromEnd ? -1 : 1;
        for (; index >= 0 && index < len; index += step)
        {
            string key = index.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
            if (!interpreter.HasProperty(receiver, key)) continue;
            if (IsStrictlyEqual(interpreter.GetPropertyValue(receiver, key), searchElement))
                return index;
        }
        return -1;
    }

    internal static long ToLength(object? value, Interpreter interpreter)
    {
        double number = interpreter.ToNumberWithPrimitive(value);
        if (double.IsNaN(number) || number <= 0) return 0;
        const long MaxSafeInteger = (1L << 53) - 1;
        if (double.IsPositiveInfinity(number)) return MaxSafeInteger;
        return (long)Math.Min(Math.Truncate(number), MaxSafeInteger);
    }

    /// <summary>
    /// ECMA-262 23.1.3.39 generic Array.prototype.with implementation. Indexed
    /// values are read directly from the original receiver so accessors remain
    /// observable, except at the replaced index, which the algorithm must not
    /// read at all.
    /// </summary>
    internal static object CopyWithArrayLike(
        Interpreter interpreter,
        object receiver,
        IReadOnlyList<object?> args)
    {
        long len = ToLength(interpreter.GetPropertyValue(receiver, "length"), interpreter);
        if (len > uint.MaxValue)
            throw new ThrowException(new SharpTSRangeError("Invalid array length."));

        object? indexValue = args.Count > 0
            ? args[0]
            : SharpTSUndefined.Instance;
        double relativeIndex = ToIntegerOrInfinity(interpreter, indexValue);
        double actualIndex = relativeIndex >= 0 ? relativeIndex : len + relativeIndex;
        if (actualIndex < 0 || actualIndex >= len)
            throw new ThrowException(new SharpTSRangeError("Invalid index for with()."));

        object? replacement = args.Count > 1
            ? args[1]
            : SharpTSUndefined.Instance;
        int materializedLength = (int)Math.Min(len, 1 << 20);
        var result = new List<object?>(materializedLength);
        for (int i = 0; i < materializedLength; i++)
        {
            if (i == actualIndex)
            {
                result.Add(replacement);
                continue;
            }

            string key = i.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
            result.Add(interpreter.GetPropertyValue(receiver, key));
        }
        return new SharpTSArray(result);
    }

    /// <summary>
    /// ECMA-262 ArraySetLength steps 3-5. The descriptor value is converted
    /// separately for ToUint32 and ToNumber, so user conversion hooks run twice
    /// before descriptor attributes are validated.
    /// </summary>
    internal static double CoerceArrayLength(Interpreter interpreter, object? value)
    {
        uint newLength = ToUint32(interpreter.ToNumberWithPrimitive(value));
        double numberLength = interpreter.ToNumberWithPrimitive(value);
        if (newLength != numberLength)
            throw new ThrowException(new SharpTSRangeError("Invalid array length."));
        return newLength;
    }

    private static uint ToUint32(double number)
    {
        if (number == 0 || double.IsNaN(number) || double.IsInfinity(number))
            return 0;
        const double Modulus = 4294967296d;
        double integer = Math.Truncate(number);
        double modulo = integer % Modulus;
        if (modulo < 0) modulo += Modulus;
        return (uint)modulo;
    }

    internal static bool IsGenericCallbackMethod(string name)
        => name is "map" or "filter" or "flatMap" or "forEach"
            or "find" or "findIndex" or "findLast" or "findLastIndex"
            or "some" or "every" or "reduce" or "reduceRight";

    /// <summary>
    /// Runs a callback-based Array.prototype method against a generic array-like
    /// receiver. Unlike the fallback materialization used by read-only methods,
    /// indexed properties are queried live so mutations from getters/callbacks
    /// affect later iterations. The receiver itself is passed as the callback's
    /// final argument.
    /// </summary>
    internal static object? InvokeArrayLikeCallbackMethod(
        Interpreter interpreter,
        object receiver,
        string methodName,
        IReadOnlyList<object?> args)
    {
        long arrayLikeLength = ToLength(
            interpreter.GetPropertyValue(receiver, "length"), interpreter);
        if (methodName == "map" && arrayLikeLength > uint.MaxValue)
            throw new ThrowException(new SharpTSRangeError("Invalid array length."));
        int len = (int)Math.Min(arrayLikeLength, 1 << 20);

        if (methodName is "reduce" or "reduceRight")
            return ReduceArrayLike(interpreter, receiver, methodName, args, len);

        using var iter = CallbackIterator.CreateForArrayLike(args, receiver, methodName);
        switch (methodName)
        {
            case "map":
            {
                var result = new List<object?>(len);
                for (int i = 0; i < len; i++)
                {
                    if (TryGetPresentElement(interpreter, receiver, i, out var element))
                        result.Add(iter.Invoke(interpreter, element, i));
                    else
                        result.Add(ArrayHole.Instance);
                }
                return new SharpTSArray(result);
            }
            case "filter":
            {
                List<object?> result = [];
                for (int i = 0; i < len; i++)
                {
                    if (!TryGetPresentElement(interpreter, receiver, i, out var element)) continue;
                    if (iter.InvokeRV(interpreter, element, i).IsTruthy())
                        result.Add(element);
                }
                return new SharpTSArray(result);
            }
            case "flatMap":
            {
                List<object?> result = [];
                for (int i = 0; i < len; i++)
                {
                    if (!TryGetPresentElement(interpreter, receiver, i, out var element)) continue;
                    var mapped = iter.Invoke(interpreter, element, i);
                    if (mapped is SharpTSArray mappedArray)
                        AppendPresentElements(mappedArray, result);
                    else
                        result.Add(mapped);
                }
                return new SharpTSArray(result);
            }
            case "forEach":
                for (int i = 0; i < len; i++)
                {
                    if (TryGetPresentElement(interpreter, receiver, i, out var element))
                        iter.InvokeRV(interpreter, element, i);
                }
                return SharpTSUndefined.Instance;
            case "some":
                for (int i = 0; i < len; i++)
                {
                    if (TryGetPresentElement(interpreter, receiver, i, out var element)
                        && iter.InvokeRV(interpreter, element, i).IsTruthy())
                        return true;
                }
                return false;
            case "every":
                for (int i = 0; i < len; i++)
                {
                    if (TryGetPresentElement(interpreter, receiver, i, out var element)
                        && !iter.InvokeRV(interpreter, element, i).IsTruthy())
                        return false;
                }
                return true;
            case "find":
                for (int i = 0; i < len; i++)
                {
                    var element = GetElement(interpreter, receiver, i);
                    if (iter.InvokeRV(interpreter, element, i).IsTruthy())
                        return element;
                }
                return SharpTSUndefined.Instance;
            case "findIndex":
                for (int i = 0; i < len; i++)
                {
                    var element = GetElement(interpreter, receiver, i);
                    if (iter.InvokeRV(interpreter, element, i).IsTruthy())
                        return (double)i;
                }
                return -1d;
            case "findLast":
                for (int i = len - 1; i >= 0; i--)
                {
                    var element = GetElement(interpreter, receiver, i);
                    if (iter.InvokeRV(interpreter, element, i).IsTruthy())
                        return element;
                }
                return SharpTSUndefined.Instance;
            case "findLastIndex":
                for (int i = len - 1; i >= 0; i--)
                {
                    var element = GetElement(interpreter, receiver, i);
                    if (iter.InvokeRV(interpreter, element, i).IsTruthy())
                        return (double)i;
                }
                return -1d;
            default:
                throw new InvalidOperationException($"Unsupported array callback method: {methodName}");
        }
    }

    private static object? ReduceArrayLike(
        Interpreter interpreter,
        object receiver,
        string methodName,
        IReadOnlyList<object?> args,
        int len)
    {
        if (args.Count == 0 || args[0] is not ISharpTSCallable callback)
            throw TypeError($"{methodName} callback must be callable");

        bool fromEnd = methodName == "reduceRight";
        int step = fromEnd ? -1 : 1;
        int index = fromEnd ? len - 1 : 0;
        object? accumulator = null;

        if (args.Count > 1)
        {
            accumulator = args[1];
        }
        else
        {
            while (index >= 0 && index < len
                && !TryGetPresentElement(interpreter, receiver, index, out accumulator))
            {
                index += step;
            }
            if (index < 0 || index >= len)
                throw TypeError("Reduce of empty array with no initial value");
            index += step;
        }

        var callbackArgs = ArgumentListPool.Rent();
        try
        {
            callbackArgs.Add(null);
            callbackArgs.Add(null);
            callbackArgs.Add(null);
            callbackArgs.Add(receiver);
            for (; index >= 0 && index < len; index += step)
            {
                if (!TryGetPresentElement(interpreter, receiver, index, out var element)) continue;
                callbackArgs[0] = accumulator;
                callbackArgs[1] = element;
                callbackArgs[2] = (double)index;
                accumulator = callback.Call(interpreter, callbackArgs);
            }
            return accumulator;
        }
        finally
        {
            ArgumentListPool.Return(callbackArgs);
        }
    }

    private static object? GetElement(Interpreter interpreter, object receiver, int index)
        => interpreter.GetPropertyValue(receiver, index.ToString(
            System.Globalization.CultureInfo.InvariantCulture));

    private static void AppendPresentElements(SharpTSArray source, List<object?> destination)
    {
        for (int i = 0; i < source.Length; i++)
        {
            if (source.HasIndex(i)) destination.Add(source[i]);
        }
    }

    /// <summary>
    /// ECMA-262 7.1.5 ToIntegerOrInfinity. Full ToNumber coercion is required:
    /// strings may be hexadecimal and objects may run valueOf/toString or throw.
    /// </summary>
    private static double ToIntegerOrInfinity(Interpreter interpreter, object? value)
    {
        double number = interpreter.ToNumberWithPrimitive(value);
        if (double.IsNaN(number)) return 0;
        if (double.IsInfinity(number)) return number;
        return Math.Truncate(number);
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

    private static RuntimeValue ConcatV2(
        Interpreter interpreter, SharpTSArray arr, ReadOnlySpan<RuntimeValue> args)
    {
        var result = new SharpTSArray();
        long nextIndex = 0;
        AppendConcatItem(interpreter, result, ref nextIndex, arr);
        for (int a = 0; a < args.Length; a++)
            AppendConcatItem(interpreter, result, ref nextIndex, args[a].ToObject());
        return RuntimeValue.FromObject(result);
    }

    /// <summary>
    /// ECMA-262 23.1.3.2 generic concat path used by
    /// <c>Array.prototype.concat.call(arrayLike, ...items)</c>. The receiver and
    /// every argument independently consult <c>Symbol.isConcatSpreadable</c>;
    /// absent indexed properties advance the output length without creating
    /// data properties, preserving holes.
    /// </summary>
    internal static object ConcatArrayLike(
        Interpreter interpreter, object receiver, IReadOnlyList<object?> args)
    {
        var result = new SharpTSArray();
        long nextIndex = 0;
        AppendConcatItem(interpreter, result, ref nextIndex, receiver);
        for (int i = 0; i < args.Count; i++)
            AppendConcatItem(interpreter, result, ref nextIndex, args[i]);
        return result;
    }

    /// <summary>
    /// ECMA-262 23.1.3.23 generic pop algorithm. It operates directly on the
    /// receiver so inherited indexed values, accessors, proxies, large lengths,
    /// and strict delete/set failures remain observable in specification order.
    /// </summary>
    internal static object? PopArrayLike(Interpreter interpreter, object receiver)
    {
        long length = ToLength(
            interpreter.GetPropertyValue(receiver, "length"), interpreter);
        if (length == 0)
        {
            interpreter.SetProperty(receiver, "length", 0d);
            return SharpTSUndefined.Instance;
        }

        long newLength = length - 1;
        string key = newLength.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        object? element = interpreter.GetPropertyValue(receiver, key);
        interpreter.DeleteProperty(receiver, key);
        interpreter.SetProperty(receiver, "length", (double)newLength);
        return element;
    }

    /// <summary>
    /// ECMA-262 23.1.3.23 generic push algorithm. Writes directly to the
    /// receiver and performs the 53-bit result-length check before any item is
    /// stored, while preserving partial writes when a later strict Set fails.
    /// </summary>
    internal static double PushArrayLike(
        Interpreter interpreter, object receiver, IReadOnlyList<object?> items)
    {
        const long MaxSafeInteger = (1L << 53) - 1;
        long length = ToLength(
            interpreter.GetPropertyValue(receiver, "length"), interpreter);
        if (items.Count > MaxSafeInteger - length)
            throw TypeError("Array.prototype.push result exceeds the maximum safe integer.");

        for (int i = 0; i < items.Count; i++)
        {
            string key = (length + i).ToString(
                System.Globalization.CultureInfo.InvariantCulture);
            interpreter.SetProperty(receiver, key, items[i]);
        }

        long newLength = length + items.Count;
        interpreter.SetProperty(receiver, "length", (double)newLength);
        return newLength;
    }

    /// <summary>
    /// ECMA-262 23.1.3.27 generic shift algorithm. Indexed properties are
    /// observed and moved one at a time on the original receiver so holes,
    /// inherited values, accessors, and abrupt completions remain visible.
    /// </summary>
    internal static object? ShiftArrayLike(Interpreter interpreter, object receiver)
    {
        long length = ToLength(
            interpreter.GetPropertyValue(receiver, "length"), interpreter);
        if (length == 0)
        {
            interpreter.SetProperty(receiver, "length", 0d);
            return SharpTSUndefined.Instance;
        }

        object? first = interpreter.GetPropertyValue(receiver, "0");
        for (long from = 1; from < length; from++)
        {
            string fromKey = from.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
            string toKey = (from - 1).ToString(
                System.Globalization.CultureInfo.InvariantCulture);
            if (interpreter.HasProperty(receiver, fromKey))
            {
                interpreter.SetProperty(
                    receiver, toKey,
                    interpreter.GetPropertyValue(receiver, fromKey));
            }
            else
            {
                interpreter.DeleteProperty(receiver, toKey);
            }
        }

        long newLength = length - 1;
        interpreter.DeleteProperty(
            receiver,
            newLength.ToString(System.Globalization.CultureInfo.InvariantCulture));
        interpreter.SetProperty(receiver, "length", (double)newLength);
        return first;
    }

    /// <summary>
    /// ECMA-262 23.1.3.29 generic unshift algorithm. Existing properties move
    /// from high to low indexes before new items are written, preserving holes
    /// and the observable ordering of getters, setters, and proxy traps.
    /// </summary>
    internal static double UnshiftArrayLike(
        Interpreter interpreter, object receiver, IReadOnlyList<object?> items)
    {
        const long MaxSafeInteger = (1L << 53) - 1;
        long length = ToLength(
            interpreter.GetPropertyValue(receiver, "length"), interpreter);
        if (items.Count > MaxSafeInteger - length)
            throw TypeError("Array.prototype.unshift result exceeds the maximum safe integer.");

        for (long from = length; from > 0; from--)
        {
            long sourceIndex = from - 1;
            long targetIndex = sourceIndex + items.Count;
            string fromKey = sourceIndex.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
            string toKey = targetIndex.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
            if (interpreter.HasProperty(receiver, fromKey))
            {
                interpreter.SetProperty(
                    receiver, toKey,
                    interpreter.GetPropertyValue(receiver, fromKey));
            }
            else
            {
                interpreter.DeleteProperty(receiver, toKey);
            }
        }

        for (int i = 0; i < items.Count; i++)
        {
            interpreter.SetProperty(
                receiver,
                i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                items[i]);
        }

        long newLength = length + items.Count;
        interpreter.SetProperty(receiver, "length", (double)newLength);
        return newLength;
    }

    private static void AppendConcatItem(
        Interpreter interpreter, SharpTSArray result, ref long nextIndex, object? item)
    {
        const long MaxSafeInteger = (1L << 53) - 1;
        var itemValue = RuntimeValue.FromBoxed(item);
        bool spreadable = false;
        if (itemValue.IsObject)
        {
            object? spreadability = interpreter.GetSymbolPropertyValue(
                item!, SharpTSSymbol.IsConcatSpreadable);
            spreadable = spreadability is SharpTSUndefined
                ? item is SharpTSArray
                    || item is SharpTSProxy proxy && proxy.HasArrayTarget()
                : RuntimeValue.FromBoxed(spreadability).IsTruthy();
        }

        if (!spreadable)
        {
            if (nextIndex >= MaxSafeInteger)
                throw TypeError("Array.prototype.concat result exceeds the maximum safe integer.");
            result.Set(nextIndex++, item);
            return;
        }

        long length = ToLength(
            interpreter.GetPropertyValue(item, "length"), interpreter);
        if (length > MaxSafeInteger - nextIndex)
            throw TypeError("Array.prototype.concat result exceeds the maximum safe integer.");
        if (length > SharpTSArray.MaxLength - nextIndex)
            throw new ThrowException(new SharpTSRangeError("Invalid array length."));

        for (long sourceIndex = 0; sourceIndex < length; sourceIndex++)
        {
            string key = sourceIndex.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
            if (interpreter.HasProperty(item, key))
            {
                result.Set(
                    nextIndex + sourceIndex,
                    interpreter.GetPropertyValue(item, key));
            }
        }
        nextIndex += length;
        result.SetLength(nextIndex);
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

    private static RuntimeValue WithV2(Interpreter interpreter, SharpTSArray arr, ReadOnlySpan<RuntimeValue> args)
    {
        // ECMA-262 23.1.3.39: produces a dense array with the modified element.
        // Holes in the source become undefined in the output.
        int len = arr.Length;
        double index = ToIntegerOrInfinity(interpreter, args[0].ToObject());
        double actualIndex = index < 0 ? len + index : index;
        if (actualIndex < 0 || actualIndex >= len)
            throw new ThrowException(new SharpTSRangeError("Invalid index for with()."));
        var result = new List<object?>(len);
        for (int i = 0; i < len; i++)
            result.Add(i == actualIndex ? args[1].ToObject() : arr[i]);
        return RuntimeValue.FromObject(new SharpTSArray(result));
    }

    private static RuntimeValue AtV2(Interpreter interpreter, SharpTSArray arr, ReadOnlySpan<RuntimeValue> args)
    {
        int len = arr.Length;
        double index = ToIntegerOrInfinity(interpreter, args[0].ToObject());
        double actualIndex = index < 0 ? len + index : index;
        if (actualIndex < 0 || actualIndex >= len)
            return RuntimeValue.Undefined;
        return RuntimeValue.FromBoxed(arr[(int)actualIndex]);
    }

    private static RuntimeValue FillV2(Interpreter interpreter, SharpTSArray arr, ReadOnlySpan<RuntimeValue> args)
    {
        // ECMA-262 23.1.3.9: Fill WRITES every position in [start, end) — holes
        // are filled, not preserved.
        if (arr.IsFrozen)
            return RuntimeValue.FromObject(arr);

        int len = arr.Length;
        if (len == 0) return RuntimeValue.FromObject(arr);

        var value = args.Length > 0 ? args[0].ToObject() : null;

        int relStart = args.Length > 1
            ? ToIntegerOrInfinityAsInt(interpreter, args[1].ToObject())
            : 0;
        int actualStart = relStart < 0 ? Math.Max(len + relStart, 0) : Math.Min(relStart, len);

        int relEnd = args.Length > 2
            ? ToIntegerOrInfinityAsInt(interpreter, args[2].ToObject())
            : len;
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

        int relTarget = args.Length > 0 ? (int)Interpreter.ToNumber(args[0]) : 0;
        int to = relTarget < 0 ? Math.Max(len + relTarget, 0) : Math.Min(relTarget, len);

        int relStart = args.Length > 1 ? (int)Interpreter.ToNumber(args[1]) : 0;
        int from = relStart < 0 ? Math.Max(len + relStart, 0) : Math.Min(relStart, len);

        int relEnd = args.Length > 2 && !args[2].IsUndefined
            ? (int)Interpreter.ToNumber(args[2])
            : len;
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
            if (TryGetPresentElement(interp, arr, i, out var element))
                result.Add(iter.Invoke(interp, element, i));
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
            if (!TryGetPresentElement(interp, arr, i, out var element)) continue;
            if (iter.InvokeRV(interp, element, i).IsTruthy())
                result.Add(element);
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
            if (!TryGetPresentElement(interp, arr, i, out var element)) continue;
            iter.InvokeRV(interp, element, i);
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
            if (!TryGetPresentElement(interp, arr, i, out var element)) continue;
            if (iter.InvokeRV(interp, element, i).IsTruthy())
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
            if (!TryGetPresentElement(interp, arr, i, out var element)) continue;
            if (!iter.InvokeRV(interp, element, i).IsTruthy())
                return RuntimeValue.False;
        }
        return RuntimeValue.True;
    }

    private static RuntimeValue ReduceV2(Interpreter interp, SharpTSArray arr, ReadOnlySpan<RuntimeValue> args)
    {
        // ECMA-262 23.1.3.24: skip holes. Initial accumulator, if none supplied,
        // is the first PRESENT element. TypeError if the array has no present
        // elements and no initial value is provided.
        var callback = RequireCallable(args, "reduce");

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
            object? firstValue = null;
            for (int i = 0; i < len; i++)
            {
                if (TryGetPresentElement(interp, arr, i, out firstValue))
                {
                    firstPresent = i;
                    break;
                }
            }
            if (firstPresent < 0)
                throw TypeError("Reduce of empty array with no initial value");
            accumulator = firstValue;
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
                if (!TryGetPresentElement(interp, arr, i, out var element)) continue;
                callbackArgs[0] = accumulator;
                callbackArgs[1] = element;
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
        var callback = RequireCallable(args, "reduceRight");

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
            object? lastValue = null;
            for (int i = len - 1; i >= 0; i--)
            {
                if (TryGetPresentElement(interp, arr, i, out lastValue))
                {
                    lastPresent = i;
                    break;
                }
            }
            if (lastPresent < 0)
                throw TypeError("Reduce of empty array with no initial value");
            accumulator = lastValue;
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
                if (!TryGetPresentElement(interp, arr, i, out var element)) continue;
                callbackArgs[0] = accumulator;
                callbackArgs[1] = element;
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

    private static ISharpTSCallable RequireCallable(
        ReadOnlySpan<RuntimeValue> args,
        string methodName)
    {
        if (args.Length > 0 && args[0].ToObject() is ISharpTSCallable callback)
            return callback;

        throw TypeError($"{methodName} callback must be callable");
    }

    private static ThrowException TypeError(string message)
        => new(new SharpTSTypeError(message));

    private static bool IsEqual(object? a, object? b)
    {
        if (a == null && b == null) return true;
        if (a == null) return false;
        return a.Equals(b);
    }

    private static bool TryGetPresentElement(
        Interpreter interpreter, SharpTSArray array, int index, out object? value)
    {
        string key = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!interpreter.HasProperty(array, key))
        {
            value = null;
            return false;
        }
        value = interpreter.GetPropertyValue(array, key);
        return true;
    }

    private static bool TryGetPresentElement(
        Interpreter interpreter, object receiver, int index, out object? value)
    {
        string key = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!interpreter.HasProperty(receiver, key))
        {
            value = null;
            return false;
        }
        value = interpreter.GetPropertyValue(receiver, key);
        return true;
    }

    private static bool IsStrictlyEqual(object? a, object? b)
    {
        if (a == null && b == null) return true;
        if (a is SharpTSUndefined && b is SharpTSUndefined) return true;
        if (a == null || b == null || a is SharpTSUndefined || b is SharpTSUndefined)
            return false;
        if (a.GetType() != b.GetType()) return false;
        if (a is double da && b is double db
            && (double.IsNaN(da) || double.IsNaN(db)))
        {
            return false;
        }
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
        => v is SharpTSArray a ? ToJsString(interp, a) : interp.ToStringForBuiltInArgument(v);

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
        private readonly bool _useDefaultGlobalThis;

        private CallbackIterator(
            ISharpTSCallable callback, RuntimeValue arrRV, bool useDefaultGlobalThis = false)
        {
            _callback = callback;
            _argsV2 = [default, default, arrRV];
            _useDefaultGlobalThis = useDefaultGlobalThis;
        }

        public static CallbackIterator Create(List<object?> args, SharpTSArray arr, string methodName)
        {
            var callback = args.Count > 0 ? args[0] as ISharpTSCallable : null;
            if (callback is null)
                throw TypeError($"{methodName} callback must be callable");

            // ECMA-262 §23.1.3 callback methods accept (cb, thisArg). If thisArg
            // is supplied, re-bind regular functions and function expressions.
            // Arrow functions (HasOwnThis=false) ignore the binding per spec.
            if (args.Count >= 2)
                callback = BindCallbackThis(callback, args[1]);
            else if (callback is SharpTSFunction { IsStrict: true })
                callback = BindCallbackThis(callback, SharpTSUndefined.Instance);
            else if (callback is SharpTSArrowFunction { HasOwnThis: true, IsStrict: true })
                callback = BindCallbackThis(callback, SharpTSUndefined.Instance);
            bool useDefaultGlobalThis = args.Count < 2
                && callback is SharpTSFunction { IsStrict: false };
            return new CallbackIterator(
                callback, RuntimeValue.FromObject(arr), useDefaultGlobalThis);
        }

        public static CallbackIterator CreateFromRV(ReadOnlySpan<RuntimeValue> args, SharpTSArray arr, string methodName)
        {
            var callback = RequireCallable(args, methodName);
            if (args.Length >= 2)
                callback = BindCallbackThis(callback, args[1].ToObject());
            else if (callback is SharpTSFunction { IsStrict: true })
                callback = BindCallbackThis(callback, SharpTSUndefined.Instance);
            else if (callback is SharpTSArrowFunction { HasOwnThis: true, IsStrict: true })
                callback = BindCallbackThis(callback, SharpTSUndefined.Instance);
            bool useDefaultGlobalThis = args.Length < 2
                && callback is SharpTSFunction { IsStrict: false };
            return new CallbackIterator(
                callback, RuntimeValue.FromObject(arr), useDefaultGlobalThis);
        }

        public static CallbackIterator CreateForArrayLike(
            IReadOnlyList<object?> args, object receiver, string methodName)
        {
            var callback = args.Count > 0 ? args[0] as ISharpTSCallable : null;
            if (callback is null)
                throw TypeError($"{methodName} callback must be callable");

            if (args.Count >= 2)
                callback = BindCallbackThis(callback, args[1]);
            else if (callback is SharpTSFunction { IsStrict: true })
                callback = BindCallbackThis(callback, SharpTSUndefined.Instance);
            else if (callback is SharpTSArrowFunction { HasOwnThis: true, IsStrict: true })
                callback = BindCallbackThis(callback, SharpTSUndefined.Instance);
            bool useDefaultGlobalThis = args.Count < 2
                && callback is SharpTSFunction { IsStrict: false };
            return new CallbackIterator(
                callback, RuntimeValue.FromObject(receiver), useDefaultGlobalThis);
        }

        public object? Invoke(Interpreter interp, object? element, int index)
        {
            _argsV2[0] = RuntimeValue.FromBoxed(element);
            _argsV2[1] = RuntimeValue.FromNumber(index);
            if (_useDefaultGlobalThis)
            {
                return FunctionBuiltIns.CallWithThis(
                    interp,
                    _callback,
                    interp.GlobalThis,
                    [element, (double)index, _argsV2[2].ToObject()]);
            }
            return _callback.CallV2(interp, _argsV2).ToObject();
        }

        /// <summary>
        /// V2-native invoke — returns RuntimeValue without boxing at return boundary.
        /// </summary>
        public RuntimeValue InvokeRV(Interpreter interp, object? element, int index)
        {
            _argsV2[0] = RuntimeValue.FromBoxed(element);
            _argsV2[1] = RuntimeValue.FromNumber(index);
            if (_useDefaultGlobalThis)
            {
                return RuntimeValue.FromBoxed(FunctionBuiltIns.CallWithThis(
                    interp,
                    _callback,
                    interp.GlobalThis,
                    [element, (double)index, _argsV2[2].ToObject()]));
            }
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

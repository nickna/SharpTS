using SharpTS.Compilation;
using SharpTS.Execution;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Built-in methods for SharpTSIterator instances (ES2025 Iterator Helpers).
/// Provides lazy (map, filter, take, drop, flatMap) and eager (reduce, toArray, forEach, some, every, find)
/// methods, plus the next()/return() iterator protocol.
/// </summary>
public static class IteratorBuiltIns
{
    private static readonly BuiltInTypeMemberLookup<SharpTSIterator> _lookup =
        BuiltInTypeBuilder<SharpTSIterator>.ForInstanceType()
            .MethodV2("next", 0, NextV2)
            .MethodV2("return", 0, 1, ReturnV2)
            .MethodV2("map", 1, MapV2)
            .MethodV2("filter", 1, FilterV2)
            .MethodV2("take", 1, TakeV2)
            .MethodV2("drop", 1, DropV2)
            .MethodV2("flatMap", 1, FlatMapV2)
            .MethodV2("reduce", 1, 2, ReduceV2)
            .MethodV2("toArray", 0, ToArrayV2)
            .MethodV2("forEach", 1, ForEachV2)
            .MethodV2("some", 1, SomeV2)
            .MethodV2("every", 1, EveryV2)
            .MethodV2("find", 1, FindV2)
            .Build();

    public static object? GetMember(SharpTSIterator receiver, string name)
        => _lookup.GetMember(receiver, name);

    private static object? Next(Interpreter _, SharpTSIterator iter, List<object?> args)
        => iter.Next();

    private static object? Return(Interpreter _, SharpTSIterator iter, List<object?> args)
        => iter.Return(args.Count > 0 ? args[0] : null);

    private static object? Map(Interpreter interp, SharpTSIterator iter, List<object?> args)
    {
        var callback = args[0] as ISharpTSCallable
            ?? throw new Exception("TypeError: Iterator.prototype.map requires a callable argument.");
        return new SharpTSIterator(MapIterator(interp, iter.Elements, callback));
    }

    private static IEnumerable<object?> MapIterator(Interpreter interp, IEnumerable<object?> source, ISharpTSCallable callback)
    {
        var callArgs = new List<object?>(2) { null, null };
        double index = 0;
        foreach (var item in source)
        {
            callArgs[0] = item;
            callArgs[1] = index++;
            yield return callback.Call(interp, callArgs);
        }
    }

    private static object? Filter(Interpreter interp, SharpTSIterator iter, List<object?> args)
    {
        var predicate = args[0] as ISharpTSCallable
            ?? throw new Exception("TypeError: Iterator.prototype.filter requires a callable argument.");
        return new SharpTSIterator(FilterIterator(interp, iter.Elements, predicate));
    }

    private static IEnumerable<object?> FilterIterator(Interpreter interp, IEnumerable<object?> source, ISharpTSCallable predicate)
    {
        var callArgs = new List<object?>(2) { null, null };
        double index = 0;
        foreach (var item in source)
        {
            callArgs[0] = item;
            callArgs[1] = index++;
            if (IsTruthy(predicate.Call(interp, callArgs)))
                yield return item;
        }
    }

    private static object? Take(Interpreter _, SharpTSIterator iter, List<object?> args)
    {
        var limit = args[0] is double d ? (int)d : 0;
        return new SharpTSIterator(TakeIterator(iter.Elements, limit));
    }

    private static IEnumerable<object?> TakeIterator(IEnumerable<object?> source, int limit)
    {
        int count = 0;
        foreach (var item in source)
        {
            if (count >= limit) yield break;
            count++;
            yield return item;
        }
    }

    private static object? Drop(Interpreter _, SharpTSIterator iter, List<object?> args)
    {
        var count = args[0] is double d ? (int)d : 0;
        return new SharpTSIterator(DropIterator(iter.Elements, count));
    }

    private static IEnumerable<object?> DropIterator(IEnumerable<object?> source, int toDrop)
    {
        int dropped = 0;
        foreach (var item in source)
        {
            if (dropped < toDrop)
            {
                dropped++;
                continue;
            }
            yield return item;
        }
    }

    private static object? FlatMap(Interpreter interp, SharpTSIterator iter, List<object?> args)
    {
        var callback = args[0] as ISharpTSCallable
            ?? throw new Exception("TypeError: Iterator.prototype.flatMap requires a callable argument.");
        return new SharpTSIterator(FlatMapIterator(interp, iter.Elements, callback));
    }

    private static IEnumerable<object?> FlatMapIterator(Interpreter interp, IEnumerable<object?> source, ISharpTSCallable callback)
    {
        var callArgs = new List<object?>(2) { null, null };
        double index = 0;
        foreach (var item in source)
        {
            callArgs[0] = item;
            callArgs[1] = index++;
            var result = callback.Call(interp, callArgs);
            foreach (var inner in ToIterable(result))
                yield return inner;
        }
    }

    private static object? Reduce(Interpreter interp, SharpTSIterator iter, List<object?> args)
    {
        var callback = args[0] as ISharpTSCallable
            ?? throw new Exception("TypeError: Iterator.prototype.reduce requires a callable argument.");

        var callArgs = new List<object?>(2) { null, null };
        bool hasInitial = args.Count > 1;
        object? accumulator = hasInitial ? args[1] : null;
        bool first = !hasInitial;

        foreach (var item in iter.Elements)
        {
            if (first)
            {
                accumulator = item;
                first = false;
                continue;
            }
            callArgs[0] = accumulator;
            callArgs[1] = item;
            accumulator = callback.Call(interp, callArgs);
        }

        if (first)
            throw new Exception("TypeError: Reduce of empty iterator with no initial value.");

        return accumulator;
    }

    private static object? ToArray(Interpreter _, SharpTSIterator iter, List<object?> args)
    {
        var elements = new List<object?>();
        foreach (var item in iter.Elements)
            elements.Add(item);
        return new SharpTSArray(elements);
    }

    private static object? ForEach(Interpreter interp, SharpTSIterator iter, List<object?> args)
    {
        var callback = args[0] as ISharpTSCallable
            ?? throw new Exception("TypeError: Iterator.prototype.forEach requires a callable argument.");

        var callArgs = new List<object?>(2) { null, null };
        double index = 0;
        foreach (var item in iter.Elements)
        {
            callArgs[0] = item;
            callArgs[1] = index++;
            callback.Call(interp, callArgs);
        }
        return null;
    }

    private static object? Some(Interpreter interp, SharpTSIterator iter, List<object?> args)
    {
        var predicate = args[0] as ISharpTSCallable
            ?? throw new Exception("TypeError: Iterator.prototype.some requires a callable argument.");

        var callArgs = new List<object?>(2) { null, null };
        double index = 0;
        foreach (var item in iter.Elements)
        {
            callArgs[0] = item;
            callArgs[1] = index++;
            if (IsTruthy(predicate.Call(interp, callArgs)))
                return true;
        }
        return false;
    }

    private static object? Every(Interpreter interp, SharpTSIterator iter, List<object?> args)
    {
        var predicate = args[0] as ISharpTSCallable
            ?? throw new Exception("TypeError: Iterator.prototype.every requires a callable argument.");

        var callArgs = new List<object?>(2) { null, null };
        double index = 0;
        foreach (var item in iter.Elements)
        {
            callArgs[0] = item;
            callArgs[1] = index++;
            if (!IsTruthy(predicate.Call(interp, callArgs)))
                return false;
        }
        return true;
    }

    private static object? Find(Interpreter interp, SharpTSIterator iter, List<object?> args)
    {
        var predicate = args[0] as ISharpTSCallable
            ?? throw new Exception("TypeError: Iterator.prototype.find requires a callable argument.");

        var callArgs = new List<object?>(2) { null, null };
        double index = 0;
        foreach (var item in iter.Elements)
        {
            callArgs[0] = item;
            callArgs[1] = index++;
            if (IsTruthy(predicate.Call(interp, callArgs)))
                return item;
        }
        return null;
    }

    /// <summary>
    /// Converts a value to an iterable sequence for flatMap.
    /// </summary>
    private static IEnumerable<object?> ToIterable(object? value)
    {
        if (value is SharpTSArray arr)
        {
            foreach (var item in arr)
                yield return item;
        }
        else if (value is SharpTSIterator iter)
        {
            foreach (var item in iter.Elements)
                yield return item;
        }
        else if (value is SharpTSGenerator gen)
        {
            while (true)
            {
                var result = gen.Next();
                if (result.Done) yield break;
                yield return result.Value;
            }
        }
        else
        {
            // Non-iterable values yield themselves (per spec)
            yield return value;
        }
    }

    private static bool IsTruthy(object? obj) => RuntimeTypes.IsTruthy(obj);

    // ===================== V2 Wrappers (RuntimeValue boundary) =====================

    private static RuntimeValue NextV2(Interpreter interp, SharpTSIterator iter, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromBoxed(Next(interp, iter, []));

    private static RuntimeValue ReturnV2(Interpreter interp, SharpTSIterator iter, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromBoxed(Return(interp, iter, args.Length > 0 ? [args[0].ToObject()] : []));

    private static RuntimeValue MapV2(Interpreter interp, SharpTSIterator iter, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromBoxed(Map(interp, iter, [args[0].ToObject()]));

    private static RuntimeValue FilterV2(Interpreter interp, SharpTSIterator iter, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromBoxed(Filter(interp, iter, [args[0].ToObject()]));

    private static RuntimeValue TakeV2(Interpreter interp, SharpTSIterator iter, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromBoxed(Take(interp, iter, [args[0].ToObject()]));

    private static RuntimeValue DropV2(Interpreter interp, SharpTSIterator iter, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromBoxed(Drop(interp, iter, [args[0].ToObject()]));

    private static RuntimeValue FlatMapV2(Interpreter interp, SharpTSIterator iter, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromBoxed(FlatMap(interp, iter, [args[0].ToObject()]));

    private static RuntimeValue ReduceV2(Interpreter interp, SharpTSIterator iter, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromBoxed(Reduce(interp, iter, CallableInterop.ToBoxedList(args)));

    private static RuntimeValue ToArrayV2(Interpreter interp, SharpTSIterator iter, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromBoxed(ToArray(interp, iter, []));

    private static RuntimeValue ForEachV2(Interpreter interp, SharpTSIterator iter, ReadOnlySpan<RuntimeValue> args)
    {
        ForEach(interp, iter, [args[0].ToObject()]);
        return RuntimeValue.Undefined;
    }

    private static RuntimeValue SomeV2(Interpreter interp, SharpTSIterator iter, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromBoxed(Some(interp, iter, [args[0].ToObject()]));

    private static RuntimeValue EveryV2(Interpreter interp, SharpTSIterator iter, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromBoxed(Every(interp, iter, [args[0].ToObject()]));

    private static RuntimeValue FindV2(Interpreter interp, SharpTSIterator iter, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromBoxed(Find(interp, iter, [args[0].ToObject()]));
}

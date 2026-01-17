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
        for (int i = 0; i < arr.Elements.Count; i++)
        {
            var callResult = callback.Call(interp, [arr.Elements[i], (double)i, arr]);
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
        for (int i = 0; i < arr.Elements.Count; i++)
        {
            var callResult = callback.Call(interp, [arr.Elements[i], (double)i, arr]);
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

        for (int i = 0; i < arr.Elements.Count; i++)
        {
            callback.Call(interp, [arr.Elements[i], (double)i, arr]);
        }
        return null;
    }

    private static object? Find(Interpreter interp, object? r, List<object?> args)
    {
        var arr = (SharpTSArray)r!;
        var callback = args[0] as ISharpTSCallable
            ?? throw new Exception("Runtime Error: find requires a function argument.");

        for (int i = 0; i < arr.Elements.Count; i++)
        {
            var result = callback.Call(interp, [arr.Elements[i], (double)i, arr]);
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

        for (int i = 0; i < arr.Elements.Count; i++)
        {
            var result = callback.Call(interp, [arr.Elements[i], (double)i, arr]);
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

        for (int i = 0; i < arr.Elements.Count; i++)
        {
            var result = callback.Call(interp, [arr.Elements[i], (double)i, arr]);
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

        for (int i = 0; i < arr.Elements.Count; i++)
        {
            var result = callback.Call(interp, [arr.Elements[i], (double)i, arr]);
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

        for (int i = startIndex; i < arr.Elements.Count; i++)
        {
            accumulator = callback.Call(interp, [accumulator, arr.Elements[i], (double)i, arr]);
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

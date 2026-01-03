using SharpTS.Execution;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

public static class ArrayBuiltIns
{
    public static object? GetMember(SharpTSArray receiver, string name)
    {
        return name switch
        {
            "length" => (double)receiver.Elements.Count,

            "push" => new BuiltInMethod("push", 1, (_, recv, args) =>
            {
                var arr = (SharpTSArray)recv!;
                arr.Elements.Add(args[0]);
                return (double)arr.Elements.Count;
            }),

            "pop" => new BuiltInMethod("pop", 0, (_, recv, _) =>
            {
                var arr = (SharpTSArray)recv!;
                if (arr.Elements.Count == 0) return null;
                var last = arr.Elements[^1];
                arr.Elements.RemoveAt(arr.Elements.Count - 1);
                return last;
            }),

            "shift" => new BuiltInMethod("shift", 0, (_, recv, _) =>
            {
                var arr = (SharpTSArray)recv!;
                if (arr.Elements.Count == 0) return null;
                var first = arr.Elements[0];
                arr.Elements.RemoveAt(0);
                return first;
            }),

            "unshift" => new BuiltInMethod("unshift", 1, (_, recv, args) =>
            {
                var arr = (SharpTSArray)recv!;
                arr.Elements.Insert(0, args[0]);
                return (double)arr.Elements.Count;
            }),

            "slice" => new BuiltInMethod("slice", 0, 2, (_, recv, args) =>
            {
                var arr = (SharpTSArray)recv!;
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
            }),

            "map" => new BuiltInMethod("map", 1, (interp, recv, args) =>
            {
                var arr = (SharpTSArray)recv!;
                var callback = args[0] as ISharpTSCallable
                    ?? throw new Exception("Runtime Error: map requires a function argument.");

                var result = new List<object?>();
                for (int i = 0; i < arr.Elements.Count; i++)
                {
                    var callResult = callback.Call(interp, [arr.Elements[i], (double)i, arr]);
                    result.Add(callResult);
                }
                return new SharpTSArray(result);
            }),

            "filter" => new BuiltInMethod("filter", 1, (interp, recv, args) =>
            {
                var arr = (SharpTSArray)recv!;
                var callback = args[0] as ISharpTSCallable
                    ?? throw new Exception("Runtime Error: filter requires a function argument.");

                var result = new List<object?>();
                for (int i = 0; i < arr.Elements.Count; i++)
                {
                    var callResult = callback.Call(interp, [arr.Elements[i], (double)i, arr]);
                    if (IsTruthy(callResult))
                    {
                        result.Add(arr.Elements[i]);
                    }
                }
                return new SharpTSArray(result);
            }),

            "forEach" => new BuiltInMethod("forEach", 1, (interp, recv, args) =>
            {
                var arr = (SharpTSArray)recv!;
                var callback = args[0] as ISharpTSCallable
                    ?? throw new Exception("Runtime Error: forEach requires a function argument.");

                for (int i = 0; i < arr.Elements.Count; i++)
                {
                    callback.Call(interp, [arr.Elements[i], (double)i, arr]);
                }
                return null;
            }),

            "find" => new BuiltInMethod("find", 1, (interp, recv, args) =>
            {
                var arr = (SharpTSArray)recv!;
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
            }),

            "findIndex" => new BuiltInMethod("findIndex", 1, (interp, recv, args) =>
            {
                var arr = (SharpTSArray)recv!;
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
            }),

            "some" => new BuiltInMethod("some", 1, (interp, recv, args) =>
            {
                var arr = (SharpTSArray)recv!;
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
            }),

            "every" => new BuiltInMethod("every", 1, (interp, recv, args) =>
            {
                var arr = (SharpTSArray)recv!;
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
            }),

            "reduce" => new BuiltInMethod("reduce", 1, 2, (interp, recv, args) =>
            {
                var arr = (SharpTSArray)recv!;
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
            }),

            "includes" => new BuiltInMethod("includes", 1, (_, recv, args) =>
            {
                var arr = (SharpTSArray)recv!;
                var searchElement = args[0];

                foreach (var element in arr.Elements)
                {
                    if (IsEqual(element, searchElement))
                    {
                        return true;
                    }
                }
                return false;
            }),

            "indexOf" => new BuiltInMethod("indexOf", 1, (_, recv, args) =>
            {
                var arr = (SharpTSArray)recv!;
                var searchElement = args[0];

                for (int i = 0; i < arr.Elements.Count; i++)
                {
                    if (IsEqual(arr.Elements[i], searchElement))
                    {
                        return (double)i;
                    }
                }
                return -1.0;
            }),

            "join" => new BuiltInMethod("join", 0, 1, (_, recv, args) =>
            {
                var arr = (SharpTSArray)recv!;
                var separator = args.Count > 0 ? Stringify(args[0]) : ",";

                var parts = arr.Elements.Select(e => Stringify(e));
                return string.Join(separator, parts);
            }),

            "concat" => new BuiltInMethod("concat", 1, (_, recv, args) =>
            {
                var arr = (SharpTSArray)recv!;
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
            }),

            "reverse" => new BuiltInMethod("reverse", 0, (_, recv, _) =>
            {
                var arr = (SharpTSArray)recv!;
                arr.Elements.Reverse();
                return arr;
            }),

            _ => null
        };
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

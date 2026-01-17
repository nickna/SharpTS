using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Static methods on the Array namespace (e.g., Array.isArray(), Array.from())
/// </summary>
public static class ArrayStaticBuiltIns
{
    public static object? GetStaticMethod(string name)
    {
        return name switch
        {
            "isArray" => new BuiltInMethod("isArray", 1, (_, _, args) =>
            {
                return args[0] is SharpTSArray;
            }),
            "from" => new BuiltInMethod("from", 1, 2, (interpreter, _, args) =>
            {
                var iterable = args[0] ?? throw new Exception("Runtime Error: Array.from requires an iterable argument.");
                var mapFn = args.Count > 1 ? args[1] as ISharpTSCallable : null;

                var elements = interpreter.GetIterableElements(iterable).ToList();

                if (mapFn != null)
                {
                    var result = new List<object?>();
                    for (int i = 0; i < elements.Count; i++)
                    {
                        result.Add(mapFn.Call(interpreter, [elements[i], (double)i]));
                    }
                    return new SharpTSArray(result);
                }

                return new SharpTSArray(elements);
            }),
            _ => null
        };
    }
}

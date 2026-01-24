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
                    var callbackArgs = new List<object?>(2) { null, null };
                    for (int i = 0; i < elements.Count; i++)
                    {
                        callbackArgs[0] = elements[i];
                        callbackArgs[1] = (double)i;
                        result.Add(mapFn.Call(interpreter, callbackArgs));
                    }
                    return new SharpTSArray(result);
                }

                return new SharpTSArray(elements);
            }),
            "of" => new BuiltInMethod("of", 0, int.MaxValue, (_, _, args) =>
            {
                // Array.of() creates an array from all arguments
                return new SharpTSArray(args.ToList());
            }),
            _ => null
        };
    }
}

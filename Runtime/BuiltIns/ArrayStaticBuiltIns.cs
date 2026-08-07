using SharpTS.Runtime.Exceptions;
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
            "isArray" => BuiltInMethod.CreateV2("isArray", 1, static (_, _, args) =>
            {
                return RuntimeValue.FromBoolean(
                    args[0].ToObject() is SharpTSArray and not SharpTSArguments
                        or SharpTSArrayPrototype);
            }).AsNonConstructor(),
            "from" => BuiltInMethod.CreateV2("from", 1, 3, static (interpreter, _, args) =>
            {
                // ECMA-262 23.1.2.1: Array.from(null) and Array.from(undefined) throw TypeError
                // (via the GetMethod(@@iterator) → Get → ToObject(items) chain).
                if (args[0].IsNullish)
                    throw new ThrowException(new SharpTSTypeError(
                        "Cannot convert undefined or null to object"));
                var iterable = args[0].ToObject()!;

                // ECMA-262 23.1.2.1 step 3a: if mapfn is provided and not undefined,
                // it must be callable. `null` is NOT a stand-in for "no mapfn" — it
                // explicitly throws TypeError per spec (only `undefined` short-circuits).
                ISharpTSCallable? mapFn = null;
                if (args.Length > 1 && !args[1].IsUndefined)
                {
                    if (args[1].ToObject() is not ISharpTSCallable mapFnCallable)
                        throw new ThrowException(new SharpTSTypeError(
                            "Array.from mapFn argument must be a function"));
                    mapFn = mapFnCallable;
                }

                // ECMA-262 23.1.2.1: prefer the iterator protocol; a source without a usable
                // iterator (e.g. an array-like { length: n }) is read via its length +
                // indexed elements rather than throwing "not iterable".
                bool isIterable = interpreter.IsIterableSource(iterable);

                if (mapFn != null)
                {
                    object? thisArg = args.Length > 2
                        ? args[2].ToObject()
                        : SharpTSUndefined.Instance;
                    // Apply mapfn DURING iteration, not after materializing the
                    // whole source (ECMA-262 23.1.2.1 step 6.g: map each element as
                    // it is produced). For an iterator this is observable two ways:
                    // an infinite iterator whose mapfn throws on the first element
                    // must surface that throw immediately (materialize-first would
                    // spin forever); and the throw must trigger IteratorClose. The
                    // lazy `foreach` here gives both — when mapfn throws, the C#
                    // enumerator over GetIterableElements is disposed, which runs
                    // EnumerateWithIteratorProtocol's finally → the iterator's
                    // return(). Array-likes (ReadArrayLikeElements) have no iterator
                    // to close, matching the spec.
                    var result = new List<object?>();
                    var callbackArgs = new List<object?>(2) { null, null };
                    var source = isIterable
                        ? interpreter.GetIterableElements(iterable)
                        : interpreter.ReadArrayLikeElements(iterable);
                    int i = 0;
                    foreach (var element in source)
                    {
                        callbackArgs[0] = element;
                        callbackArgs[1] = (double)i++;
                        result.Add(FunctionBuiltIns.CallWithThis(
                            interpreter, mapFn, thisArg, callbackArgs));
                    }
                    return RuntimeValue.FromObject(new SharpTSArray(result));
                }

                var elements = isIterable
                    ? interpreter.GetIterableElements(iterable).ToList()
                    : interpreter.ReadArrayLikeElements(iterable);
                return RuntimeValue.FromObject(new SharpTSArray(elements));
            }).AsNonConstructor(),
            "of" => BuiltInMethod.CreateV2("of", 0, int.MaxValue, static (_, _, args) =>
            {
                // Array.of() creates an array from all arguments
                var items = new List<object?>(args.Length);
                foreach (var arg in args)
                    items.Add(arg.ToObject());
                return RuntimeValue.FromObject(new SharpTSArray(items));
            }).AsNonConstructor(),
            _ => null
        };
    }
}

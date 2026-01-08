using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Built-in method implementations for WeakMap instances.
/// </summary>
public static class WeakMapBuiltIns
{
    public static object? GetMember(SharpTSWeakMap receiver, string name)
    {
        return name switch
        {
            "get" => new BuiltInMethod("get", 1, (_, recv, args) =>
            {
                var weakMap = (SharpTSWeakMap)recv!;
                var key = args[0]
                    ?? throw new Exception("Runtime Error: WeakMap key cannot be null or undefined.");
                return weakMap.Get(key);
            }),

            "set" => new BuiltInMethod("set", 2, (_, recv, args) =>
            {
                var weakMap = (SharpTSWeakMap)recv!;
                var key = args[0]
                    ?? throw new Exception("Runtime Error: WeakMap key cannot be null or undefined.");
                return weakMap.Set(key, args[1]);
            }),

            "has" => new BuiltInMethod("has", 1, (_, recv, args) =>
            {
                var weakMap = (SharpTSWeakMap)recv!;
                var key = args[0]
                    ?? throw new Exception("Runtime Error: WeakMap key cannot be null or undefined.");
                return weakMap.Has(key);
            }),

            "delete" => new BuiltInMethod("delete", 1, (_, recv, args) =>
            {
                var weakMap = (SharpTSWeakMap)recv!;
                var key = args[0]
                    ?? throw new Exception("Runtime Error: WeakMap key cannot be null or undefined.");
                return weakMap.Delete(key);
            }),

            // Explicitly reject unsupported properties/methods
            "size" => throw new Exception("Runtime Error: WeakMap does not have a size property."),
            "keys" => throw new Exception("Runtime Error: WeakMap does not support iteration."),
            "values" => throw new Exception("Runtime Error: WeakMap does not support iteration."),
            "entries" => throw new Exception("Runtime Error: WeakMap does not support iteration."),
            "forEach" => throw new Exception("Runtime Error: WeakMap does not support iteration."),
            "clear" => throw new Exception("Runtime Error: WeakMap does not have a clear method."),

            _ => null
        };
    }
}

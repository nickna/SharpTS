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
            "get" => BuiltInMethod.CreateV2("get", 1, static (_, recv, args) =>
            {
                var weakMap = (SharpTSWeakMap)recv.ToObject()!;
                var key = args[0].ToObject()
                    ?? throw new Exception("Runtime Error: WeakMap key cannot be null or undefined.");
                return RuntimeValue.FromBoxed(weakMap.Get(key));
            }),

            "set" => BuiltInMethod.CreateV2("set", 2, static (_, recv, args) =>
            {
                var weakMap = (SharpTSWeakMap)recv.ToObject()!;
                var key = args[0].ToObject()
                    ?? throw new Exception("Runtime Error: WeakMap key cannot be null or undefined.");
                return RuntimeValue.FromObject(weakMap.Set(key, args[1].ToObject()));
            }),

            "has" => BuiltInMethod.CreateV2("has", 1, static (_, recv, args) =>
            {
                var weakMap = (SharpTSWeakMap)recv.ToObject()!;
                var key = args[0].ToObject()
                    ?? throw new Exception("Runtime Error: WeakMap key cannot be null or undefined.");
                return RuntimeValue.FromBoolean(weakMap.Has(key));
            }),

            "delete" => BuiltInMethod.CreateV2("delete", 1, static (_, recv, args) =>
            {
                var weakMap = (SharpTSWeakMap)recv.ToObject()!;
                var key = args[0].ToObject()
                    ?? throw new Exception("Runtime Error: WeakMap key cannot be null or undefined.");
                return RuntimeValue.FromBoolean(weakMap.Delete(key));
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

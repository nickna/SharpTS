using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Built-in method implementations for WeakSet instances.
/// </summary>
public static class WeakSetBuiltIns
{
    public static object? GetMember(SharpTSWeakSet receiver, string name)
    {
        return name switch
        {
            "add" => BuiltInMethod.CreateV2("add", 1, static (_, recv, args) =>
            {
                var weakSet = (SharpTSWeakSet)recv.ToObject()!;
                var value = args[0].ToObject()
                    ?? throw new Exception("Runtime Error: WeakSet value cannot be null or undefined.");
                return RuntimeValue.FromObject(weakSet.Add(value));
            }),

            "has" => BuiltInMethod.CreateV2("has", 1, static (_, recv, args) =>
            {
                var weakSet = (SharpTSWeakSet)recv.ToObject()!;
                var value = args[0].ToObject()
                    ?? throw new Exception("Runtime Error: WeakSet value cannot be null or undefined.");
                return RuntimeValue.FromBoolean(weakSet.Has(value));
            }),

            "delete" => BuiltInMethod.CreateV2("delete", 1, static (_, recv, args) =>
            {
                var weakSet = (SharpTSWeakSet)recv.ToObject()!;
                var value = args[0].ToObject()
                    ?? throw new Exception("Runtime Error: WeakSet value cannot be null or undefined.");
                return RuntimeValue.FromBoolean(weakSet.Delete(value));
            }),

            // Explicitly reject unsupported properties/methods
            "size" => throw new Exception("Runtime Error: WeakSet does not have a size property."),
            "keys" => throw new Exception("Runtime Error: WeakSet does not support iteration."),
            "values" => throw new Exception("Runtime Error: WeakSet does not support iteration."),
            "entries" => throw new Exception("Runtime Error: WeakSet does not support iteration."),
            "forEach" => throw new Exception("Runtime Error: WeakSet does not support iteration."),
            "clear" => throw new Exception("Runtime Error: WeakSet does not have a clear method."),

            _ => null
        };
    }
}

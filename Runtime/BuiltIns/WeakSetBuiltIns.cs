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
            "add" => new BuiltInMethod("add", 1, (_, recv, args) =>
            {
                var weakSet = (SharpTSWeakSet)recv!;
                var value = args[0]
                    ?? throw new Exception("Runtime Error: WeakSet value cannot be null or undefined.");
                return weakSet.Add(value);
            }),

            "has" => new BuiltInMethod("has", 1, (_, recv, args) =>
            {
                var weakSet = (SharpTSWeakSet)recv!;
                var value = args[0]
                    ?? throw new Exception("Runtime Error: WeakSet value cannot be null or undefined.");
                return weakSet.Has(value);
            }),

            "delete" => new BuiltInMethod("delete", 1, (_, recv, args) =>
            {
                var weakSet = (SharpTSWeakSet)recv!;
                var value = args[0]
                    ?? throw new Exception("Runtime Error: WeakSet value cannot be null or undefined.");
                return weakSet.Delete(value);
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

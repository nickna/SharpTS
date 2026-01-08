using SharpTS.Execution;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Built-in methods for the Set type.
/// </summary>
/// <remarks>
/// Implements the JavaScript Set API: add, has, delete, clear, keys, values, entries, forEach, size.
/// forEach callback receives (value, value, set) to match JavaScript semantics (value is passed twice).
/// </remarks>
public static class SetBuiltIns
{
    public static object? GetMember(SharpTSSet receiver, string name)
    {
        return name switch
        {
            "size" => (double)receiver.Size,

            "add" => new BuiltInMethod("add", 1, (_, recv, args) =>
            {
                var set = (SharpTSSet)recv!;
                var value = args[0];
                if (value == null)
                    throw new Exception("Runtime Error: Set value cannot be null.");
                return set.Add(value);
            }),

            "has" => new BuiltInMethod("has", 1, (_, recv, args) =>
            {
                var set = (SharpTSSet)recv!;
                var value = args[0];
                if (value == null)
                    throw new Exception("Runtime Error: Set value cannot be null.");
                return set.Has(value);
            }),

            "delete" => new BuiltInMethod("delete", 1, (_, recv, args) =>
            {
                var set = (SharpTSSet)recv!;
                var value = args[0];
                if (value == null)
                    throw new Exception("Runtime Error: Set value cannot be null.");
                return set.Delete(value);
            }),

            "clear" => new BuiltInMethod("clear", 0, (_, recv, _) =>
            {
                var set = (SharpTSSet)recv!;
                set.Clear();
                return null;
            }),

            "keys" => new BuiltInMethod("keys", 0, (_, recv, _) =>
            {
                var set = (SharpTSSet)recv!;
                return set.Keys();
            }),

            "values" => new BuiltInMethod("values", 0, (_, recv, _) =>
            {
                var set = (SharpTSSet)recv!;
                return set.Values();
            }),

            "entries" => new BuiltInMethod("entries", 0, (_, recv, _) =>
            {
                var set = (SharpTSSet)recv!;
                return set.Entries();
            }),

            "forEach" => new BuiltInMethod("forEach", 1, (interp, recv, args) =>
            {
                var set = (SharpTSSet)recv!;
                var callback = args[0] as ISharpTSCallable
                    ?? throw new Exception("Runtime Error: forEach requires a function argument.");

                // JavaScript Set.forEach callback receives (value, value, set)
                // The value is passed twice (for consistency with Map.forEach API)
                foreach (var value in set.InternalValues)
                {
                    callback.Call(interp, [value, value, set]);
                }
                return null;
            }),

            // ES2025 Set Operations
            "union" => new BuiltInMethod("union", 1, (_, recv, args) =>
            {
                var set = (SharpTSSet)recv!;
                var other = args[0] as SharpTSSet
                    ?? throw new Exception("Runtime Error: union requires a Set argument.");
                return set.Union(other);
            }),

            "intersection" => new BuiltInMethod("intersection", 1, (_, recv, args) =>
            {
                var set = (SharpTSSet)recv!;
                var other = args[0] as SharpTSSet
                    ?? throw new Exception("Runtime Error: intersection requires a Set argument.");
                return set.Intersection(other);
            }),

            "difference" => new BuiltInMethod("difference", 1, (_, recv, args) =>
            {
                var set = (SharpTSSet)recv!;
                var other = args[0] as SharpTSSet
                    ?? throw new Exception("Runtime Error: difference requires a Set argument.");
                return set.Difference(other);
            }),

            "symmetricDifference" => new BuiltInMethod("symmetricDifference", 1, (_, recv, args) =>
            {
                var set = (SharpTSSet)recv!;
                var other = args[0] as SharpTSSet
                    ?? throw new Exception("Runtime Error: symmetricDifference requires a Set argument.");
                return set.SymmetricDifference(other);
            }),

            "isSubsetOf" => new BuiltInMethod("isSubsetOf", 1, (_, recv, args) =>
            {
                var set = (SharpTSSet)recv!;
                var other = args[0] as SharpTSSet
                    ?? throw new Exception("Runtime Error: isSubsetOf requires a Set argument.");
                return set.IsSubsetOf(other);
            }),

            "isSupersetOf" => new BuiltInMethod("isSupersetOf", 1, (_, recv, args) =>
            {
                var set = (SharpTSSet)recv!;
                var other = args[0] as SharpTSSet
                    ?? throw new Exception("Runtime Error: isSupersetOf requires a Set argument.");
                return set.IsSupersetOf(other);
            }),

            "isDisjointFrom" => new BuiltInMethod("isDisjointFrom", 1, (_, recv, args) =>
            {
                var set = (SharpTSSet)recv!;
                var other = args[0] as SharpTSSet
                    ?? throw new Exception("Runtime Error: isDisjointFrom requires a Set argument.");
                return set.IsDisjointFrom(other);
            }),

            _ => null
        };
    }
}

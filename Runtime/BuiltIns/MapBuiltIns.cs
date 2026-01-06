using SharpTS.Execution;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Built-in methods for the Map type.
/// </summary>
/// <remarks>
/// Implements the JavaScript Map API: get, set, has, delete, clear, keys, values, entries, forEach, size.
/// forEach callback receives (value, key, map) to match JavaScript semantics.
/// </remarks>
public static class MapBuiltIns
{
    public static object? GetMember(SharpTSMap receiver, string name)
    {
        return name switch
        {
            "size" => (double)receiver.Size,

            "get" => new BuiltInMethod("get", 1, (_, recv, args) =>
            {
                var map = (SharpTSMap)recv!;
                var key = args[0];
                if (key == null)
                    throw new Exception("Runtime Error: Map key cannot be null.");
                return map.Get(key);
            }),

            "set" => new BuiltInMethod("set", 2, (_, recv, args) =>
            {
                var map = (SharpTSMap)recv!;
                var key = args[0];
                if (key == null)
                    throw new Exception("Runtime Error: Map key cannot be null.");
                return map.Set(key, args[1]);
            }),

            "has" => new BuiltInMethod("has", 1, (_, recv, args) =>
            {
                var map = (SharpTSMap)recv!;
                var key = args[0];
                if (key == null)
                    throw new Exception("Runtime Error: Map key cannot be null.");
                return map.Has(key);
            }),

            "delete" => new BuiltInMethod("delete", 1, (_, recv, args) =>
            {
                var map = (SharpTSMap)recv!;
                var key = args[0];
                if (key == null)
                    throw new Exception("Runtime Error: Map key cannot be null.");
                return map.Delete(key);
            }),

            "clear" => new BuiltInMethod("clear", 0, (_, recv, _) =>
            {
                var map = (SharpTSMap)recv!;
                map.Clear();
                return null;
            }),

            "keys" => new BuiltInMethod("keys", 0, (_, recv, _) =>
            {
                var map = (SharpTSMap)recv!;
                return map.Keys();
            }),

            "values" => new BuiltInMethod("values", 0, (_, recv, _) =>
            {
                var map = (SharpTSMap)recv!;
                return map.Values();
            }),

            "entries" => new BuiltInMethod("entries", 0, (_, recv, _) =>
            {
                var map = (SharpTSMap)recv!;
                return map.Entries();
            }),

            "forEach" => new BuiltInMethod("forEach", 1, (interp, recv, args) =>
            {
                var map = (SharpTSMap)recv!;
                var callback = args[0] as ISharpTSCallable
                    ?? throw new Exception("Runtime Error: forEach requires a function argument.");

                // JavaScript Map.forEach callback receives (value, key, map)
                foreach (var kvp in map.InternalEntries)
                {
                    callback.Call(interp, [kvp.Value, kvp.Key, map]);
                }
                return null;
            }),

            _ => null
        };
    }
}

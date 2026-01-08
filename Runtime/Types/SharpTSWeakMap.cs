using System.Runtime.CompilerServices;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime wrapper for TypeScript WeakMap&lt;K, V&gt; collections.
/// </summary>
/// <remarks>
/// Uses ConditionalWeakTable to provide weak reference semantics matching JavaScript WeakMap:
/// - Keys must be objects (not primitives)
/// - Keys are held weakly - when no other references exist, entries can be garbage collected
/// - No size property (not enumerable per spec)
/// - No iteration methods (keys(), values(), entries(), forEach())
/// </remarks>
public class SharpTSWeakMap
{
    private readonly ConditionalWeakTable<object, object?> _map = new();

    /// <summary>
    /// Gets the value associated with the specified key.
    /// Returns null (undefined) if the key is not found.
    /// </summary>
    public object? Get(object key)
    {
        ValidateKey(key);
        return _map.TryGetValue(key, out var value) ? value : null;
    }

    /// <summary>
    /// Sets the value for the specified key. Returns this WeakMap for method chaining.
    /// </summary>
    public SharpTSWeakMap Set(object key, object? value)
    {
        ValidateKey(key);
        _map.AddOrUpdate(key, value);
        return this;
    }

    /// <summary>
    /// Returns true if the WeakMap contains the specified key.
    /// </summary>
    public bool Has(object key)
    {
        ValidateKey(key);
        return _map.TryGetValue(key, out _);
    }

    /// <summary>
    /// Removes the entry with the specified key. Returns true if the key was present.
    /// </summary>
    public bool Delete(object key)
    {
        ValidateKey(key);
        return _map.Remove(key);
    }

    /// <summary>
    /// Validates that the key is a valid object type (not a primitive).
    /// </summary>
    private static void ValidateKey(object? key)
    {
        if (key == null)
        {
            throw new Exception("Runtime Error: WeakMap key cannot be null or undefined.");
        }

        // Check for primitive types that cannot be WeakMap keys
        if (key is string or double or bool or int or long or float or decimal)
        {
            throw new Exception($"Runtime Error: Invalid value used as weak map key. WeakMap keys must be objects, not '{GetTypeName(key)}'.");
        }
    }

    private static string GetTypeName(object value) => value switch
    {
        string => "string",
        double or int or long or float or decimal => "number",
        bool => "boolean",
        _ => value.GetType().Name
    };

    public override string ToString() => "WeakMap { <items unknown> }";
}

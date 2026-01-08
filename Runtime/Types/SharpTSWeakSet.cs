using System.Runtime.CompilerServices;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime wrapper for TypeScript WeakSet&lt;T&gt; collections.
/// </summary>
/// <remarks>
/// Uses ConditionalWeakTable with a sentinel value to provide weak reference semantics matching JavaScript WeakSet:
/// - Values must be objects (not primitives)
/// - Values are held weakly - when no other references exist, entries can be garbage collected
/// - No size property (not enumerable per spec)
/// - No iteration methods (keys(), values(), entries(), forEach())
/// </remarks>
public class SharpTSWeakSet
{
    private readonly ConditionalWeakTable<object, object> _set = new();
    private static readonly object Sentinel = new();

    /// <summary>
    /// Adds a value to the WeakSet. Returns this WeakSet for method chaining.
    /// </summary>
    public SharpTSWeakSet Add(object value)
    {
        ValidateValue(value);
        _set.AddOrUpdate(value, Sentinel);
        return this;
    }

    /// <summary>
    /// Returns true if the WeakSet contains the specified value.
    /// </summary>
    public bool Has(object value)
    {
        ValidateValue(value);
        return _set.TryGetValue(value, out _);
    }

    /// <summary>
    /// Removes the specified value from the WeakSet. Returns true if the value was present.
    /// </summary>
    public bool Delete(object value)
    {
        ValidateValue(value);
        return _set.Remove(value);
    }

    /// <summary>
    /// Validates that the value is a valid object type (not a primitive).
    /// </summary>
    private static void ValidateValue(object? value)
    {
        if (value == null)
        {
            throw new Exception("Runtime Error: WeakSet value cannot be null or undefined.");
        }

        // Check for primitive types that cannot be WeakSet values
        if (value is string or double or bool or int or long or float or decimal)
        {
            throw new Exception($"Runtime Error: Invalid value used in weak set. WeakSet values must be objects, not '{GetTypeName(value)}'.");
        }
    }

    private static string GetTypeName(object value) => value switch
    {
        string => "string",
        double or int or long or float or decimal => "number",
        bool => "boolean",
        _ => value.GetType().Name
    };

    public override string ToString() => "WeakSet { <items unknown> }";
}

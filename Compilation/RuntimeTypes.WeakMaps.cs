using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

public static partial class RuntimeTypes
{
    #region WeakMap Support

    private static readonly ConditionalWeakTable<object, object?>.CreateValueCallback DefaultFactory = _ => null;

    /// <summary>
    /// Creates an empty WeakMap using ConditionalWeakTable.
    /// </summary>
    public static object CreateWeakMap()
    {
        return new ConditionalWeakTable<object, object?>();
    }

    /// <summary>
    /// Gets the value associated with a key in the WeakMap.
    /// Returns null if the key is not found.
    /// </summary>
    public static object? WeakMapGet(object? weakMap, object? key)
    {
        if (weakMap is ConditionalWeakTable<object, object?> table && key != null)
        {
            ValidateWeakMapKey(key);
            return table.TryGetValue(key, out var value) ? value : null;
        }
        return null;
    }

    /// <summary>
    /// Sets a key-value pair in the WeakMap. Returns the WeakMap.
    /// </summary>
    public static object WeakMapSet(object? weakMap, object? key, object? value)
    {
        if (weakMap is ConditionalWeakTable<object, object?> table && key != null)
        {
            ValidateWeakMapKey(key);
            table.AddOrUpdate(key, value);
        }
        return weakMap!;
    }

    /// <summary>
    /// Checks if a key exists in the WeakMap.
    /// </summary>
    public static bool WeakMapHas(object? weakMap, object? key)
    {
        if (weakMap is ConditionalWeakTable<object, object?> table && key != null)
        {
            ValidateWeakMapKey(key);
            return table.TryGetValue(key, out _);
        }
        return false;
    }

    /// <summary>
    /// Deletes a key from the WeakMap. Returns true if the key existed.
    /// </summary>
    public static bool WeakMapDelete(object? weakMap, object? key)
    {
        if (weakMap is ConditionalWeakTable<object, object?> table && key != null)
        {
            ValidateWeakMapKey(key);
            return table.Remove(key);
        }
        return false;
    }

    /// <summary>
    /// Validates that the key is not a primitive type.
    /// </summary>
    private static void ValidateWeakMapKey(object key)
    {
        if (key is string or double or bool or int or long or float or decimal)
        {
            throw new Exception($"Runtime Error: Invalid value used as weak map key. WeakMap keys must be objects, not '{GetPrimitiveTypeName(key)}'.");
        }
    }

    #endregion
}

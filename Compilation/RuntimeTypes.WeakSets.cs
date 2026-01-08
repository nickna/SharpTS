using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

public static partial class RuntimeTypes
{
    #region WeakSet Support

    private static readonly object WeakSetSentinel = new();

    /// <summary>
    /// Creates an empty WeakSet using ConditionalWeakTable with a sentinel value.
    /// </summary>
    public static object CreateWeakSet()
    {
        return new ConditionalWeakTable<object, object>();
    }

    /// <summary>
    /// Adds a value to the WeakSet. Returns the WeakSet.
    /// </summary>
    public static object WeakSetAdd(object? weakSet, object? value)
    {
        if (weakSet is ConditionalWeakTable<object, object> table && value != null)
        {
            ValidateWeakSetValue(value);
            table.AddOrUpdate(value, WeakSetSentinel);
        }
        return weakSet!;
    }

    /// <summary>
    /// Checks if a value exists in the WeakSet.
    /// </summary>
    public static bool WeakSetHas(object? weakSet, object? value)
    {
        if (weakSet is ConditionalWeakTable<object, object> table && value != null)
        {
            ValidateWeakSetValue(value);
            return table.TryGetValue(value, out _);
        }
        return false;
    }

    /// <summary>
    /// Deletes a value from the WeakSet. Returns true if the value existed.
    /// </summary>
    public static bool WeakSetDelete(object? weakSet, object? value)
    {
        if (weakSet is ConditionalWeakTable<object, object> table && value != null)
        {
            ValidateWeakSetValue(value);
            return table.Remove(value);
        }
        return false;
    }

    /// <summary>
    /// Validates that the value is not a primitive type.
    /// </summary>
    private static void ValidateWeakSetValue(object value)
    {
        if (value is string or double or bool or int or long or float or decimal)
        {
            throw new Exception($"Runtime Error: Invalid value used in weak set. WeakSet values must be objects, not '{GetPrimitiveTypeName(value)}'.");
        }
    }

    /// <summary>
    /// Gets the type name for primitive values.
    /// </summary>
    private static string GetPrimitiveTypeName(object value) => value switch
    {
        string => "string",
        double or int or long or float or decimal => "number",
        bool => "boolean",
        _ => value.GetType().Name
    };

    #endregion
}

using SharpTS.Runtime.Types;

namespace SharpTS.Compilation;

public static partial class RuntimeTypes
{
    #region Sets

    /// <summary>
    /// Creates a new empty Set.
    /// </summary>
    public static object CreateSet() => new HashSet<object>(SharpTS.Runtime.Types.ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Creates a Set from an array of values.
    /// </summary>
    public static object CreateSetFromArray(object? values)
    {
        var set = new HashSet<object>(SharpTS.Runtime.Types.ReferenceEqualityComparer.Instance);
        if (values is List<object?> list)
        {
            foreach (var item in list)
            {
                if (item != null)
                {
                    set.Add(item);
                }
            }
        }
        return set;
    }

    /// <summary>
    /// Gets the size of a Set.
    /// </summary>
    public static double SetSize(object? set)
    {
        if (set is HashSet<object> hashSet)
            return hashSet.Count;
        return 0;
    }

    /// <summary>
    /// Adds a value to a Set. Returns the Set for chaining.
    /// </summary>
    public static object SetAdd(object? set, object? value)
    {
        if (set is HashSet<object> hashSet && value != null)
        {
            hashSet.Add(value);
        }
        return set!;
    }

    /// <summary>
    /// Checks if a Set has a value.
    /// </summary>
    public static bool SetHas(object? set, object? value)
    {
        if (set is HashSet<object> hashSet && value != null)
        {
            return hashSet.Contains(value);
        }
        return false;
    }

    /// <summary>
    /// Deletes a value from a Set. Returns true if value existed.
    /// </summary>
    public static bool SetDelete(object? set, object? value)
    {
        if (set is HashSet<object> hashSet && value != null)
        {
            return hashSet.Remove(value);
        }
        return false;
    }

    /// <summary>
    /// Clears all values from a Set.
    /// </summary>
    public static void SetClear(object? set)
    {
        if (set is HashSet<object> hashSet)
        {
            hashSet.Clear();
        }
    }

    /// <summary>
    /// Returns an iterator over the Set's values (same as values() for Set).
    /// </summary>
    public static List<object?> SetKeys(object? set)
    {
        if (set is HashSet<object> hashSet)
        {
            return hashSet.Select(v => (object?)v).ToList();
        }
        return [];
    }

    /// <summary>
    /// Returns an iterator over the Set's values.
    /// </summary>
    public static List<object?> SetValues(object? set)
    {
        if (set is HashSet<object> hashSet)
        {
            return hashSet.Select(v => (object?)v).ToList();
        }
        return [];
    }

    /// <summary>
    /// Returns an iterator over [value, value] pairs (for compatibility with Map.entries()).
    /// </summary>
    public static List<object?> SetEntries(object? set)
    {
        if (set is HashSet<object> hashSet)
        {
            return hashSet.Select(v => (object?)new List<object?> { v, v }).ToList();
        }
        return [];
    }

    /// <summary>
    /// Executes a callback for each Set value.
    /// </summary>
    public static void SetForEach(object? set, object? callback)
    {
        if (set is HashSet<object> hashSet && callback != null)
        {
            foreach (var value in hashSet)
            {
                // Per JS spec, callback receives (value, value, set) for Set.forEach
                InvokeValue(callback, [value, value, set]);
            }
        }
    }

    #endregion
}

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

    #region ES2025 Set Operations

    /// <summary>
    /// Returns a new Set containing all elements from both Sets.
    /// ES2025: Set.prototype.union()
    /// </summary>
    public static object SetUnion(object? set1, object? set2)
    {
        var result = new HashSet<object>(SharpTS.Runtime.Types.ReferenceEqualityComparer.Instance);
        if (set1 is HashSet<object> hashSet1)
        {
            foreach (var value in hashSet1)
                result.Add(value);
        }
        if (set2 is HashSet<object> hashSet2)
        {
            foreach (var value in hashSet2)
                result.Add(value);
        }
        return result;
    }

    /// <summary>
    /// Returns a new Set containing only elements present in both Sets.
    /// ES2025: Set.prototype.intersection()
    /// </summary>
    public static object SetIntersection(object? set1, object? set2)
    {
        var result = new HashSet<object>(SharpTS.Runtime.Types.ReferenceEqualityComparer.Instance);
        if (set1 is HashSet<object> hashSet1 && set2 is HashSet<object> hashSet2)
        {
            foreach (var value in hashSet1)
            {
                if (hashSet2.Contains(value))
                    result.Add(value);
            }
        }
        return result;
    }

    /// <summary>
    /// Returns a new Set containing elements in set1 but not in set2.
    /// ES2025: Set.prototype.difference()
    /// </summary>
    public static object SetDifference(object? set1, object? set2)
    {
        var result = new HashSet<object>(SharpTS.Runtime.Types.ReferenceEqualityComparer.Instance);
        if (set1 is HashSet<object> hashSet1)
        {
            var hashSet2 = set2 as HashSet<object>;
            foreach (var value in hashSet1)
            {
                if (hashSet2 == null || !hashSet2.Contains(value))
                    result.Add(value);
            }
        }
        return result;
    }

    /// <summary>
    /// Returns a new Set containing elements in either Set but not in both.
    /// ES2025: Set.prototype.symmetricDifference()
    /// </summary>
    public static object SetSymmetricDifference(object? set1, object? set2)
    {
        var result = new HashSet<object>(SharpTS.Runtime.Types.ReferenceEqualityComparer.Instance);
        var hashSet1 = set1 as HashSet<object>;
        var hashSet2 = set2 as HashSet<object>;

        if (hashSet1 != null)
        {
            foreach (var value in hashSet1)
            {
                if (hashSet2 == null || !hashSet2.Contains(value))
                    result.Add(value);
            }
        }
        if (hashSet2 != null)
        {
            foreach (var value in hashSet2)
            {
                if (hashSet1 == null || !hashSet1.Contains(value))
                    result.Add(value);
            }
        }
        return result;
    }

    /// <summary>
    /// Returns true if every element in set1 is also in set2.
    /// ES2025: Set.prototype.isSubsetOf()
    /// </summary>
    public static bool SetIsSubsetOf(object? set1, object? set2)
    {
        if (set1 is not HashSet<object> hashSet1)
            return true;  // Empty set is subset of everything
        if (set2 is not HashSet<object> hashSet2)
            return hashSet1.Count == 0;

        foreach (var value in hashSet1)
        {
            if (!hashSet2.Contains(value))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Returns true if every element in set2 is also in set1.
    /// ES2025: Set.prototype.isSupersetOf()
    /// </summary>
    public static bool SetIsSupersetOf(object? set1, object? set2)
    {
        if (set2 is not HashSet<object> hashSet2)
            return true;  // Everything is superset of empty set
        if (set1 is not HashSet<object> hashSet1)
            return hashSet2.Count == 0;

        foreach (var value in hashSet2)
        {
            if (!hashSet1.Contains(value))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Returns true if set1 and set2 have no elements in common.
    /// ES2025: Set.prototype.isDisjointFrom()
    /// </summary>
    public static bool SetIsDisjointFrom(object? set1, object? set2)
    {
        if (set1 is not HashSet<object> hashSet1 || set2 is not HashSet<object> hashSet2)
            return true;  // Empty sets are disjoint from everything

        // Iterate over the smaller set for efficiency
        if (hashSet1.Count <= hashSet2.Count)
        {
            foreach (var value in hashSet1)
            {
                if (hashSet2.Contains(value))
                    return false;
            }
        }
        else
        {
            foreach (var value in hashSet2)
            {
                if (hashSet1.Contains(value))
                    return false;
            }
        }
        return true;
    }

    #endregion

    #endregion
}

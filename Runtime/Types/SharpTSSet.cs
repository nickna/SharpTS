using System.Collections;
using SharpTS.TypeSystem;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime wrapper for TypeScript Set&lt;T&gt; collections.
/// </summary>
/// <remarks>
/// Wraps a HashSet with ReferenceEqualityComparer to match JavaScript Set semantics:
/// - Primitive values (string, number, boolean) are compared by value
/// - Object values are compared by reference (same object identity)
/// Methods match the JavaScript Set API: add, has, delete, clear, keys, values, entries, forEach.
/// </remarks>
public class SharpTSSet : ITypeCategorized, IEnumerable<object?>
{
    /// <inheritdoc />
    public TypeCategory RuntimeCategory => TypeCategory.Set;

    private readonly HashSet<object> _set;

    public SharpTSSet()
    {
        _set = new HashSet<object>(ReferenceEqualityComparer.Instance);
    }

    public SharpTSSet(IEnumerable<object> values) : this()
    {
        foreach (var value in values)
        {
            _set.Add(value);
        }
    }

    /// <summary>
    /// Creates a Set from an array of values (JavaScript constructor pattern).
    /// </summary>
    public static SharpTSSet FromArray(SharpTSArray valuesArray)
    {
        var set = new SharpTSSet();
        foreach (var value in valuesArray.Elements)
        {
            if (value != null)
            {
                set._set.Add(value);
            }
        }
        return set;
    }

    /// <summary>
    /// Gets the number of values in the Set.
    /// </summary>
    public int Size => _set.Count;

    /// <summary>
    /// Adds a value to the Set. Returns this Set for method chaining.
    /// </summary>
    public SharpTSSet Add(object value)
    {
        _set.Add(value);
        return this;
    }

    /// <summary>
    /// Returns true if the Set contains the specified value.
    /// </summary>
    public bool Has(object value)
    {
        return _set.Contains(value);
    }

    /// <summary>
    /// Removes the specified value from the Set. Returns true if the value was present.
    /// </summary>
    public bool Delete(object value)
    {
        return _set.Remove(value);
    }

    /// <summary>
    /// Removes all values from the Set.
    /// </summary>
    public void Clear()
    {
        _set.Clear();
    }

    /// <summary>
    /// Returns an iterator over the values (same as values() for Set).
    /// </summary>
    public SharpTSIterator Keys()
    {
        // For Set, keys() is the same as values()
        return Values();
    }

    /// <summary>
    /// Returns an iterator over the values.
    /// </summary>
    public SharpTSIterator Values()
    {
        return new SharpTSIterator(EnumerateValues());
    }

    /// <summary>
    /// Returns an iterator over [value, value] pairs (for Set API compatibility with Map).
    /// </summary>
    public SharpTSIterator Entries()
    {
        return new SharpTSIterator(EnumerateEntries());
    }

    /// <summary>
    /// Exposes the internal hash set for forEach implementation.
    /// </summary>
    internal IEnumerable<object> InternalValues => _set;

    #region ES2025 Set Operations

    /// <summary>
    /// Returns a new Set containing all elements from both this Set and the other Set.
    /// ES2025: Set.prototype.union()
    /// </summary>
    public SharpTSSet Union(SharpTSSet other)
    {
        var result = new SharpTSSet(_set);
        foreach (var value in other._set)
        {
            result._set.Add(value);
        }
        return result;
    }

    /// <summary>
    /// Returns a new Set containing only elements present in both this Set and the other Set.
    /// ES2025: Set.prototype.intersection()
    /// </summary>
    public SharpTSSet Intersection(SharpTSSet other)
    {
        var result = new SharpTSSet();
        foreach (var value in _set)
        {
            if (other._set.Contains(value))
            {
                result._set.Add(value);
            }
        }
        return result;
    }

    /// <summary>
    /// Returns a new Set containing elements in this Set but not in the other Set.
    /// ES2025: Set.prototype.difference()
    /// </summary>
    public SharpTSSet Difference(SharpTSSet other)
    {
        var result = new SharpTSSet();
        foreach (var value in _set)
        {
            if (!other._set.Contains(value))
            {
                result._set.Add(value);
            }
        }
        return result;
    }

    /// <summary>
    /// Returns a new Set containing elements in either Set but not in both.
    /// ES2025: Set.prototype.symmetricDifference()
    /// </summary>
    public SharpTSSet SymmetricDifference(SharpTSSet other)
    {
        var result = new SharpTSSet();
        // Add elements in this but not in other
        foreach (var value in _set)
        {
            if (!other._set.Contains(value))
            {
                result._set.Add(value);
            }
        }
        // Add elements in other but not in this
        foreach (var value in other._set)
        {
            if (!_set.Contains(value))
            {
                result._set.Add(value);
            }
        }
        return result;
    }

    /// <summary>
    /// Returns true if every element in this Set is also in the other Set.
    /// ES2025: Set.prototype.isSubsetOf()
    /// </summary>
    public bool IsSubsetOf(SharpTSSet other)
    {
        foreach (var value in _set)
        {
            if (!other._set.Contains(value))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Returns true if every element in the other Set is also in this Set.
    /// ES2025: Set.prototype.isSupersetOf()
    /// </summary>
    public bool IsSupersetOf(SharpTSSet other)
    {
        foreach (var value in other._set)
        {
            if (!_set.Contains(value))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Returns true if this Set and the other Set have no elements in common.
    /// ES2025: Set.prototype.isDisjointFrom()
    /// </summary>
    public bool IsDisjointFrom(SharpTSSet other)
    {
        // Iterate over the smaller set for efficiency
        if (_set.Count <= other._set.Count)
        {
            foreach (var value in _set)
            {
                if (other._set.Contains(value))
                {
                    return false;
                }
            }
        }
        else
        {
            foreach (var value in other._set)
            {
                if (_set.Contains(value))
                {
                    return false;
                }
            }
        }
        return true;
    }

    #endregion

    private IEnumerable<object?> EnumerateValues()
    {
        foreach (var value in _set)
            yield return value;
    }

    private IEnumerable<object?> EnumerateEntries()
    {
        // Set.entries() yields [value, value] pairs (per JS spec)
        foreach (var value in _set)
            yield return new SharpTSArray([value, value]);
    }

    /// <summary>
    /// Returns an enumerator over the values, matching JavaScript Set iteration semantics.
    /// This enables yield* and for...of to work with Set in compiled mode.
    /// </summary>
    public IEnumerator<object?> GetEnumerator() => EnumerateValues().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString()
    {
        var values = _set.Select(FormatValue);
        return $"Set({_set.Count}) {{ {string.Join(", ", values)} }}";
    }

    private static string FormatValue(object? value) => value switch
    {
        null => "undefined",
        string s => $"\"{s}\"",
        bool b => b ? "true" : "false",
        SharpTSArray arr => arr.ToString(),
        SharpTSObject obj => obj.ToString(),
        _ => value.ToString() ?? "undefined"
    };
}

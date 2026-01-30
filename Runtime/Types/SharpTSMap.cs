using SharpTS.TypeSystem;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime wrapper for TypeScript Map&lt;K, V&gt; collections.
/// </summary>
/// <remarks>
/// Wraps a Dictionary with ReferenceEqualityComparer to match JavaScript Map semantics:
/// - Primitive keys (string, number, boolean) are compared by value
/// - Object keys are compared by reference (same object identity)
/// Methods match the JavaScript Map API: get, set, has, delete, clear, keys, values, entries, forEach.
/// </remarks>
public class SharpTSMap : ITypeCategorized
{
    /// <inheritdoc />
    public TypeCategory RuntimeCategory => TypeCategory.Map;

    private readonly Dictionary<object, object?> _map;

    public SharpTSMap()
    {
        _map = new Dictionary<object, object?>(ReferenceEqualityComparer.Instance);
    }

    public SharpTSMap(IEnumerable<(object Key, object? Value)> entries) : this()
    {
        foreach (var (key, value) in entries)
        {
            _map[key] = value;
        }
    }

    /// <summary>
    /// Creates a Map from an array of [key, value] arrays (JavaScript constructor pattern).
    /// </summary>
    public static SharpTSMap FromEntries(SharpTSArray entriesArray)
    {
        var map = new SharpTSMap();
        foreach (var entry in entriesArray.Elements)
        {
            if (entry is SharpTSArray pair && pair.Elements.Count >= 2)
            {
                var key = pair.Elements[0];
                if (key != null)
                {
                    map._map[key] = pair.Elements[1];
                }
            }
        }
        return map;
    }

    /// <summary>
    /// Gets the number of key-value pairs in the Map.
    /// </summary>
    public int Size => _map.Count;

    /// <summary>
    /// Gets the value associated with the specified key, or undefined (null) if not found.
    /// </summary>
    public object? Get(object key)
    {
        return _map.TryGetValue(key, out var value) ? value : null;
    }

    /// <summary>
    /// Sets the value for the specified key. Returns this Map for method chaining.
    /// </summary>
    public SharpTSMap Set(object key, object? value)
    {
        _map[key] = value;
        return this;
    }

    /// <summary>
    /// Returns true if the Map contains the specified key.
    /// </summary>
    public bool Has(object key)
    {
        return _map.ContainsKey(key);
    }

    /// <summary>
    /// Removes the specified key from the Map. Returns true if the key was present.
    /// </summary>
    public bool Delete(object key)
    {
        return _map.Remove(key);
    }

    /// <summary>
    /// Removes all key-value pairs from the Map.
    /// </summary>
    public void Clear()
    {
        _map.Clear();
    }

    /// <summary>
    /// Returns an iterator over the keys in insertion order.
    /// </summary>
    public SharpTSIterator Keys()
    {
        return new SharpTSIterator(EnumerateKeys());
    }

    /// <summary>
    /// Returns an iterator over the values in insertion order.
    /// </summary>
    public SharpTSIterator Values()
    {
        return new SharpTSIterator(EnumerateValues());
    }

    /// <summary>
    /// Returns an iterator over [key, value] pairs in insertion order.
    /// </summary>
    public SharpTSIterator Entries()
    {
        return new SharpTSIterator(EnumerateEntries());
    }

    /// <summary>
    /// Exposes the internal dictionary for forEach implementation.
    /// </summary>
    internal IEnumerable<KeyValuePair<object, object?>> InternalEntries => _map;

    private IEnumerable<object?> EnumerateKeys()
    {
        foreach (var key in _map.Keys)
            yield return key;
    }

    private IEnumerable<object?> EnumerateValues()
    {
        foreach (var value in _map.Values)
            yield return value;
    }

    private IEnumerable<object?> EnumerateEntries()
    {
        foreach (var kvp in _map)
            yield return new SharpTSArray([kvp.Key, kvp.Value]);
    }

    public override string ToString()
    {
        var entries = _map.Select(kvp =>
            $"{FormatValue(kvp.Key)} => {FormatValue(kvp.Value)}");
        return $"Map({_map.Count}) {{ {string.Join(", ", entries)} }}";
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

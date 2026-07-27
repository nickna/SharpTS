using System.Collections;
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
public class SharpTSMap : ITypeCategorized, IEnumerable<object?>
{
    /// <inheritdoc />
    public TypeCategory RuntimeCategory => TypeCategory.Map;

    /// <summary>
    /// Sentinel object used as dictionary key when the JavaScript key is null/undefined.
    /// JavaScript Maps support null and undefined as keys, but C# Dictionary requires non-null keys.
    /// </summary>
    private static readonly object NullSentinel = new();

    private static object NormalizeKey(object? key) => key ?? NullSentinel;

    /// <summary>
    /// Reverses <see cref="NormalizeKey"/>: turns the internal null-sentinel back into a real
    /// null so the sentinel never escapes the Map (keys(), entries(), forEach, for...of,
    /// spread, console.log). undefined is stored as its own value, so it passes through.
    /// </summary>
    private static object? DenormalizeKey(object key) => ReferenceEquals(key, NullSentinel) ? null : key;

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
        foreach (var entry in entriesArray)
        {
            if (entry is SharpTSArray pair && pair.Length >= 2)
            {
                map._map[NormalizeKey(pair[0])] = pair[1];
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
    /// Accepts null to match JavaScript Map semantics where undefined is a valid key.
    /// </summary>
    public object? Get(object? key)
    {
        return _map.TryGetValue(NormalizeKey(key), out var value) ? value : null;
    }

    /// <summary>
    /// Sets the value for the specified key. Returns this Map for method chaining.
    /// Accepts null to match JavaScript Map semantics where undefined is a valid key.
    /// </summary>
    public SharpTSMap Set(object? key, object? value)
    {
        _map[NormalizeKey(key)] = value;
        return this;
    }

    /// <summary>
    /// Returns true if the Map contains the specified key.
    /// Accepts null to match JavaScript Map semantics where undefined is a valid key.
    /// </summary>
    public bool Has(object? key)
    {
        return _map.ContainsKey(NormalizeKey(key));
    }

    /// <summary>
    /// Removes the specified key from the Map. Returns true if the key was present.
    /// Accepts null to match JavaScript Map semantics where undefined is a valid key.
    /// </summary>
    public bool Delete(object? key)
    {
        return _map.Remove(NormalizeKey(key));
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
    internal IEnumerable<(object? Key, object? Value)> InternalEntries =>
        _map.Select(kvp => (DenormalizeKey(kvp.Key), kvp.Value));

    private IEnumerable<object?> EnumerateKeys()
    {
        foreach (var key in _map.Keys)
            yield return DenormalizeKey(key);
    }

    private IEnumerable<object?> EnumerateValues()
    {
        foreach (var value in _map.Values)
            yield return value;
    }

    private IEnumerable<object?> EnumerateEntries()
    {
        foreach (var kvp in _map)
            yield return new SharpTSArray([DenormalizeKey(kvp.Key), kvp.Value]);
    }

    /// <summary>
    /// Returns an enumerator over [key, value] pairs, matching JavaScript Map iteration semantics.
    /// This enables yield* and for...of to work with Map in compiled mode.
    /// </summary>
    public IEnumerator<object?> GetEnumerator() => EnumerateEntries().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString()
    {
        var entries = _map.Select(kvp =>
            $"{CollectionInspect.FormatValue(DenormalizeKey(kvp.Key))} => {CollectionInspect.FormatValue(kvp.Value)}");
        return $"Map({_map.Count}) {{ {string.Join(", ", entries)} }}";
    }

}

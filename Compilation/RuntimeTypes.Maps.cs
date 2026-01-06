using SharpTS.Runtime.Types;

namespace SharpTS.Compilation;

public static partial class RuntimeTypes
{
    #region Maps

    /// <summary>
    /// Creates a new empty Map.
    /// </summary>
    public static object CreateMap() => new Dictionary<object, object?>(SharpTS.Runtime.Types.ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Creates a Map from an array of [key, value] entries.
    /// </summary>
    public static object CreateMapFromEntries(object? entries)
    {
        var map = new Dictionary<object, object?>(SharpTS.Runtime.Types.ReferenceEqualityComparer.Instance);
        if (entries is List<object?> list)
        {
            foreach (var entry in list)
            {
                if (entry is List<object?> pair && pair.Count >= 2)
                {
                    var key = pair[0];
                    var value = pair[1];
                    if (key != null)
                    {
                        map[key] = value;
                    }
                }
            }
        }
        return map;
    }

    /// <summary>
    /// Gets the size of a Map.
    /// </summary>
    public static double MapSize(object? map)
    {
        if (map is Dictionary<object, object?> dict)
            return dict.Count;
        return 0;
    }

    /// <summary>
    /// Gets a value from a Map by key.
    /// </summary>
    public static object? MapGet(object? map, object? key)
    {
        if (map is Dictionary<object, object?> dict && key != null)
        {
            return dict.TryGetValue(key, out var value) ? value : null;
        }
        return null;
    }

    /// <summary>
    /// Sets a value in a Map. Returns the Map for chaining.
    /// </summary>
    public static object MapSet(object? map, object? key, object? value)
    {
        if (map is Dictionary<object, object?> dict && key != null)
        {
            dict[key] = value;
        }
        return map!;
    }

    /// <summary>
    /// Checks if a Map has a key.
    /// </summary>
    public static bool MapHas(object? map, object? key)
    {
        if (map is Dictionary<object, object?> dict && key != null)
        {
            return dict.ContainsKey(key);
        }
        return false;
    }

    /// <summary>
    /// Deletes a key from a Map. Returns true if key existed.
    /// </summary>
    public static bool MapDelete(object? map, object? key)
    {
        if (map is Dictionary<object, object?> dict && key != null)
        {
            return dict.Remove(key);
        }
        return false;
    }

    /// <summary>
    /// Clears all entries from a Map.
    /// </summary>
    public static void MapClear(object? map)
    {
        if (map is Dictionary<object, object?> dict)
        {
            dict.Clear();
        }
    }

    /// <summary>
    /// Returns an iterator over the Map's keys.
    /// </summary>
    public static List<object?> MapKeys(object? map)
    {
        if (map is Dictionary<object, object?> dict)
        {
            return dict.Keys.Select(k => (object?)k).ToList();
        }
        return [];
    }

    /// <summary>
    /// Returns an iterator over the Map's values.
    /// </summary>
    public static List<object?> MapValues(object? map)
    {
        if (map is Dictionary<object, object?> dict)
        {
            return dict.Values.ToList();
        }
        return [];
    }

    /// <summary>
    /// Returns an iterator over [key, value] entries.
    /// </summary>
    public static List<object?> MapEntries(object? map)
    {
        if (map is Dictionary<object, object?> dict)
        {
            return dict.Select(kvp => (object?)new List<object?> { kvp.Key, kvp.Value }).ToList();
        }
        return [];
    }

    /// <summary>
    /// Executes a callback for each Map entry.
    /// </summary>
    public static void MapForEach(object? map, object? callback)
    {
        if (map is Dictionary<object, object?> dict && callback != null)
        {
            foreach (var kvp in dict)
            {
                InvokeValue(callback, [kvp.Value, kvp.Key, map]);
            }
        }
    }

    #endregion
}

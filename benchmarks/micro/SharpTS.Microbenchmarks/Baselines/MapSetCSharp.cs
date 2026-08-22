namespace SharpTS.Microbenchmarks.Baselines;

/// <summary>
/// Native and boxed C# controls for the shared numeric Map/Set workloads.
/// The boxed iteration controls deliberately reproduce the compiled runtime's
/// key/value snapshots and entry-pair materialization.
/// </summary>
public static class MapSetCSharp
{
    public static double IdiomaticMapOperations(int n)
    {
        var map = new Dictionary<double, double>(n);
        for (int i = 0; i < n; i++)
            map[i] = i * 3 + 1;

        double sum = 0;
        for (int i = 0; i < n; i++)
        {
            if (map.TryGetValue(i, out double value))
                sum += value;
        }

        int deleted = 0;
        for (int i = 0; i < n; i += 2)
        {
            if (map.Remove(i))
                deleted++;
        }
        return sum + deleted + map.Count;
    }

    public static double IdiomaticMapIteration(int n)
    {
        var map = new Dictionary<double, double>(n);
        for (int i = 0; i < n; i++)
            map[i] = i * 3 + 1;

        double sum = 0;
        foreach (var entry in map)
            sum += entry.Key + entry.Value;
        return sum + map.Count;
    }

    public static double IdiomaticSetOperations(int n)
    {
        var set = new HashSet<double>(n);
        for (int i = 0; i < n; i++)
            set.Add(i);

        int found = 0;
        for (int i = 0; i < n; i++)
        {
            if (set.Contains(i))
                found++;
        }

        int deleted = 0;
        for (int i = 0; i < n; i += 2)
        {
            if (set.Remove(i))
                deleted++;
        }
        return found + deleted + set.Count;
    }

    public static double IdiomaticSetIteration(int n)
    {
        var set = new HashSet<double>(n);
        for (int i = 0; i < n; i++)
            set.Add(i);

        double sum = 0;
        foreach (double value in set)
            sum += value;
        return sum + set.Count;
    }

    public static object EquivalentMapOperations(double n)
    {
        int count = (int)n;
        var map = new Dictionary<object, object?>(count);
        for (int i = 0; i < count; i++)
            map[(double)i] = (double)(i * 3 + 1);

        double sum = 0;
        for (int i = 0; i < count; i++)
        {
            if (map.TryGetValue((double)i, out object? value))
                sum += Convert.ToDouble(value);
        }

        int deleted = 0;
        for (int i = 0; i < count; i += 2)
        {
            if (map.Remove((double)i))
                deleted++;
        }
        return sum + deleted + map.Count;
    }

    public static object EquivalentMapIteration(double n)
    {
        int count = (int)n;
        var map = new Dictionary<object, object?>(count);
        for (int i = 0; i < count; i++)
            map[(double)i] = (double)(i * 3 + 1);

        var keys = new List<object>(map.Keys);
        double sum = 0;
        foreach (object key in keys)
        {
            if (!map.TryGetValue(key, out object? value))
                continue;

            var pair = new List<object?>(2) { key, value };
            sum += Convert.ToDouble(pair[0]) + Convert.ToDouble(pair[1]);
        }
        return sum + map.Count;
    }

    public static object EquivalentSetOperations(double n)
    {
        int count = (int)n;
        var set = new HashSet<object>();
        for (int i = 0; i < count; i++)
            set.Add((double)i);

        int found = 0;
        for (int i = 0; i < count; i++)
        {
            if (set.Contains((double)i))
                found++;
        }

        int deleted = 0;
        for (int i = 0; i < count; i += 2)
        {
            if (set.Remove((double)i))
                deleted++;
        }
        return (double)(found + deleted + set.Count);
    }

    public static object EquivalentSetIteration(double n)
    {
        int count = (int)n;
        var set = new HashSet<object>();
        for (int i = 0; i < count; i++)
            set.Add((double)i);

        var values = new List<object>(set);
        double sum = 0;
        foreach (object value in values)
        {
            if (set.Contains(value))
                sum += Convert.ToDouble(value);
        }
        return sum + set.Count;
    }
}

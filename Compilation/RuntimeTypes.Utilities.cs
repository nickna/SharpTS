namespace SharpTS.Compilation;

public static partial class RuntimeTypes
{
    #region Math

    public static double Random()
    {
        return _random.NextDouble();
    }

    #endregion

    #region Enums

    /// <summary>
    /// Get enum member name by value with caching.
    /// Keys and values arrays define the reverse mapping (passed once, cached by enumName).
    /// </summary>
    public static string GetEnumMemberName(string enumName, double value, double[] keys, string[] values)
    {
        if (!_enumReverseCache.TryGetValue(enumName, out var reverse))
        {
            reverse = new Dictionary<double, string>();
            for (int i = 0; i < keys.Length; i++)
            {
                reverse[keys[i]] = values[i];
            }
            _enumReverseCache[enumName] = reverse;
        }

        return reverse.TryGetValue(value, out var name) ? name : throw new Exception($"Value {value} not found in enum '{enumName}'");
    }

    #endregion

    #region Template Literals

    public static string ConcatTemplate(object?[] parts)
    {
        return string.Concat(parts.Select(Stringify));
    }

    #endregion
}

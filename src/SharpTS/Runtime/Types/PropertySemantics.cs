namespace SharpTS.Runtime.Types;

/// <summary>
/// Property descriptor comparison and canonical index semantics for interpreter runtime values.
/// Compiled programs use their separately emitted runtime implementations.
/// </summary>
internal static class PropertySemantics
{
    internal static bool TryGetArrayIndex(string key, out uint index)
    {
        // ECMA-262 array indices use the canonical decimal spelling of a
        // uint32 other than 2^32-1. Keep ordinary names such as "01", "+1",
        // and whitespace-padded numbers out of indexed storage.
        if (key.Length == 0
            || key[0] is < '0' or > '9'
            || (key.Length > 1 && key[0] == '0'))
        {
            index = 0;
            return false;
        }

        return uint.TryParse(
                key,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out index)
            && index < uint.MaxValue;
    }

    /// <summary>
    /// ECMA-262 SameValue comparison used by descriptor validation.
    /// Numbers keep NaN equal to itself and distinguish positive from negative zero;
    /// runtime objects and callables retain their reference identity.
    /// </summary>
    internal static bool SameValue(object? left, object? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is double ld && right is double rd)
        {
            if (double.IsNaN(ld) && double.IsNaN(rd)) return true;
            if (ld == 0 && rd == 0)
                return BitConverter.DoubleToInt64Bits(ld)
                    == BitConverter.DoubleToInt64Bits(rd);
            return ld.Equals(rd);
        }
        return left?.Equals(right) == true;
    }
}

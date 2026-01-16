using SharpTS.Runtime.Types;

namespace SharpTS.Compilation;

public static partial class RuntimeTypes
{
    #region Operators

    public static object Add(object? left, object? right)
    {
        // String concatenation if either operand is a string
        if (left is string || right is string)
        {
            // Use string.Concat and avoid Stringify for values already strings
            return string.Concat(
                left as string ?? Stringify(left),
                right as string ?? Stringify(right));
        }
        return ToNumber(left) + ToNumber(right);
    }

    /// <summary>
    /// Loose equality (==) - null and undefined are equal to each other.
    /// </summary>
    public static new bool Equals(object? left, object? right)
    {
        // null == null, undefined == undefined, null == undefined (loose equality)
        bool leftNullish = left == null || left is SharpTSUndefined;
        bool rightNullish = right == null || right is SharpTSUndefined;
        if (leftNullish && rightNullish) return true;
        if (leftNullish || rightNullish) return false;

        // Same type comparison
        if (left!.GetType() == right!.GetType())
        {
            return left.Equals(right);
        }

        // Numeric comparison
        if (IsNumeric(left) && IsNumeric(right))
        {
            return ToNumber(left) == ToNumber(right);
        }

        return left.Equals(right);
    }

    /// <summary>
    /// Strict equality (===) - null and undefined are NOT equal to each other.
    /// </summary>
    public static bool StrictEquals(object? left, object? right)
    {
        // null === null and undefined === undefined, but NOT null === undefined
        if (left == null && right == null) return true;
        if (left is SharpTSUndefined && right is SharpTSUndefined) return true;
        if (left == null || right == null || left is SharpTSUndefined || right is SharpTSUndefined) return false;

        // Same type comparison
        if (left.GetType() != right.GetType()) return false;
        return left.Equals(right);
    }

    private static bool IsNumeric(object? value) =>
        value is double or int or long;

    #endregion
}

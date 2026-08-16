namespace SharpTS.Compilation;

public static partial class RuntimeTypes
{
    /// <summary>Helper to check if a value is SharpTSUndefined.</summary>
    private static bool IsUndefined(object? value) =>
        value is SharpTS.Runtime.Types.SharpTSUndefined;

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
        bool leftNullish = left == null || IsUndefined(left);
        bool rightNullish = right == null || IsUndefined(right);
        if (leftNullish && rightNullish) return true;
        if (leftNullish || rightNullish) return false;

        // Same type comparison
        if (left!.GetType() == right!.GetType())
        {
            // NaN is never loosely equal to itself (same-type == reduces to ===).
            if (left is double dl && right is double dr && (double.IsNaN(dl) || double.IsNaN(dr)))
                return false;
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
        if (IsUndefined(left) && IsUndefined(right)) return true;
        if (left == null || right == null || IsUndefined(left) || IsUndefined(right)) return false;

        // Same type comparison
        if (left.GetType() != right.GetType()) return false;
        // ECMA-262 7.2.16 IsStrictlyEqual: NaN is never equal to anything, including
        // itself. Object.Equals defers to Double.Equals which treats NaN as equal to
        // itself, so guard explicitly — matches the interpreter's IsStrictEqual and the
        // emitted $Runtime.StrictEquals (see RuntimeEmitter.CoreUtilities.cs).
        if (left is double dl && right is double dr && (double.IsNaN(dl) || double.IsNaN(dr)))
            return false;
        return left.Equals(right);
    }

    private static bool IsNumeric(object? value) =>
        value is double or int or long;

    #endregion
}

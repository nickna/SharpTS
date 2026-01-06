namespace SharpTS.Compilation;

public static partial class RuntimeTypes
{
    #region Operators

    public static object Add(object? left, object? right)
    {
        // String concatenation if either operand is a string
        if (left is string || right is string)
        {
            return Stringify(left) + Stringify(right);
        }
        return ToNumber(left) + ToNumber(right);
    }

    public static new bool Equals(object? left, object? right)
    {
        if (left == null && right == null) return true;
        if (left == null || right == null) return false;

        // Same type comparison
        if (left.GetType() == right.GetType())
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

    private static bool IsNumeric(object? value) =>
        value is double or int or long;

    #endregion
}

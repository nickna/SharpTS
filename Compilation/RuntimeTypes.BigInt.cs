using System.Numerics;

namespace SharpTS.Compilation;

public static partial class RuntimeTypes
{
    #region BigInt Operations

    public static object CreateBigInt(object? value) => value switch
    {
        BigInteger bi => bi,
        double d => new BigInteger(d),
        string s when s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) =>
            BigInteger.Parse(s[2..], System.Globalization.NumberStyles.HexNumber),
        string s when s.StartsWith("-0x", StringComparison.OrdinalIgnoreCase) =>
            -BigInteger.Parse(s[3..], System.Globalization.NumberStyles.HexNumber),
        string s => BigInteger.Parse(s),
        _ => throw new Exception($"Cannot convert {value?.GetType().Name ?? "null"} to bigint")
    };

    public static object BigIntAdd(object? left, object? right) =>
        (BigInteger)left! + (BigInteger)right!;

    public static object BigIntSubtract(object? left, object? right) =>
        (BigInteger)left! - (BigInteger)right!;

    public static object BigIntMultiply(object? left, object? right) =>
        (BigInteger)left! * (BigInteger)right!;

    public static object BigIntDivide(object? left, object? right) =>
        BigInteger.Divide((BigInteger)left!, (BigInteger)right!);

    public static object BigIntRemainder(object? left, object? right) =>
        BigInteger.Remainder((BigInteger)left!, (BigInteger)right!);

    public static object BigIntPow(object? baseValue, object? exponent)
    {
        var exp = (BigInteger)exponent!;
        if (exp < 0)
            throw new Exception("Runtime Error: BigInt exponent must be non-negative.");
        if (exp > int.MaxValue)
            throw new Exception("Runtime Error: BigInt exponent too large.");
        return BigInteger.Pow((BigInteger)baseValue!, (int)exp);
    }

    public static object BigIntNegate(object? value) =>
        BigInteger.Negate((BigInteger)value!);

    public static object BigIntBitwiseAnd(object? left, object? right) =>
        (BigInteger)left! & (BigInteger)right!;

    public static object BigIntBitwiseOr(object? left, object? right) =>
        (BigInteger)left! | (BigInteger)right!;

    public static object BigIntBitwiseXor(object? left, object? right) =>
        (BigInteger)left! ^ (BigInteger)right!;

    public static object BigIntBitwiseNot(object? value) =>
        ~(BigInteger)value!;

    public static object BigIntLeftShift(object? left, object? right) =>
        (BigInteger)left! << (int)(BigInteger)right!;

    public static object BigIntRightShift(object? left, object? right) =>
        (BigInteger)left! >> (int)(BigInteger)right!;

    public static bool BigIntEquals(object? left, object? right) =>
        (BigInteger)left! == (BigInteger)right!;

    public static bool BigIntLessThan(object? left, object? right) =>
        (BigInteger)left! < (BigInteger)right!;

    public static bool BigIntLessThanOrEqual(object? left, object? right) =>
        (BigInteger)left! <= (BigInteger)right!;

    public static bool BigIntGreaterThan(object? left, object? right) =>
        (BigInteger)left! > (BigInteger)right!;

    public static bool BigIntGreaterThanOrEqual(object? left, object? right) =>
        (BigInteger)left! >= (BigInteger)right!;

    public static bool IsBigInt(object? value) =>
        value is BigInteger;

    #endregion
}

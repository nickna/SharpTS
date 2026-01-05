using System.Globalization;
using System.Numerics;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of JavaScript/TypeScript bigint values.
/// </summary>
/// <remarks>
/// Wraps System.Numerics.BigInteger to provide TypeScript bigint semantics.
/// Supports arbitrary precision integers with full arithmetic operations.
/// </remarks>
public class SharpTSBigInt
{
    public BigInteger Value { get; }

    public SharpTSBigInt(BigInteger value)
    {
        Value = value;
    }

    public SharpTSBigInt(double value)
    {
        // Truncate to integer (like JavaScript's BigInt(number))
        Value = new BigInteger(value);
    }

    public SharpTSBigInt(string value)
    {
        // Handle hex strings (0x...) and decimal strings
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("0X", StringComparison.OrdinalIgnoreCase))
        {
            // Prepend "0" to ensure positive interpretation (BigInteger.Parse treats hex as signed)
            Value = BigInteger.Parse("0" + value[2..], NumberStyles.HexNumber);
        }
        else if (value.StartsWith("-0x", StringComparison.OrdinalIgnoreCase) ||
                 value.StartsWith("-0X", StringComparison.OrdinalIgnoreCase))
        {
            Value = -BigInteger.Parse("0" + value[3..], NumberStyles.HexNumber);
        }
        else
        {
            Value = BigInteger.Parse(value);
        }
    }

    public override bool Equals(object? obj)
    {
        if (obj is SharpTSBigInt other)
            return Value == other.Value;
        if (obj is BigInteger bi)
            return Value == bi;
        return false;
    }

    public override int GetHashCode() => Value.GetHashCode();

    public override string ToString() => $"{Value}n";

    // Implicit conversions
    public static implicit operator SharpTSBigInt(BigInteger value) => new(value);
    public static implicit operator BigInteger(SharpTSBigInt bigInt) => bigInt.Value;

    // Arithmetic operators
    public static SharpTSBigInt operator +(SharpTSBigInt a, SharpTSBigInt b) => new(a.Value + b.Value);
    public static SharpTSBigInt operator -(SharpTSBigInt a, SharpTSBigInt b) => new(a.Value - b.Value);
    public static SharpTSBigInt operator *(SharpTSBigInt a, SharpTSBigInt b) => new(a.Value * b.Value);
    public static SharpTSBigInt operator /(SharpTSBigInt a, SharpTSBigInt b) => new(BigInteger.Divide(a.Value, b.Value));
    public static SharpTSBigInt operator %(SharpTSBigInt a, SharpTSBigInt b) => new(BigInteger.Remainder(a.Value, b.Value));
    public static SharpTSBigInt operator -(SharpTSBigInt a) => new(-a.Value);

    // Bitwise operators
    public static SharpTSBigInt operator &(SharpTSBigInt a, SharpTSBigInt b) => new(a.Value & b.Value);
    public static SharpTSBigInt operator |(SharpTSBigInt a, SharpTSBigInt b) => new(a.Value | b.Value);
    public static SharpTSBigInt operator ^(SharpTSBigInt a, SharpTSBigInt b) => new(a.Value ^ b.Value);
    public static SharpTSBigInt operator ~(SharpTSBigInt a) => new(~a.Value);
    public static SharpTSBigInt operator <<(SharpTSBigInt a, int shift) => new(a.Value << shift);
    public static SharpTSBigInt operator >>(SharpTSBigInt a, int shift) => new(a.Value >> shift);

    // Comparison operators
    public static bool operator ==(SharpTSBigInt a, SharpTSBigInt b) => a.Value == b.Value;
    public static bool operator !=(SharpTSBigInt a, SharpTSBigInt b) => a.Value != b.Value;
    public static bool operator <(SharpTSBigInt a, SharpTSBigInt b) => a.Value < b.Value;
    public static bool operator >(SharpTSBigInt a, SharpTSBigInt b) => a.Value > b.Value;
    public static bool operator <=(SharpTSBigInt a, SharpTSBigInt b) => a.Value <= b.Value;
    public static bool operator >=(SharpTSBigInt a, SharpTSBigInt b) => a.Value >= b.Value;

    // Power operation (exponent must be non-negative and fit in int)
    public static SharpTSBigInt Pow(SharpTSBigInt baseValue, SharpTSBigInt exponent)
    {
        if (exponent.Value < 0)
            throw new Exception("Runtime Error: BigInt exponent must be non-negative.");
        if (exponent.Value > int.MaxValue)
            throw new Exception("Runtime Error: BigInt exponent too large.");
        return new SharpTSBigInt(BigInteger.Pow(baseValue.Value, (int)exponent.Value));
    }
}

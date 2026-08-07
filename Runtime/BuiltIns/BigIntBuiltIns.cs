using SharpTS.Execution;
using System.Globalization;
using System.Numerics;
using System.Text;
using SharpTS.Runtime.Types;
using SharpTS.Runtime.Exceptions;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Implements the <c>BigInt.prototype</c> instance surface (toString, valueOf,
/// toLocaleString) for the interpreter. BigInt values are primitives, so the
/// interpreter has no boxed wrapper object to hang methods off — property access
/// on a <see cref="SharpTSBigInt"/> routes here via the
/// <see cref="BuiltInRegistry"/> category dispatch (TypeCategory.BigInt).
/// </summary>
/// <remarks>
/// Mirrors <see cref="NumberBuiltIns"/> for the radix-aware toString. The JS-correct
/// string form of a bigint is the bare numeric form ("42", NOT the "42n" debug form
/// used by console.log / util.inspect).
/// </remarks>
public static class BigIntBuiltIns
{
    private static readonly BuiltInMethod _asUintN = BuiltInMethod.CreateV2(
        "asUintN", 0, int.MaxValue, (interpreter, _, args) =>
            RuntimeValue.FromBigInt(Truncate(interpreter, args, signed: false)))
        .WithSpecLength(2)
        .AsNonConstructor();

    private static readonly BuiltInMethod _asIntN = BuiltInMethod.CreateV2(
        "asIntN", 0, int.MaxValue, (interpreter, _, args) =>
            RuntimeValue.FromBigInt(Truncate(interpreter, args, signed: true)))
        .WithSpecLength(2)
        .AsNonConstructor();

    public static object? GetStaticMember(string name) => name switch
    {
        "asUintN" => _asUintN,
        "asIntN" => _asIntN,
        _ => null,
    };

    private static SharpTSBigInt Truncate(
        Interpreter interpreter, ReadOnlySpan<RuntimeValue> args, bool signed)
    {
        object? bitsValue = args.Length > 0
            ? args[0].ToObject()
            : SharpTSUndefined.Instance;
        double bitsNumber = interpreter.ToNumberWithPrimitive(bitsValue);
        double bitsInteger = double.IsNaN(bitsNumber) ? 0 : Math.Truncate(bitsNumber);
        if (bitsInteger < 0 || double.IsInfinity(bitsInteger)
            || bitsInteger > int.MaxValue)
        {
            throw new ThrowException(new SharpTSRangeError(
                "BigInt bit width is outside the supported index range"));
        }

        object? bigintValue = args.Length > 1
            ? args[1].ToObject()
            : SharpTSUndefined.Instance;
        object? primitive = bigintValue is SharpTSObject or SharpTSArray or SharpTSInstance
            ? interpreter.ToPrimitiveForBuiltIn(bigintValue)
            : bigintValue;
        BigInteger value = primitive switch
        {
            SharpTSBigInt bigint => bigint.Value,
            BigInteger raw => raw,
            bool boolean => boolean ? BigInteger.One : BigInteger.Zero,
            string text => ParseBigIntString(text),
            _ => throw new ThrowException(new SharpTSTypeError(
                "BigInt value is required")),
        };

        int bits = (int)bitsInteger;
        if (bits == 0) return new SharpTSBigInt(BigInteger.Zero);
        BigInteger modulus = BigInteger.One << bits;
        BigInteger truncated = ((value % modulus) + modulus) % modulus;
        if (signed && truncated >= (BigInteger.One << (bits - 1)))
            truncated -= modulus;
        return new SharpTSBigInt(truncated);
    }

    private static BigInteger ParseBigIntString(string text)
    {
        string value = text.Trim();
        if (value.Length == 0) return BigInteger.Zero;
        try
        {
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return BigInteger.Parse("0" + value[2..], NumberStyles.HexNumber);
            if (value.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
                return ParseRadix(value.AsSpan(2), 2);
            if (value.StartsWith("0o", StringComparison.OrdinalIgnoreCase))
                return ParseRadix(value.AsSpan(2), 8);
            return BigInteger.Parse(value, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            throw new ThrowException(new SharpTSSyntaxError(
                "Cannot convert string to BigInt"));
        }
    }

    private static BigInteger ParseRadix(ReadOnlySpan<char> digits, int radix)
    {
        if (digits.Length == 0) throw new FormatException();
        BigInteger result = BigInteger.Zero;
        foreach (char digit in digits)
        {
            int value = digit - '0';
            if (value < 0 || value >= radix) throw new FormatException();
            result = result * radix + value;
        }
        return result;
    }

    /// <summary>
    /// Gets an instance member for a bigint value (BigInt.prototype surface).
    /// Accepts either the interpreter wrapper (<see cref="SharpTSBigInt"/>) or a raw
    /// <see cref="BigInteger"/> as the receiver.
    /// </summary>
    /// <param name="receiver">The receiver bigint value.</param>
    /// <param name="name">The member name (e.g., "toString", "valueOf").</param>
    /// <returns>A bound method, or null if the member is not defined.</returns>
    public static object? GetInstanceMember(object receiver, string name)
    {
        BigInteger value = receiver switch
        {
            SharpTSBigInt bi => bi.Value,
            BigInteger raw => raw,
            _ => default
        };

        return name switch
        {
            // BigInt.prototype.toString([radix]) — ECMA-262 21.2.3.3.
            "toString" => BuiltInMethod.CreateV2("toString", 0, 1, (interpreter, _, args) =>
            {
                int radix = 10;
                if (args.Length > 0 && !args[0].IsUndefined)
                {
                    double number = interpreter.ToNumberWithPrimitive(args[0].ToObject());
                    radix = double.IsNaN(number) ? 0 : (int)Math.Truncate(number);
                    if (radix < 2 || radix > 36)
                        throw new ThrowException(new SharpTSRangeError(
                            "toString() radix must be between 2 and 36"));
                }
                return RuntimeValue.FromString(ToStringWithRadix(value, radix));
            }).AsNonConstructor(),

            // BigInt.prototype.toLocaleString() — no Intl options support; decimal form.
            "toLocaleString" => BuiltInMethod.CreateV2("toLocaleString", 0, 1, (_, _, _) =>
                RuntimeValue.FromString(value.ToString(CultureInfo.InvariantCulture))).AsNonConstructor(),

            // BigInt.prototype.valueOf() — returns the bigint itself.
            "valueOf" => BuiltInMethod.CreateV2("valueOf", 0, (_, _, _) =>
                RuntimeValue.FromBigInt(new SharpTSBigInt(value))).AsNonConstructor(),

            _ => null
        };
    }

    /// <summary>
    /// Converts a bigint to its JS string form in the given radix (2–36), lowercase
    /// digits with a leading '-' for negatives. Radix 10 is the bare decimal form.
    /// </summary>
    internal static string ToStringWithRadix(BigInteger value, int radix)
    {
        if (radix == 10) return value.ToString(CultureInfo.InvariantCulture);
        if (value.IsZero) return "0";

        const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
        bool negative = value.Sign < 0;
        BigInteger abs = BigInteger.Abs(value);
        BigInteger r = radix;
        var sb = new StringBuilder();
        while (abs > 0)
        {
            abs = BigInteger.DivRem(abs, r, out BigInteger rem);
            sb.Insert(0, digits[(int)rem]);
        }
        if (negative) sb.Insert(0, '-');
        return sb.ToString();
    }
}

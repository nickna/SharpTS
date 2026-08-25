using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

public static partial class RuntimeTypes
{
    /// <summary>
    /// ECMA-262 Number.prototype.toFixed formatting. Unlike the BCL fixed-point
    /// format, JavaScript resolves an exact halfway case toward the larger
    /// decimal integer rather than toward the even integer. The standalone
    /// emitted twin in RuntimeEmitter.Number.cs must stay algorithmically aligned.
    /// </summary>
    internal static string FormatNumberFixed(double value, int fractionDigits)
    {
        if (double.IsNaN(value)) return "NaN";
        if (double.IsPositiveInfinity(value)) return "Infinity";
        if (double.IsNegativeInfinity(value)) return "-Infinity";
        if (Math.Abs(value) >= 1e21) return FormatNumber(value);

        bool negative = value < 0;
        ulong bits = (ulong)BitConverter.DoubleToInt64Bits(value) & 0x7fff_ffff_ffff_ffffUL;
        ulong fraction = bits & 0x000f_ffff_ffff_ffffUL;
        int rawExponent = (int)(bits >> 52);
        ulong significand = rawExponent == 0
            ? fraction
            : fraction | 0x0010_0000_0000_0000UL;
        int binaryExponent = rawExponent == 0 ? -1074 : rawExponent - 1023 - 52;

        if (TryScaleToUInt64(significand, binaryExponent, fractionDigits, out ulong scaled))
            return FormatScaledUInt64(scaled, fractionDigits, negative);

        return FormatScaledBigInteger(
            significand, binaryExponent, fractionDigits, negative);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryScaleToUInt64(
        ulong significand,
        int binaryExponent,
        int fractionDigits,
        out ulong scaled)
    {
        ulong numerator = significand;
        for (int i = 0; i < fractionDigits; i++)
        {
            if (numerator > ulong.MaxValue / 5)
            {
                scaled = 0;
                return false;
            }
            numerator *= 5;
        }

        int shift = binaryExponent + fractionDigits;
        if (shift >= 0)
        {
            if (shift >= 64 || numerator > (ulong.MaxValue >> shift))
            {
                scaled = 0;
                return false;
            }
            scaled = numerator << shift;
            return true;
        }

        int rightShift = -shift;
        if (rightShift > 64)
        {
            scaled = 0;
            return true;
        }
        if (rightShift == 64)
        {
            scaled = numerator >= 0x8000_0000_0000_0000UL ? 1UL : 0UL;
            return true;
        }

        ulong quotient = numerator >> rightShift;
        ulong remainderMask = (1UL << rightShift) - 1;
        ulong halfway = 1UL << (rightShift - 1);
        scaled = quotient + ((numerator & remainderMask) >= halfway ? 1UL : 0UL);
        return true;
    }

    private static string FormatScaledUInt64(
        ulong scaled,
        int fractionDigits,
        bool negative)
    {
        int digitCount = 1;
        for (ulong remaining = scaled; remaining >= 10; remaining /= 10)
            digitCount++;

        int wholeDigits = Math.Max(1, digitCount - fractionDigits);
        int length = (negative ? 1 : 0) + wholeDigits
            + (fractionDigits == 0 ? 0 : fractionDigits + 1);

        return string.Create(
            length,
            (scaled, fractionDigits, negative),
            static (span, state) =>
            {
                (ulong remaining, int digits, bool isNegative) = state;
                int start = isNegative ? 1 : 0;
                int decimalIndex = digits == 0 ? -1 : span.Length - digits - 1;
                for (int i = span.Length - 1; i >= start; i--)
                {
                    if (i == decimalIndex)
                    {
                        span[i] = '.';
                        continue;
                    }

                    span[i] = (char)('0' + remaining % 10);
                    remaining /= 10;
                }

                if (isNegative)
                    span[0] = '-';
            });
    }

    private static string FormatScaledBigInteger(
        ulong significand,
        int binaryExponent,
        int fractionDigits,
        bool negative)
    {
        BigInteger numerator = new(significand);
        numerator *= BigInteger.Pow(5, fractionDigits);

        int shift = binaryExponent + fractionDigits;
        BigInteger scaled;
        if (shift >= 0)
        {
            scaled = numerator << shift;
        }
        else
        {
            BigInteger divisor = BigInteger.One << -shift;
            scaled = BigInteger.DivRem(numerator, divisor, out BigInteger remainder);
            if (remainder + remainder >= divisor)
                scaled += BigInteger.One;
        }

        string integer = scaled.ToString(CultureInfo.InvariantCulture);
        if (fractionDigits == 0)
            return negative ? "-" + integer : integer;

        integer = integer.PadLeft(fractionDigits + 1, '0');
        string fixedPoint = integer.Insert(integer.Length - fractionDigits, ".");
        return negative ? "-" + fixedPoint : fixedPoint;
    }
}

using SharpTS.Execution;
using System.Globalization;
using System.Numerics;
using System.Text;
using SharpTS.Runtime.Types;

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
            "toString" => BuiltInMethod.CreateV2("toString", 0, 1, (_, _, args) =>
            {
                int radix = 10;
                if (args.Length > 0 && args[0].Kind == ValueKind.Number)
                {
                    radix = (int)Interpreter.ToNumber(args[0]);
                    if (radix < 2 || radix > 36)
                        throw new Exception("Runtime Error: toString() radix must be between 2 and 36");
                }
                return RuntimeValue.FromString(ToStringWithRadix(value, radix));
            }),

            // BigInt.prototype.toLocaleString() — no Intl options support; decimal form.
            "toLocaleString" => BuiltInMethod.CreateV2("toLocaleString", 0, 1, (_, _, _) =>
                RuntimeValue.FromString(value.ToString(CultureInfo.InvariantCulture))),

            // BigInt.prototype.valueOf() — returns the bigint itself.
            "valueOf" => BuiltInMethod.CreateV2("valueOf", 0, (_, _, _) =>
                RuntimeValue.FromBigInt(new SharpTSBigInt(value))),

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

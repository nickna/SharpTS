using System.Numerics;
using System.Security.Cryptography;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Miller-Rabin primality testing and prime generation backing
/// <c>crypto.checkPrime(Sync)</c> / <c>crypto.generatePrime(Sync)</c> (#1062).
/// </summary>
/// <remarks>
/// NOTE: Must stay in sync with the emitted IL versions in
/// Compilation/RuntimeEmitter.CryptoHelpers.Primes.cs (compiled mode re-emits the
/// same algorithm as standalone IL over System.Numerics.BigInteger).
/// </remarks>
internal static class CryptoPrimes
{
    private static readonly int[] SmallPrimes =
        [2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37, 41, 43, 47, 53, 59, 61, 67, 71];

    /// <summary>Miller-Rabin probabilistic primality test.</summary>
    public static bool IsProbablyPrime(BigInteger n, int checks = 0)
    {
        if (checks <= 0) checks = 20;
        if (n < 2) return false;

        foreach (var sp in SmallPrimes)
        {
            if (n % sp == 0) return n == sp;
        }

        var d = n - 1;
        int r = 0;
        while (d.IsEven) { d /= 2; r++; }

        var byteLen = n.GetByteCount(isUnsigned: false);
        for (int i = 0; i < checks; i++)
        {
            // Random witness a in [2, n-2]
            var bytes = new byte[byteLen];
            RandomNumberGenerator.Fill(bytes);
            bytes[^1] &= 0x7f; // non-negative
            var a = new BigInteger(bytes) % (n - 3) + 2;

            var x = BigInteger.ModPow(a, d, n);
            if (x == 1 || x == n - 1) continue;

            bool composite = true;
            for (int j = 0; j < r - 1; j++)
            {
                x = BigInteger.ModPow(x, 2, n);
                if (x == n - 1) { composite = false; break; }
            }
            if (composite) return false;
        }
        return true;
    }

    /// <summary>
    /// Generates a random prime of exactly <paramref name="bits"/> bits.
    /// When <paramref name="safe"/> is set, (p-1)/2 is also prime.
    /// </summary>
    public static BigInteger GeneratePrime(int bits, bool safe = false)
    {
        if (bits < 2)
            throw new ArgumentException("generatePrime: size must be at least 2 bits");

        int byteCount = (bits + 7) / 8;
        var bytes = new byte[byteCount + 1]; // extra zero byte keeps the value non-negative

        while (true)
        {
            RandomNumberGenerator.Fill(bytes.AsSpan(0, byteCount));
            bytes[byteCount] = 0;

            // Little-endian in BigInteger's default ctor: bytes[0] is the low byte.
            bytes[0] |= 1; // odd
            // Force the exact bit length: set the top bit, clear anything above it.
            int topBitIndex = (bits - 1) % 8;
            int topByteIndex = byteCount - 1;
            bytes[topByteIndex] &= (byte)((1 << (topBitIndex + 1)) - 1);
            bytes[topByteIndex] |= (byte)(1 << topBitIndex);

            var candidate = new BigInteger(bytes);
            if (!IsProbablyPrime(candidate))
                continue;

            if (safe && !IsProbablyPrime((candidate - 1) / 2))
                continue;

            return candidate;
        }
    }

    /// <summary>Big-endian magnitude bytes for a non-negative BigInteger (Node Buffer form).</summary>
    public static byte[] ToUnsignedBigEndian(BigInteger value)
        => value.ToByteArray(isUnsigned: true, isBigEndian: true);

    /// <summary>Parses big-endian magnitude bytes (Node Buffer form) as a non-negative BigInteger.</summary>
    public static BigInteger FromUnsignedBigEndian(byte[] bytes)
        => new(bytes, isUnsigned: true, isBigEndian: true);
}

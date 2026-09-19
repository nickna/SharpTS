using System.Collections.Immutable;

namespace SharpTS.Compilation;

/// <summary>
/// Fixed trial divisors embedded by the prime emitters. This process-wide catalog
/// contains values only; generated declarations belong to each crypto runtime.
/// </summary>
internal static class CryptoPrimeEmitConstants
{
    internal static ImmutableArray<int> SmallPrimes { get; } =
        [2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37, 41, 43, 47, 53, 59, 61, 67, 71];
}

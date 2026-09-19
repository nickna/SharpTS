using System.Collections;
using System.Collections.Immutable;
using SharpTS.Compilation;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmitterConstantCatalogTests
{
    [Fact]
    public void TrialDivisorsCannotBeChangedForLaterCompilations()
    {
        AssertImmutable(() => CryptoPrimeEmitConstants.SmallPrimes, 97);
        Assert.Equal(new[] { 2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37, 41, 43, 47, 53, 59, 61, 67, 71 },
            CryptoPrimeEmitConstants.SmallPrimes);
    }

    [Fact]
    public void SignalNamesCannotBeChangedForLaterCompilations()
    {
        AssertImmutable(() => ProcessSignalEmitConstants.TrappableSignals, "injected");
        Assert.Equal(new[] { "SIGINT", "SIGTERM", "SIGHUP", "SIGQUIT", "SIGBREAK", "SIGWINCH" },
            ProcessSignalEmitConstants.TrappableSignals);
    }

    [Fact]
    public void SignalNumbersCannotBeChangedForLaterCompilations()
    {
        AssertImmutable(() => ProcessSignalEmitConstants.SignalNumbers, ("injected", 99));
        Assert.Equal(new[] { ("SIGHUP", 1), ("SIGINT", 2), ("SIGQUIT", 3), ("SIGABRT", 6),
            ("SIGKILL", 9), ("SIGUSR1", 10), ("SIGUSR2", 12), ("SIGTERM", 15), ("SIGBREAK", 21), ("SIGWINCH", 28) },
            ProcessSignalEmitConstants.SignalNumbers);
    }

    [Fact]
    public void HashNamesCannotBeChangedForLaterCompilations()
    {
        AssertImmutable(() => WebCryptoEmitConstants.HashNames, ("injected", "INJECTED"));
        Assert.Equal(new[] { ("sha1", "SHA-1"), ("sha256", "SHA-256"), ("sha384", "SHA-384"), ("sha512", "SHA-512") },
            WebCryptoEmitConstants.HashNames);
    }

    private static void AssertImmutable<T>(Func<ImmutableArray<T>> read, T replacement)
    {
        var original = read();
        var values = original.ToArray();
        var list = Assert.IsAssignableFrom<IList>(original);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list[0] = replacement);
        Assert.Throws<NotSupportedException>(list.Clear);
        Assert.Throws<NotSupportedException>(() => list.Add(replacement));
        var changedCopy = original.SetItem(0, replacement);
        Assert.Equal(replacement, changedCopy[0]);
        Assert.Equal(values, read());
    }
}

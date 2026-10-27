using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class NumberParseIntWideMagnitudeTests
{
    [Theory, ModeData]
    public void NumberParseIntDoesNotWrapAtSignedIntegerBoundaries(ExecutionMode mode)
        => Assert.Equal("true true\ntrue true\ntrue\ntrue\n", TestHarness.Run("""
            console.log(Number.parseInt('0x8000000000000000', 16) === 9223372036854776000,
                Number.parseInt('0x10000000000000000', 16) === 18446744073709552000);
            console.log(Number.parseInt('-0x8000000000000000', 16) === -9223372036854776000,
                Number.parseInt('0x10000000000000000') === 18446744073709552000);
            console.log(Number.parseInt('0xffffffffffffffffffffffff', 16) > 1e28);
            console.log(Number.isNaN(Number.parseInt('0x', 16)));
            """, mode));
}

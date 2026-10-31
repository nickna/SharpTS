using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class DynamicMixedBigIntEqualityTests
{
    [Theory, ModeData]
    public void DynamicLooseEqualityUsesTheBigIntDomain(ExecutionMode mode)
        => Assert.Equal("true true true true\ntrue true true\nfalse false false false\n", TestHarness.Run("""
            function equal(left: any, right: any): boolean { return left == right; }
            console.log(equal(1n, true), equal(true, 1n), equal(1n, '1'), equal('1', 1n));
            console.log(equal(1n, 1), equal(1, 1n), equal(1n, 1n));
            console.log(equal(1n, 1.5), equal(1n, 'bad'), equal(1n, null), equal(1n, undefined));
            """, mode));
}

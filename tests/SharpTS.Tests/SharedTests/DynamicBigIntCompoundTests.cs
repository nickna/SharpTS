using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class DynamicBigIntCompoundTests
{
    [Theory, ModeData]
    public void DynamicCompoundOperationsRetainTheNumericKind(ExecutionMode mode)
        => Assert.Equal("6 bigint\n3 bigint\n2 bigint\n4 bigint\ntrue\n", TestHarness.Run("""
            let value: any = 2n;
            const right: any = 3n;
            console.log(String(value *= right), typeof value);
            console.log(String(value /= 2n), typeof value);
            console.log(String(value -= 1n), typeof value);
            console.log(String(value <<= 1n), typeof value);
            try { value *= 2; console.log(false); } catch (error) { console.log(error instanceof TypeError); }
            """, mode));
}

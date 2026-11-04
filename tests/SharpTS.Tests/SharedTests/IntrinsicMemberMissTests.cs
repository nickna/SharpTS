using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class IntrinsicMemberMissTests
{
    [Theory, ModeData]
    public void MissingIntrinsicMembersReturnUndefined(ExecutionMode mode)
    {
        Assert.Equal("true false\ntrue false\ntrue false\n", TestHarness.Run("""
            const values: any[] = [new Uint8Array([1]), new Map(), new Set()];
            for (const value of values) console.log(value.missing === undefined, value.missing === null);
            """, mode));
    }
}

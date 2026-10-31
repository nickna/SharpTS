using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class MemberExpressionConstructionTests
{
    [Theory, ModeData]
    public void Issue2249Regression(ExecutionMode mode)
    {
        const string source = """
            const C: any = class { value = 1; };
            const Other: any = class { value = 2; };
            const holder: any = { C: Other };
            const child: any = new holder.C();
            console.log(child.value, child instanceof Other);
            const holder2: any = { slot: Other };
            const child2: any = new holder2.slot();
            console.log(child2.value, child2 instanceof Other);
            """;
        Assert.Equal("2 true\n2 true\n", TestHarness.Run(source, mode));
    }
}
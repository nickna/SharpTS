using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class EvaluatedClassInheritedStaticReceiverTests
{
    [Theory, ModeData]
    public void Issue2324Regression(ExecutionMode mode)
    {
        const string source = """
            const B = class { static get named() { return this; } };
            const D = class extends B {};
            const receiver: any = D;
            console.log(receiver.named === D);
            """;
        Assert.Equal("true\n", TestHarness.Run(source, mode));
    }
}
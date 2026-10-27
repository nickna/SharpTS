using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class AutoAccessorOwnPresenceTests
{
    [Theory, ModeData]
    public void Issue2375Regression(ExecutionMode mode)
    {
        const string source = """
            class B { accessor amount: number = 1; }
            const instance: any = new B();
            console.log(instance.amount, Object.hasOwn(instance, 'amount'));
            instance.amount = 7;
            console.log(instance.amount, Object.hasOwn(instance, 'amount'));
            """;
        Assert.Equal("1 false\n7 false\n", TestHarness.Run(source, mode));
    }
}

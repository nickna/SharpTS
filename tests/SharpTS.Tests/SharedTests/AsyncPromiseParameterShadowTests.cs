using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class AsyncPromiseParameterShadowTests
{
    [Theory, ModeData]
    public void Issue2378Regression(ExecutionMode mode)
    {
        const string source = """
            function make(Promise: any) { return async () => await Promise.resolve(7); }
            const read: any = make({ resolve(value: number) { return value + 2; } });
            read().then(value => console.log(value));
            """;
        Assert.Equal("9\n", TestHarness.Run(source, mode));
    }
}

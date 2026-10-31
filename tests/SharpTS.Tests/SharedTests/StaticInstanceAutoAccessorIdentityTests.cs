using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class StaticInstanceAutoAccessorIdentityTests
{
    [Theory, ModeData]
    public void Issue2512Regression(ExecutionMode mode)
    {
        const string source = """
            class C {
                static accessor value: string = "static";
                accessor value: string = "instance";
            }
            console.log("declared");
            """;
        Assert.Equal("declared\n", TestHarness.Run(source, mode));
    }
}

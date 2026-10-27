using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ClassStaticFieldArrowReceiverTests
{
    [Theory, ModeData]
    public void Issue2420Regression(ExecutionMode mode)
    {
        const string source = """
            const C = class { static read = () => this; };
            console.log(C.read() === C);
            """;
        Assert.Equal("true\n", TestHarness.Run(source, mode));
    }
}

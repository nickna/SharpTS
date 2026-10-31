using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class GeneratorLaterDeclaratorCaptureTests
{
    [Theory, ModeData]
    public void Issue2313Regression(ExecutionMode mode)
    {
        const string source = """
            function check() {
                const first = {}, later = {};
                function* values() { yield 1; throw later; }
                console.log(values().next().value);
            }
            check();
            """;
        Assert.Equal("1\n", TestHarness.Run(source, mode));
    }
}

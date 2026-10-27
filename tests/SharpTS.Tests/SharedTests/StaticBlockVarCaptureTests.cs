using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class StaticBlockVarCaptureTests
{
    [Theory, ModeData]
    public void Issue2454Regression(ExecutionMode mode)
    {
        const string source = """
            let result = "";
            const C = class {
                static {
                    const read = () => value;
                    var value = "block-var";
                    result = read();
                }
            };
            console.log(result);
            """;
        Assert.Equal("block-var\n", TestHarness.Run(source, mode));
    }
}

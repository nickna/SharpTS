using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class StaticInstanceMethodDefaultTests
{
    [Theory, ModeData]
    public void Issue2506Regression(ExecutionMode mode)
    {
        const string source = """
            class C {
                run(value: number, flag: boolean, text: string) {
                    return value + ":" + flag + ":" + text;
                }
                static run(value: number = 2, flag: boolean = true, text: string = "S") {
                    return value + ":" + flag + ":" + text;
                }
            }
            console.log(C.run());
            """;
        Assert.Equal("2:true:S\n", TestHarness.Run(source, mode));
    }
}

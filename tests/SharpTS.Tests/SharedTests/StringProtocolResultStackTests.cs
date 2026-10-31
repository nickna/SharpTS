using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class StringProtocolResultStackTests
{
    [Theory, ModeData]
    public void ReplacementResultCanFeedTypedStringConcatenation(ExecutionMode mode)
    {
        const string source = """
            const pattern = /\d/;
            console.log('p' + '12'.replace(pattern, 'x'));
            """;
        Assert.Equal("px2\n", TestHarness.Run(source, mode));
    }
}

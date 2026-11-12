using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class StaticLiteralIndexAssignmentTests
{
    [Theory, ModeData]
    public void LiteralStaticWritesExecuteInStaticBlocksAndOnTheConstructor(ExecutionMode mode)
    {
        const string source = """
            const C = class {
                static value = 0;
                static { this['value'] = 1; }
            };
            C['value'] = C['value'] + 2;
            console.log(C.value);
            """;
        Assert.Equal("3\n", TestHarness.Run(source, mode));
    }
}

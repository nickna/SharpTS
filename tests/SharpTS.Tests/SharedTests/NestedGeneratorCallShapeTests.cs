using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class NestedGeneratorCallShapeTests
{
    [Theory, ModeData]
    public void CapturingGeneratorPreservesItsOwnArguments(ExecutionMode mode)
    {
        const string source = """
            function make(value: number) {
                function* values() { yield value + arguments.length; }
                return values;
            }
            const values = make(9), iterator = values(1, 2);
            console.log(iterator.next().value);
            iterator.next();
            """;
        Assert.Equal("11\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void CapturingGeneratorPreservesItsRestParameter(ExecutionMode mode)
    {
        const string source = """
            function make(value: number) {
                function* values(...rest: number[]) { yield value + rest.length; }
                return values;
            }
            const values = make(9), iterator = values(1, 2);
            console.log(iterator.next().value);
            iterator.next();
            """;
        Assert.Equal("11\n", TestHarness.Run(source, mode));
    }
}

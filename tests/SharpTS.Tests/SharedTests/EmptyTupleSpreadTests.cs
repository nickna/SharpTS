using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class EmptyTupleSpreadTests
{
    [Theory, ModeData]
    public void EmptyTupleSpreadsProduceAnEmptyArray(ExecutionMode mode)
    {
        const string source = """
            const empty: [] = [];
            const first = [...empty];
            const second = [...empty, ...empty];
            const mixed = [...empty, 7, ...empty];
            console.log(first.length, second.length, mixed.length, mixed[0]);
            """;
        Assert.Equal("0 0 1 7\n", TestHarness.Run(source, mode));
    }
}

using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class MapGroupByIterableTests
{
    [Theory, ModeData]
    public void SetsAreGroupedIncrementallyWithStableCallbackIndexes(ExecutionMode mode)
    {
        const string source = """
            const map: any = Map;
            const indexes: number[] = [];
            const result: any = map.groupBy(new Set([2, 2, 3]), (value: number, index: number) => {
                indexes.push(index); return value % 2;
            });
            console.log(result.size, result.get(0).join(","), result.get(1).join(","), indexes.join(","));
            """;
        Assert.Equal("2 2 3 0,1\n", TestHarness.Run(source, mode));
    }
}

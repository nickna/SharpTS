using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class DynamicCollectionIterationStackTests
{
    [Theory, ModeData]
    public void DynamicCollectionBranchesResetBodyStackProofs(ExecutionMode mode)
    {
        const string source = """
            function read(values: any) {
                for (const value of values) { delete value.item; }
            }
            const map = new Map<any, any>();
            const set = new Set<any>();
            const value = { item: 1 };
            read([value]);
            map.set(0, value);
            read(map);
            set.add(value);
            read(set);
            console.log(Object.hasOwn(value, 'item'));
            """;
        Assert.Equal("false\n", TestHarness.Run(source, mode));
    }
}

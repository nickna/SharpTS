using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class NumericMapPromotionSemanticsTests
{
    [Theory, ModeData]
    public void NumericMapOperations_PreserveSameValueZeroAndMissingGet(ExecutionMode mode)
    {
        const string source = """
            function run(): void {
                const map = new Map<number, number>();
                const nan: number = 0 / 0;

                map.set(nan, 1);
                map.set(nan, 2);
                console.log(map.get(nan));
                console.log(map.has(nan));
                console.log(map.size);

                map.set(-0, 3);
                map.set(+0, 4);
                console.log(map.get(-0));
                console.log(map.size);

                map.set(1, 10);
                map.set(2, 20);
                map.set(1, 11);
                console.log(map.size);
                console.log(map.get(1));

                console.log(map.delete(1));
                console.log(map.size);
                map.set(1, 12);
                console.log(map.get(1));
                console.log(map.get(999) === undefined);

                map.clear();
                console.log(map.size);
            }
            run();
            """;

        Assert.Equal(
            "2\ntrue\n1\n4\n2\n4\n11\ntrue\n3\n12\ntrue\n0\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void RepeatedSetAndDeleteReinsert_PreserveOrderValueAndSize(ExecutionMode mode)
    {
        const string source = """
            function order(): string {
                const map = new Map<number, number>();
                map.set(1, 10);
                map.set(2, 20);
                map.set(1, 11);
                let before: string = "";
                for (const entry of map) before = before + entry[0];

                map.delete(1);
                map.set(1, 12);
                return before + ":" + map.size + ":" + map.get(1);
            }
            console.log(order());
            """;

        Assert.Equal("12:2:12\n", TestHarness.Run(source, mode));
    }
}

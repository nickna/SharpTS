using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for Object.groupBy() and Map.groupBy(). Runs against both interpreter and compiler.
/// </summary>
public class GroupByTests
{
    [Theory, ModeData]
    public void ObjectGroupBy_BasicGrouping(ExecutionMode mode)
    {
        var source = """
            const inventory = [
                { name: "asparagus", type: "vegetables" },
                { name: "bananas", type: "fruit" },
                { name: "goat", type: "meat" },
                { name: "cherries", type: "fruit" },
                { name: "fish", type: "meat" }
            ];
            const result: any = Object.groupBy(inventory, (item: any) => item.type);
            console.log(Object.keys(result).length);
            console.log(result.vegetables.length);
            console.log(result.fruit.length);
            console.log(result.meat.length);
            console.log(result.fruit[0].name);
            console.log(result.fruit[1].name);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n1\n2\n2\nbananas\ncherries\n", output);
    }

    [Theory, ModeData]
    public void ObjectGroupBy_EmptyArray(ExecutionMode mode)
    {
        var source = """
            const result: any = Object.groupBy([], (_: any) => "key");
            console.log(Object.keys(result).length);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }

    [Theory, ModeData]
    public void ObjectGroupBy_NumericKeys(ExecutionMode mode)
    {
        var source = """
            const nums = [1, 2, 3, 4, 5, 6];
            const result: any = Object.groupBy(nums, (n: any) => n % 2 === 0 ? "even" : "odd");
            console.log(result.odd.length);
            console.log(result.even.length);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n3\n", output);
    }

    [Theory, ModeData]
    public void ObjectGroupBy_CallbackReceivesIndex(ExecutionMode mode)
    {
        var source = """
            const arr = ["a", "b", "c", "d"];
            const result: any = Object.groupBy(arr, (_: any, i: number) => i < 2 ? "first" : "second");
            console.log(result.first.length);
            console.log(result.second.length);
            console.log(result.first[0]);
            console.log(result.second[0]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n2\na\nc\n", output);
    }

    [Theory, ModeData]
    public void MapGroupBy_BasicGrouping(ExecutionMode mode)
    {
        var source = """
            const inventory = [
                { name: "asparagus", type: "vegetables" },
                { name: "bananas", type: "fruit" },
                { name: "cherries", type: "fruit" }
            ];
            const result = Map.groupBy(inventory, (item: any) => item.type);
            console.log(result.get("vegetables").length);
            console.log(result.get("fruit").length);
            console.log(result.get("fruit")[0].name);
            console.log(result.get("fruit")[1].name);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\nbananas\ncherries\n", output);
    }

    [Theory, ModeData]
    public void MapGroupBy_EmptyArray(ExecutionMode mode)
    {
        var source = """
            const result = Map.groupBy([], (_: any) => "key");
            console.log(result.size);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }

    [Theory, ModeData]
    public void MapGroupBy_NullUndefinedKeys(ExecutionMode mode)
    {
        var source = """
            const items = [1, 2, 3];
            const result = Map.groupBy(items, (n: any) => n > 2 ? "big" : undefined);
            console.log(result.size);
            console.log(result.get(undefined).length);
            console.log(result.get(undefined)[0]);
            console.log(result.get(undefined)[1]);
            console.log(result.get("big").length);
            console.log(result.get("big")[0]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n2\n1\n2\n1\n3\n", output);
    }

    [Theory, ModeData]
    public void MapGroupBy_MultipleItemsPerGroup(ExecutionMode mode)
    {
        var source = """
            const nums = [1, 2, 3, 4, 5, 6];
            const result = Map.groupBy(nums, (n: any) => { return n % 2 === 0 ? "even" : "odd"; });
            const odd = result.get("odd");
            const even = result.get("even");
            console.log(odd.length);
            console.log(even.length);
            console.log(odd[0]);
            console.log(even[0]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n3\n1\n2\n", output);
    }
}

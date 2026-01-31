using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for array flat() and flatMap() methods. Runs against both interpreter and compiler.
/// </summary>
public class ArrayFlatTests
{
    #region Flat Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Flat_DefaultDepth_FlattensOneLevel(ExecutionMode mode)
    {
        var source = """
            let arr: any[] = [1, [2, 3], [4, [5, 6]]];
            let result = arr.flat();
            console.log(result.length);
            console.log(result[0]);
            console.log(result[1]);
            console.log(result[2]);
            console.log(result[3]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\n1\n2\n3\n4\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Flat_DepthTwo_FlattensNested(ExecutionMode mode)
    {
        var source = """
            let arr: any[] = [1, [2, [3, [4]]]];
            let result = arr.flat(2);
            console.log(result.length);
            console.log(result[0]);
            console.log(result[1]);
            console.log(result[2]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("4\n1\n2\n3\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Flat_DepthZero_ShallowCopy(ExecutionMode mode)
    {
        var source = """
            let arr: any[] = [1, [2, 3]];
            let result = arr.flat(0);
            console.log(result.length);
            console.log(result[0]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n1\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Flat_LargeDepth_FlattensCompletely(ExecutionMode mode)
    {
        var source = """
            let arr: any[] = [1, [2, [3, [4, [5]]]]];
            let result = arr.flat(100);
            console.log(result.length);
            console.log(result[0]);
            console.log(result[1]);
            console.log(result[2]);
            console.log(result[3]);
            console.log(result[4]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\n1\n2\n3\n4\n5\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Flat_EmptyArray_ReturnsEmpty(ExecutionMode mode)
    {
        var source = """
            let arr: any[] = [];
            let result = arr.flat();
            console.log(result.length);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Flat_NoNestedArrays_ReturnsCopy(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3];
            let result = arr.flat();
            console.log(result.length);
            console.log(result[0]);
            console.log(result[1]);
            console.log(result[2]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n1\n2\n3\n", output);
    }

    #endregion

    #region FlatMap Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void FlatMap_ArrayResult_Flattens(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3];
            let result = arr.flatMap((x: number): number[] => [x, x * 2]);
            console.log(result.length);
            console.log(result[0]);
            console.log(result[1]);
            console.log(result[2]);
            console.log(result[3]);
            console.log(result[4]);
            console.log(result[5]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("6\n1\n2\n2\n4\n3\n6\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void FlatMap_NonArrayResult_AddedDirectly(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3];
            let result = arr.flatMap((x: number): number => x * 2);
            console.log(result.length);
            console.log(result[0]);
            console.log(result[1]);
            console.log(result[2]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n2\n4\n6\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void FlatMap_EmptyArrayResult_Filters(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3, 4];
            let result = arr.flatMap((x: number): number[] => x % 2 == 0 ? [x] : []);
            console.log(result.length);
            console.log(result[0]);
            console.log(result[1]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n2\n4\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void FlatMap_ReceivesIndexAndArray(ExecutionMode mode)
    {
        var source = """
            let arr: string[] = ["a", "b"];
            let indices: number[] = [];
            arr.flatMap((el: string, idx: number, arr: string[]): string[] => {
                indices.push(idx);
                return [el];
            });
            console.log(indices[0]);
            console.log(indices[1]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n1\n", output);
    }

    #endregion
}

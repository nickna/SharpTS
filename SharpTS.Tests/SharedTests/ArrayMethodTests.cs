using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for array methods (find, findIndex, some, every, reduce, includes, indexOf, join, concat, reverse, etc.).
/// Runs against both interpreter and compiler.
/// </summary>
public class ArrayMethodTests
{
    #region Find Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_Find_ReturnsMatchingElement(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [1, 2, 3, 4, 5];
            let result: number | null = nums.find((n: number): boolean => n > 3);
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("4\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_Find_ReturnsNullWhenNotFound(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [1, 2, 3];
            let result: number | null = nums.find((n: number): boolean => n > 10);
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("null\n", output);
    }

    #endregion

    #region FindIndex Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_FindIndex_ReturnsIndex(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [1, 2, 3, 4, 5];
            console.log(nums.findIndex((n: number): boolean => n > 3));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_FindIndex_ReturnsMinusOneWhenNotFound(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [1, 2, 3];
            console.log(nums.findIndex((n: number): boolean => n > 10));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("-1\n", output);
    }

    #endregion

    #region Some/Every Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_Some_ReturnsTrueWhenMatch(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [1, 2, 3, 4, 5];
            console.log(nums.some((n: number): boolean => n > 3));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_Some_ReturnsFalseWhenNoMatch(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [1, 2, 3];
            console.log(nums.some((n: number): boolean => n > 10));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_Every_ReturnsTrueWhenAllMatch(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [2, 4, 6, 8];
            console.log(nums.every((n: number): boolean => n % 2 == 0));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_Every_ReturnsFalseWhenSomeDontMatch(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [2, 4, 5, 8];
            console.log(nums.every((n: number): boolean => n % 2 == 0));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\n", output);
    }

    #endregion

    #region Reduce Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_Reduce_WithInitialValue_ReturnsResult(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [1, 2, 3, 4];
            let sum: number = nums.reduce((acc: number, n: number): number => acc + n, 0);
            console.log(sum);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_Reduce_WithoutInitialValue_UsesFirstElement(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [1, 2, 3, 4];
            let sum: number = nums.reduce((acc: number, n: number): number => acc + n);
            console.log(sum);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n", output);
    }

    #endregion

    #region Includes/IndexOf Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_Includes_ReturnsTrueWhenFound(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [1, 2, 3, 4, 5];
            console.log(nums.includes(3));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_Includes_ReturnsFalseWhenNotFound(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [1, 2, 3, 4, 5];
            console.log(nums.includes(10));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_IndexOf_ReturnsIndex(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [1, 2, 3, 4, 5];
            console.log(nums.indexOf(3));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_IndexOf_ReturnsMinusOneWhenNotFound(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [1, 2, 3, 4, 5];
            console.log(nums.indexOf(10));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("-1\n", output);
    }

    #endregion

    #region Join/Concat/Reverse Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_Join_ReturnsJoinedString(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [1, 2, 3];
            console.log(nums.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1,2,3\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_Join_WithEmptySeparator_ConcatenatesElements(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [1, 2, 3];
            console.log(nums.join(""));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("123\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_Concat_ReturnsNewArray(ExecutionMode mode)
    {
        var source = """
            let a: number[] = [1, 2];
            let b: number[] = [3, 4];
            let c: number[] = a.concat(b);
            console.log(c.length);
            console.log(c[0]);
            console.log(c[3]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("4\n1\n4\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_Reverse_ReversesInPlace(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [1, 2, 3, 4, 5];
            nums.reverse();
            console.log(nums[0]);
            console.log(nums[4]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\n1\n", output);
    }

    #endregion

    #region Chained Methods

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_ChainedMethods_Work(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [1, 2, 3, 4, 5];
            let result: number[] = nums.filter((n: number): boolean => n % 2 == 1).map((n: number): number => n * 2);
            console.log(result[0]);
            console.log(result[1]);
            console.log(result[2]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n6\n10\n", output);
    }

    #endregion

    #region ES2023 FindLast/FindLastIndex

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_FindLast_ReturnsLastMatchingElement(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [1, 2, 3, 4, 5];
            let result: number | null = nums.findLast((n: number): boolean => n > 2);
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_FindLast_ReturnsNullWhenNotFound(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [1, 2, 3];
            let result: number | null = nums.findLast((n: number): boolean => n > 10);
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("null\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_FindLast_EmptyArrayReturnsNull(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [];
            let result: number | null = nums.findLast((n: number): boolean => n > 0);
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("null\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_FindLastIndex_ReturnsLastMatchingIndex(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [1, 2, 3, 4, 5];
            console.log(nums.findLastIndex((n: number): boolean => n > 2));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("4\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_FindLastIndex_ReturnsMinusOneWhenNotFound(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [1, 2, 3];
            console.log(nums.findLastIndex((n: number): boolean => n > 10));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("-1\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_FindLastIndex_EmptyArrayReturnsMinusOne(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [];
            console.log(nums.findLastIndex((n: number): boolean => n > 0));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("-1\n", output);
    }

    #endregion

    #region ES2023 ToReversed

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_ToReversed_ReturnsNewReversedArray(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [1, 2, 3, 4, 5];
            let reversed: number[] = nums.toReversed();
            console.log(reversed[0]);
            console.log(reversed[4]);
            console.log(nums[0]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\n1\n1\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_ToReversed_OriginalUnchanged(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [1, 2, 3];
            let reversed: number[] = nums.toReversed();
            console.log(nums.join(","));
            console.log(reversed.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1,2,3\n3,2,1\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_ToReversed_EmptyArrayReturnsEmpty(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [];
            let reversed: number[] = nums.toReversed();
            console.log(reversed.length);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_ToReversed_SingleElementArray(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [42];
            let reversed: number[] = nums.toReversed();
            console.log(reversed[0]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    #endregion

    #region ES2023 With

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_With_ReplacesElementAtIndex(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [1, 2, 3];
            let result: number[] = nums.with(1, 99);
            console.log(result.join(","));
            console.log(nums.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1,99,3\n1,2,3\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_With_NegativeIndex(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [1, 2, 3];
            let result: number[] = nums.with(-1, 99);
            console.log(result.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1,2,99\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_With_FirstElement(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [1, 2, 3];
            let result: number[] = nums.with(0, 99);
            console.log(result.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("99,2,3\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_With_LastElement(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [1, 2, 3];
            let result: number[] = nums.with(2, 99);
            console.log(result.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1,2,99\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_With_OriginalUnchanged(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [1, 2, 3, 4, 5];
            let result: number[] = nums.with(2, 99);
            console.log(nums[2]);
            console.log(result[2]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n99\n", output);
    }

    #endregion

    #region ES2022 At

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_At_PositiveIndex(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [1, 2, 3, 4, 5];
            console.log(nums.at(0));
            console.log(nums.at(2));
            console.log(nums.at(4));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n3\n5\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_At_NegativeIndex(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [1, 2, 3, 4, 5];
            console.log(nums.at(-1));
            console.log(nums.at(-2));
            console.log(nums.at(-5));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\n4\n1\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_At_OutOfBoundsReturnsNull(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [1, 2, 3];
            console.log(nums.at(10) === null);
            console.log(nums.at(-10) === null);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_At_EmptyArray(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [];
            console.log(nums.at(0) === null);
            console.log(nums.at(-1) === null);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_At_SingleElement(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [42];
            console.log(nums.at(0));
            console.log(nums.at(-1));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n42\n", output);
    }

    #endregion
}

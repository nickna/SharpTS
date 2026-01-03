using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

public class ArrayMethodTests
{
    // find
    [Fact]
    public void Array_Find_ReturnsMatchingElement()
    {
        var source = """
            let nums: number[] = [1, 2, 3, 4, 5];
            let result: number | null = nums.find((n: number): boolean => n > 3);
            console.log(result);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("4\n", output);
    }

    [Fact]
    public void Array_Find_ReturnsNullWhenNotFound()
    {
        var source = """
            let nums: number[] = [1, 2, 3];
            let result: number | null = nums.find((n: number): boolean => n > 10);
            console.log(result);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("null\n", output);
    }

    // findIndex
    [Fact]
    public void Array_FindIndex_ReturnsIndex()
    {
        var source = """
            let nums: number[] = [1, 2, 3, 4, 5];
            console.log(nums.findIndex((n: number): boolean => n > 3));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("3\n", output);
    }

    [Fact]
    public void Array_FindIndex_ReturnsMinusOneWhenNotFound()
    {
        var source = """
            let nums: number[] = [1, 2, 3];
            console.log(nums.findIndex((n: number): boolean => n > 10));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("-1\n", output);
    }

    // some
    [Fact]
    public void Array_Some_ReturnsTrueWhenMatch()
    {
        var source = """
            let nums: number[] = [1, 2, 3, 4, 5];
            console.log(nums.some((n: number): boolean => n > 3));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Array_Some_ReturnsFalseWhenNoMatch()
    {
        var source = """
            let nums: number[] = [1, 2, 3];
            console.log(nums.some((n: number): boolean => n > 10));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("false\n", output);
    }

    // every
    [Fact]
    public void Array_Every_ReturnsTrueWhenAllMatch()
    {
        var source = """
            let nums: number[] = [2, 4, 6, 8];
            console.log(nums.every((n: number): boolean => n % 2 == 0));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Array_Every_ReturnsFalseWhenSomeDontMatch()
    {
        var source = """
            let nums: number[] = [2, 4, 5, 8];
            console.log(nums.every((n: number): boolean => n % 2 == 0));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("false\n", output);
    }

    // reduce
    [Fact]
    public void Array_Reduce_WithInitialValue_ReturnsResult()
    {
        var source = """
            let nums: number[] = [1, 2, 3, 4];
            let sum: number = nums.reduce((acc: number, n: number): number => acc + n, 0);
            console.log(sum);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("10\n", output);
    }

    [Fact]
    public void Array_Reduce_WithoutInitialValue_UsesFirstElement()
    {
        var source = """
            let nums: number[] = [1, 2, 3, 4];
            let sum: number = nums.reduce((acc: number, n: number): number => acc + n);
            console.log(sum);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("10\n", output);
    }

    // includes
    [Fact]
    public void Array_Includes_ReturnsTrueWhenFound()
    {
        var source = """
            let nums: number[] = [1, 2, 3, 4, 5];
            console.log(nums.includes(3));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Array_Includes_ReturnsFalseWhenNotFound()
    {
        var source = """
            let nums: number[] = [1, 2, 3, 4, 5];
            console.log(nums.includes(10));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("false\n", output);
    }

    // indexOf
    [Fact]
    public void Array_IndexOf_ReturnsIndex()
    {
        var source = """
            let nums: number[] = [1, 2, 3, 4, 5];
            console.log(nums.indexOf(3));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("2\n", output);
    }

    [Fact]
    public void Array_IndexOf_ReturnsMinusOneWhenNotFound()
    {
        var source = """
            let nums: number[] = [1, 2, 3, 4, 5];
            console.log(nums.indexOf(10));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("-1\n", output);
    }

    // join
    [Fact]
    public void Array_Join_ReturnsJoinedString()
    {
        var source = """
            let nums: number[] = [1, 2, 3];
            console.log(nums.join(","));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1,2,3\n", output);
    }

    [Fact]
    public void Array_Join_WithEmptySeparator_ConcatenatesElements()
    {
        var source = """
            let nums: number[] = [1, 2, 3];
            console.log(nums.join(""));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("123\n", output);
    }

    // concat
    [Fact]
    public void Array_Concat_ReturnsNewArray()
    {
        var source = """
            let a: number[] = [1, 2];
            let b: number[] = [3, 4];
            let c: number[] = a.concat(b);
            console.log(c.length);
            console.log(c[0]);
            console.log(c[3]);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("4\n1\n4\n", output);
    }

    // reverse
    [Fact]
    public void Array_Reverse_ReversesInPlace()
    {
        var source = """
            let nums: number[] = [1, 2, 3, 4, 5];
            nums.reverse();
            console.log(nums[0]);
            console.log(nums[4]);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("5\n1\n", output);
    }

    // Combined operations
    [Fact]
    public void Array_ChainedMethods_Work()
    {
        var source = """
            let nums: number[] = [1, 2, 3, 4, 5];
            let result: number[] = nums.filter((n: number): boolean => n % 2 == 1).map((n: number): number => n * 2);
            console.log(result[0]);
            console.log(result[1]);
            console.log(result[2]);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("2\n6\n10\n", output);
    }
}

using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

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

        var output = TestHarness.RunCompiled(source);
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

        var output = TestHarness.RunCompiled(source);
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("3\n", output);
    }

    [Fact]
    public void Array_FindIndex_ReturnsMinusOneWhenNotFound()
    {
        var source = """
            let nums: number[] = [1, 2, 3];
            console.log(nums.findIndex((n: number): boolean => n > 10));
            """;

        var output = TestHarness.RunCompiled(source);
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Array_Some_ReturnsFalseWhenNoMatch()
    {
        var source = """
            let nums: number[] = [1, 2, 3];
            console.log(nums.some((n: number): boolean => n > 10));
            """;

        var output = TestHarness.RunCompiled(source);
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Array_Every_ReturnsFalseWhenSomeDontMatch()
    {
        var source = """
            let nums: number[] = [2, 4, 5, 8];
            console.log(nums.every((n: number): boolean => n % 2 == 0));
            """;

        var output = TestHarness.RunCompiled(source);
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

        var output = TestHarness.RunCompiled(source);
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

        var output = TestHarness.RunCompiled(source);
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Array_Includes_ReturnsFalseWhenNotFound()
    {
        var source = """
            let nums: number[] = [1, 2, 3, 4, 5];
            console.log(nums.includes(10));
            """;

        var output = TestHarness.RunCompiled(source);
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("2\n", output);
    }

    [Fact]
    public void Array_IndexOf_ReturnsMinusOneWhenNotFound()
    {
        var source = """
            let nums: number[] = [1, 2, 3, 4, 5];
            console.log(nums.indexOf(10));
            """;

        var output = TestHarness.RunCompiled(source);
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1,2,3\n", output);
    }

    [Fact]
    public void Array_Join_WithEmptySeparator_ConcatenatesElements()
    {
        var source = """
            let nums: number[] = [1, 2, 3];
            console.log(nums.join(""));
            """;

        var output = TestHarness.RunCompiled(source);
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

        var output = TestHarness.RunCompiled(source);
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

        var output = TestHarness.RunCompiled(source);
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("2\n6\n10\n", output);
    }

    // Regression tests for array method boxing (IL compiler)
    // These test that boolean-returning methods work correctly in expressions

    [Fact]
    public void Array_Some_ResultInTernary()
    {
        var source = """
            let nums: number[] = [1, 2, 3];
            let msg: string = nums.some((n: number): boolean => n > 2) ? "found" : "not found";
            console.log(msg);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("found\n", output);
    }

    [Fact]
    public void Array_Every_ResultInTernary()
    {
        var source = """
            let nums: number[] = [2, 4, 6];
            let msg: string = nums.every((n: number): boolean => n % 2 == 0) ? "all even" : "some odd";
            console.log(msg);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("all even\n", output);
    }

    [Fact]
    public void Array_Includes_ResultInTernary()
    {
        var source = """
            let nums: number[] = [1, 2, 3];
            let msg: string = nums.includes(2) ? "has 2" : "no 2";
            console.log(msg);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("has 2\n", output);
    }

    [Fact]
    public void Array_Some_AssignToVariable()
    {
        var source = """
            let nums: number[] = [1, 2, 3, 4, 5];
            let result: boolean = nums.some((n: number): boolean => n > 3);
            console.log(result);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Array_Every_AssignToVariable()
    {
        var source = """
            let nums: number[] = [2, 4, 6, 8];
            let result: boolean = nums.every((n: number): boolean => n % 2 == 0);
            console.log(result);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Array_Includes_AssignToVariable()
    {
        var source = """
            let nums: number[] = [1, 2, 3, 4, 5];
            let result: boolean = nums.includes(3);
            console.log(result);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Array_Some_InIfCondition()
    {
        var source = """
            let nums: number[] = [1, 2, 3];
            if (nums.some((n: number): boolean => n > 2)) {
                console.log("found larger than 2");
            } else {
                console.log("none larger than 2");
            }
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("found larger than 2\n", output);
    }

    [Fact]
    public void Array_Every_InIfCondition()
    {
        var source = """
            let nums: number[] = [2, 4, 6];
            if (nums.every((n: number): boolean => n % 2 == 0)) {
                console.log("all even");
            } else {
                console.log("not all even");
            }
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("all even\n", output);
    }

    [Fact]
    public void Array_Includes_InIfCondition()
    {
        var source = """
            let nums: number[] = [1, 2, 3];
            if (nums.includes(2)) {
                console.log("has 2");
            } else {
                console.log("no 2");
            }
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("has 2\n", output);
    }

    // ES2023: findLast
    [Fact]
    public void Array_FindLast_ReturnsLastMatchingElement()
    {
        var source = """
            let nums: number[] = [1, 2, 3, 4, 5];
            let result: number | null = nums.findLast((n: number): boolean => n > 2);
            console.log(result);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("5\n", output);
    }

    [Fact]
    public void Array_FindLast_ReturnsNullWhenNotFound()
    {
        var source = """
            let nums: number[] = [1, 2, 3];
            let result: number | null = nums.findLast((n: number): boolean => n > 10);
            console.log(result);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("null\n", output);
    }

    [Fact]
    public void Array_FindLast_EmptyArrayReturnsNull()
    {
        var source = """
            let nums: number[] = [];
            let result: number | null = nums.findLast((n: number): boolean => n > 0);
            console.log(result);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("null\n", output);
    }

    // ES2023: findLastIndex
    [Fact]
    public void Array_FindLastIndex_ReturnsLastMatchingIndex()
    {
        var source = """
            let nums: number[] = [1, 2, 3, 4, 5];
            console.log(nums.findLastIndex((n: number): boolean => n > 2));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("4\n", output);
    }

    [Fact]
    public void Array_FindLastIndex_ReturnsMinusOneWhenNotFound()
    {
        var source = """
            let nums: number[] = [1, 2, 3];
            console.log(nums.findLastIndex((n: number): boolean => n > 10));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("-1\n", output);
    }

    [Fact]
    public void Array_FindLastIndex_EmptyArrayReturnsMinusOne()
    {
        var source = """
            let nums: number[] = [];
            console.log(nums.findLastIndex((n: number): boolean => n > 0));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("-1\n", output);
    }

    // ES2023: toReversed
    [Fact]
    public void Array_ToReversed_ReturnsNewReversedArray()
    {
        var source = """
            let nums: number[] = [1, 2, 3, 4, 5];
            let reversed: number[] = nums.toReversed();
            console.log(reversed[0]);
            console.log(reversed[4]);
            console.log(nums[0]);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("5\n1\n1\n", output);
    }

    [Fact]
    public void Array_ToReversed_OriginalUnchanged()
    {
        var source = """
            let nums: number[] = [1, 2, 3];
            let reversed: number[] = nums.toReversed();
            console.log(nums.join(","));
            console.log(reversed.join(","));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1,2,3\n3,2,1\n", output);
    }

    [Fact]
    public void Array_ToReversed_EmptyArrayReturnsEmpty()
    {
        var source = """
            let nums: number[] = [];
            let reversed: number[] = nums.toReversed();
            console.log(reversed.length);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("0\n", output);
    }

    [Fact]
    public void Array_ToReversed_SingleElementArray()
    {
        var source = """
            let nums: number[] = [42];
            let reversed: number[] = nums.toReversed();
            console.log(reversed[0]);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42\n", output);
    }

    // ES2023: with
    [Fact]
    public void Array_With_ReplacesElementAtIndex()
    {
        var source = """
            let nums: number[] = [1, 2, 3];
            let result: number[] = nums.with(1, 99);
            console.log(result.join(","));
            console.log(nums.join(","));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1,99,3\n1,2,3\n", output);
    }

    [Fact]
    public void Array_With_NegativeIndex()
    {
        var source = """
            let nums: number[] = [1, 2, 3];
            let result: number[] = nums.with(-1, 99);
            console.log(result.join(","));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1,2,99\n", output);
    }

    [Fact]
    public void Array_With_FirstElement()
    {
        var source = """
            let nums: number[] = [1, 2, 3];
            let result: number[] = nums.with(0, 99);
            console.log(result.join(","));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("99,2,3\n", output);
    }

    [Fact]
    public void Array_With_LastElement()
    {
        var source = """
            let nums: number[] = [1, 2, 3];
            let result: number[] = nums.with(2, 99);
            console.log(result.join(","));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1,2,99\n", output);
    }

    [Fact]
    public void Array_With_OriginalUnchanged()
    {
        var source = """
            let nums: number[] = [1, 2, 3, 4, 5];
            let result: number[] = nums.with(2, 99);
            console.log(nums[2]);
            console.log(result[2]);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("3\n99\n", output);
    }

    // ES2022: at
    [Fact]
    public void Array_At_PositiveIndex()
    {
        var source = """
            let nums: number[] = [1, 2, 3, 4, 5];
            console.log(nums.at(0));
            console.log(nums.at(2));
            console.log(nums.at(4));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\n3\n5\n", output);
    }

    [Fact]
    public void Array_At_NegativeIndex()
    {
        var source = """
            let nums: number[] = [1, 2, 3, 4, 5];
            console.log(nums.at(-1));
            console.log(nums.at(-2));
            console.log(nums.at(-5));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("5\n4\n1\n", output);
    }

    [Fact]
    public void Array_At_OutOfBoundsReturnsNull()
    {
        var source = """
            let nums: number[] = [1, 2, 3];
            console.log(nums.at(10) === null);
            console.log(nums.at(-10) === null);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Array_At_EmptyArray()
    {
        var source = """
            let nums: number[] = [];
            console.log(nums.at(0) === null);
            console.log(nums.at(-1) === null);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Array_At_SingleElement()
    {
        var source = """
            let nums: number[] = [42];
            console.log(nums.at(0));
            console.log(nums.at(-1));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42\n42\n", output);
    }
}

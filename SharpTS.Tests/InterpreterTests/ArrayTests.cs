using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

public class ArrayTests
{
    [Fact]
    public void ArrayLiteral_CreatesArray()
    {
        var source = """
            let arr: number[] = [1, 2, 3];
            console.log(arr);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("[1, 2, 3]\n", output);
    }

    [Fact]
    public void ArrayIndexing_ReturnsCorrectElement()
    {
        var source = """
            let arr: number[] = [10, 20, 30];
            console.log(arr[0]);
            console.log(arr[1]);
            console.log(arr[2]);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("10\n20\n30\n", output);
    }

    [Fact]
    public void ArrayLength_ReturnsCorrectLength()
    {
        var source = """
            let arr: number[] = [1, 2, 3, 4, 5];
            console.log(arr.length);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("5\n", output);
    }

    [Fact]
    public void ArrayPush_AddsElement()
    {
        var source = """
            let arr: number[] = [1, 2];
            arr.push(3);
            console.log(arr.length);
            console.log(arr[2]);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("3\n3\n", output);
    }

    [Fact]
    public void ArrayPop_RemovesAndReturnsLastElement()
    {
        var source = """
            let arr: number[] = [1, 2, 3];
            let popped: number = arr.pop();
            console.log(popped);
            console.log(arr.length);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("3\n2\n", output);
    }

    [Fact]
    public void ArrayShift_RemovesAndReturnsFirstElement()
    {
        var source = """
            let arr: number[] = [1, 2, 3];
            let shifted: number = arr.shift();
            console.log(shifted);
            console.log(arr.length);
            console.log(arr[0]);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n2\n2\n", output);
    }

    [Fact]
    public void ArrayUnshift_AddsElementAtBeginning()
    {
        var source = """
            let arr: number[] = [2, 3];
            arr.unshift(1);
            console.log(arr[0]);
            console.log(arr.length);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n3\n", output);
    }

    [Fact]
    public void ArraySlice_ReturnsSubarray()
    {
        var source = """
            let arr: number[] = [1, 2, 3, 4, 5];
            let sliced: number[] = arr.slice(1, 4);
            console.log(sliced.length);
            console.log(sliced[0]);
            console.log(sliced[2]);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("3\n2\n4\n", output);
    }

    [Fact]
    public void ArrayMap_TransformsElements()
    {
        var source = """
            function double(n: number): number {
                return n * 2;
            }
            let arr: number[] = [1, 2, 3];
            let doubled: number[] = arr.map(double);
            console.log(doubled[0]);
            console.log(doubled[1]);
            console.log(doubled[2]);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("2\n4\n6\n", output);
    }

    [Fact]
    public void ArrayFilter_FiltersElements()
    {
        var source = """
            function isEven(n: number): boolean {
                return n % 2 == 0;
            }
            let arr: number[] = [1, 2, 3, 4, 5, 6];
            let evens: number[] = arr.filter(isEven);
            console.log(evens.length);
            console.log(evens[0]);
            console.log(evens[1]);
            console.log(evens[2]);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("3\n2\n4\n6\n", output);
    }

    [Fact]
    public void ArrayForEach_IteratesElements()
    {
        var source = """
            let arr: number[] = [1, 2, 3];
            function printNum(n: number): void {
                console.log(n);
            }
            arr.forEach(printNum);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n2\n3\n", output);
    }

    [Fact]
    public void ArrayIndexAssignment_ModifiesElement()
    {
        var source = """
            let arr: number[] = [1, 2, 3];
            arr[1] = 99;
            console.log(arr[1]);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("99\n", output);
    }

    [Fact]
    public void NestedArray_AccessesCorrectly()
    {
        var source = """
            let matrix: number[][] = [[1, 2], [3, 4]];
            console.log(matrix[0][0]);
            console.log(matrix[0][1]);
            console.log(matrix[1][0]);
            console.log(matrix[1][1]);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n2\n3\n4\n", output);
    }

    [Fact]
    public void EmptyArray_HasZeroLength()
    {
        var source = """
            let arr: number[] = [];
            console.log(arr.length);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("0\n", output);
    }
}

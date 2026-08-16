using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for Array.fill() method.
/// Runs against both interpreter and compiler.
/// </summary>
public class ArrayFillTests
{
    #region Basic Fill Tests

    [Theory, ModeData]
    public void Array_Fill_FillsEntireArray(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3, 4, 5];
            arr.fill(0);
            console.log(arr.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0,0,0,0,0\n", output);
    }

    [Theory, ModeData]
    public void Array_Fill_ReturnsSameArray(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3];
            let result: number[] = arr.fill(9);
            console.log(arr === result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Array_Fill_WithDifferentValueType(ExecutionMode mode)
    {
        var source = """
            let arr: string[] = ["a", "b", "c"];
            arr.fill("x");
            console.log(arr.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("x,x,x\n", output);
    }

    #endregion

    #region Start Index Tests

    [Theory, ModeData]
    public void Array_Fill_WithStartIndex(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3, 4, 5];
            arr.fill(0, 2);
            console.log(arr.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1,2,0,0,0\n", output);
    }

    [Theory, ModeData]
    public void Array_Fill_WithNegativeStartIndex(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3, 4, 5];
            arr.fill(0, -2);
            console.log(arr.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1,2,3,0,0\n", output);
    }

    [Theory, ModeData]
    public void Array_Fill_StartIndexBeyondLength(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3];
            arr.fill(0, 10);
            console.log(arr.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1,2,3\n", output);
    }

    [Theory, ModeData]
    public void Array_Fill_NegativeStartIndexBeyondLength(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3];
            arr.fill(0, -10);
            console.log(arr.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0,0,0\n", output);
    }

    #endregion

    #region End Index Tests

    [Theory, ModeData]
    public void Array_Fill_WithStartAndEndIndex(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3, 4, 5];
            arr.fill(0, 1, 4);
            console.log(arr.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1,0,0,0,5\n", output);
    }

    [Theory, ModeData]
    public void Array_Fill_WithNegativeEndIndex(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3, 4, 5];
            arr.fill(0, 1, -1);
            console.log(arr.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1,0,0,0,5\n", output);
    }

    [Theory, ModeData]
    public void Array_Fill_EndIndexBeforeStart(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3, 4, 5];
            arr.fill(0, 3, 1);
            console.log(arr.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1,2,3,4,5\n", output);
    }

    [Theory, ModeData]
    public void Array_Fill_EndIndexBeyondLength(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3];
            arr.fill(0, 1, 10);
            console.log(arr.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1,0,0\n", output);
    }

    #endregion

    #region Edge Cases

    [Theory, ModeData]
    public void Array_Fill_EmptyArray(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [];
            arr.fill(0);
            console.log(arr.length);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }

    [Theory, ModeData]
    public void Array_Fill_SingleElement(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1];
            arr.fill(99);
            console.log(arr[0]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("99\n", output);
    }

    [Theory, ModeData]
    public void Array_Fill_WithNull(ExecutionMode mode)
    {
        var source = """
            let arr: (number | null)[] = [1, 2, 3];
            arr.fill(null);
            console.log(arr[0]);
            console.log(arr[1]);
            console.log(arr[2]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("null\nnull\nnull\n", output);
    }

    [Theory, ModeData]
    public void Array_Fill_WithObject(ExecutionMode mode)
    {
        // Test that fill uses the same object reference for all elements
        var source = """
            let obj = { x: 1 };
            let arr = [{ x: 0 }, { x: 0 }, { x: 0 }];
            arr.fill(obj);
            arr[0].x = 99;
            console.log(arr[1].x);
            """;

        var output = TestHarness.Run(source, mode);
        // All elements reference the same object
        Assert.Equal("99\n", output);
    }

    #endregion

    #region Chaining Tests

    [Theory, ModeData]
    public void Array_Fill_Chaining(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3, 4, 5];
            let result: string = arr.fill(0, 0, 2).fill(9, 3).join(",");
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0,0,3,9,9\n", output);
    }

    #endregion
}

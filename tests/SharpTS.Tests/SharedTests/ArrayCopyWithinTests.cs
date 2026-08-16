using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for Array.copyWithin() method.
/// Runs against both interpreter and compiler.
/// </summary>
public class ArrayCopyWithinTests
{
    #region Basic CopyWithin Tests

    [Theory, ModeData]
    public void Array_CopyWithin_BasicCopy(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3, 4, 5];
            arr.copyWithin(0, 3);
            console.log(arr.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("4,5,3,4,5\n", output);
    }

    [Theory, ModeData]
    public void Array_CopyWithin_ReturnsSameArray(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3];
            let result: number[] = arr.copyWithin(0, 1);
            console.log(arr === result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Array_CopyWithin_WithStartAndEnd(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3, 4, 5];
            arr.copyWithin(0, 3, 4);
            console.log(arr.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("4,2,3,4,5\n", output);
    }

    [Theory, ModeData]
    public void Array_CopyWithin_CopyToMiddle(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3, 4, 5];
            arr.copyWithin(1, 3);
            console.log(arr.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1,4,5,4,5\n", output);
    }

    #endregion

    #region Negative Index Tests

    [Theory, ModeData]
    public void Array_CopyWithin_NegativeTarget(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3, 4, 5];
            arr.copyWithin(-2, 0);
            console.log(arr.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1,2,3,1,2\n", output);
    }

    [Theory, ModeData]
    public void Array_CopyWithin_NegativeStart(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3, 4, 5];
            arr.copyWithin(0, -2);
            console.log(arr.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("4,5,3,4,5\n", output);
    }

    [Theory, ModeData]
    public void Array_CopyWithin_NegativeEnd(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3, 4, 5];
            arr.copyWithin(0, 2, -1);
            console.log(arr.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3,4,3,4,5\n", output);
    }

    [Theory, ModeData]
    public void Array_CopyWithin_AllNegativeIndices(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3, 4, 5];
            arr.copyWithin(-2, -3, -1);
            console.log(arr.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1,2,3,3,4\n", output);
    }

    #endregion

    #region Overlapping Region Tests

    [Theory, ModeData]
    public void Array_CopyWithin_OverlappingForward(ExecutionMode mode)
    {
        // Copying from index 0 to index 1 with overlap
        // [1, 2, 3, 4, 5] -> copy [1,2,3,4] to index 1
        // Expected: [1, 1, 2, 3, 4]
        var source = """
            let arr: number[] = [1, 2, 3, 4, 5];
            arr.copyWithin(1, 0, 4);
            console.log(arr.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1,1,2,3,4\n", output);
    }

    [Theory, ModeData]
    public void Array_CopyWithin_OverlappingBackward(ExecutionMode mode)
    {
        // Copying from index 2 to index 0 with overlap
        // [1, 2, 3, 4, 5] -> copy [3,4,5] to index 0
        // Expected: [3, 4, 5, 4, 5]
        var source = """
            let arr: number[] = [1, 2, 3, 4, 5];
            arr.copyWithin(0, 2);
            console.log(arr.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3,4,5,4,5\n", output);
    }

    #endregion

    #region Edge Cases

    [Theory, ModeData]
    public void Array_CopyWithin_EmptyArray(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [];
            arr.copyWithin(0, 0);
            console.log(arr.length);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }

    [Theory, ModeData]
    public void Array_CopyWithin_SingleElement(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1];
            arr.copyWithin(0, 0);
            console.log(arr[0]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n", output);
    }

    [Theory, ModeData]
    public void Array_CopyWithin_TargetBeyondLength(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3];
            arr.copyWithin(10, 0);
            console.log(arr.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1,2,3\n", output);
    }

    [Theory, ModeData]
    public void Array_CopyWithin_StartBeyondLength(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3];
            arr.copyWithin(0, 10);
            console.log(arr.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1,2,3\n", output);
    }

    [Theory, ModeData]
    public void Array_CopyWithin_EndBeforeStart(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3, 4, 5];
            arr.copyWithin(0, 3, 1);
            console.log(arr.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1,2,3,4,5\n", output);
    }

    [Theory, ModeData]
    public void Array_CopyWithin_SameTargetAndStart(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3, 4, 5];
            arr.copyWithin(2, 2);
            console.log(arr.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1,2,3,4,5\n", output);
    }

    [Theory, ModeData]
    public void Array_CopyWithin_NegativeTargetBeyondLength(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3];
            arr.copyWithin(-10, 0);
            console.log(arr.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1,2,3\n", output);
    }

    #endregion

    #region Chaining Tests

    [Theory, ModeData]
    public void Array_CopyWithin_Chaining(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3, 4, 5];
            let result: string = arr.copyWithin(0, 3).copyWithin(2, 0, 2).join(",");
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        // First copyWithin(0, 3): [4, 5, 3, 4, 5]
        // Second copyWithin(2, 0, 2): copy [4, 5] to index 2 -> [4, 5, 4, 5, 5]
        Assert.Equal("4,5,4,5,5\n", output);
    }

    #endregion

    #region MDN Examples

    [Theory, ModeData]
    public void Array_CopyWithin_MDNExample1(ExecutionMode mode)
    {
        // MDN: [1, 2, 3, 4, 5].copyWithin(-2)
        // Expected: [1, 2, 3, 1, 2]
        var source = """
            let arr: number[] = [1, 2, 3, 4, 5];
            arr.copyWithin(-2);
            console.log(arr.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1,2,3,1,2\n", output);
    }

    [Theory, ModeData]
    public void Array_CopyWithin_MDNExample2(ExecutionMode mode)
    {
        // MDN: [1, 2, 3, 4, 5].copyWithin(0, 3)
        // Expected: [4, 5, 3, 4, 5]
        var source = """
            let arr: number[] = [1, 2, 3, 4, 5];
            arr.copyWithin(0, 3);
            console.log(arr.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("4,5,3,4,5\n", output);
    }

    [Theory, ModeData]
    public void Array_CopyWithin_MDNExample3(ExecutionMode mode)
    {
        // MDN: [1, 2, 3, 4, 5].copyWithin(0, 3, 4)
        // Expected: [4, 2, 3, 4, 5]
        var source = """
            let arr: number[] = [1, 2, 3, 4, 5];
            arr.copyWithin(0, 3, 4);
            console.log(arr.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("4,2,3,4,5\n", output);
    }

    [Theory, ModeData]
    public void Array_CopyWithin_MDNExample4(ExecutionMode mode)
    {
        // MDN: [1, 2, 3, 4, 5].copyWithin(-2, -3, -1)
        // Expected: [1, 2, 3, 3, 4]
        var source = """
            let arr: number[] = [1, 2, 3, 4, 5];
            arr.copyWithin(-2, -3, -1);
            console.log(arr.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1,2,3,3,4\n", output);
    }

    #endregion

    #region With Different Types

    [Theory, ModeData]
    public void Array_CopyWithin_WithStrings(ExecutionMode mode)
    {
        var source = """
            let arr: string[] = ["a", "b", "c", "d", "e"];
            arr.copyWithin(0, 3);
            console.log(arr.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("d,e,c,d,e\n", output);
    }

    [Theory, ModeData]
    public void Array_CopyWithin_WithObjects(ExecutionMode mode)
    {
        var source = """
            let obj1 = { x: 1 };
            let obj2 = { x: 2 };
            let obj3 = { x: 3 };
            let arr = [obj1, obj2, obj3];
            arr.copyWithin(0, 2);
            console.log(arr[0] === obj3);
            console.log(arr[1] === obj2);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n", output);
    }

    #endregion
}

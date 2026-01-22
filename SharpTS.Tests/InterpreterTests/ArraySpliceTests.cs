using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

public class ArraySpliceTests
{
    // ========== splice() - Basic Operations ==========

    [Fact]
    public void Array_Splice_DeleteMiddleElements()
    {
        // [1,2,3,4,5].splice(1, 2) -> deleted [2,3], arr becomes [1,4,5]
        var source = """
            let arr: number[] = [1, 2, 3, 4, 5];
            let deleted = arr.splice(1, 2);
            console.log(deleted.join(","));
            console.log(arr.join(","));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("2,3\n1,4,5\n", output);
    }

    [Fact]
    public void Array_Splice_InsertWithoutDeleting()
    {
        // [1,2,3].splice(1, 0, 'a', 'b') -> deleted [], arr becomes [1,'a','b',2,3]
        var source = """
            let arr: any[] = [1, 2, 3];
            let deleted = arr.splice(1, 0, "a", "b");
            console.log(deleted.length);
            console.log(arr.join(","));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("0\n1,a,b,2,3\n", output);
    }

    [Fact]
    public void Array_Splice_ReplaceElement()
    {
        // [1,2,3].splice(1, 1, 'x') -> deleted [2], arr becomes [1,'x',3]
        var source = """
            let arr: any[] = [1, 2, 3];
            let deleted = arr.splice(1, 1, "x");
            console.log(deleted.join(","));
            console.log(arr.join(","));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("2\n1,x,3\n", output);
    }

    // ========== splice() - Negative Indices ==========

    [Fact]
    public void Array_Splice_NegativeStart_DeleteToEnd()
    {
        // [1,2,3].splice(-1) -> deleted [3], arr becomes [1,2]
        var source = """
            let arr: number[] = [1, 2, 3];
            let deleted = arr.splice(-1);
            console.log(deleted.join(","));
            console.log(arr.join(","));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("3\n1,2\n", output);
    }

    [Fact]
    public void Array_Splice_NegativeStart_WithDeleteCount()
    {
        // [1,2,3].splice(-2, 1) -> deleted [2], arr becomes [1,3]
        var source = """
            let arr: number[] = [1, 2, 3];
            let deleted = arr.splice(-2, 1);
            console.log(deleted.join(","));
            console.log(arr.join(","));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("2\n1,3\n", output);
    }

    [Fact]
    public void Array_Splice_NegativeStart_BeyondLength()
    {
        // [1,2,3].splice(-10, 1) -> start clamps to 0, deleted [1], arr becomes [2,3]
        var source = """
            let arr: number[] = [1, 2, 3];
            let deleted = arr.splice(-10, 1);
            console.log(deleted.join(","));
            console.log(arr.join(","));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n2,3\n", output);
    }

    // ========== splice() - No DeleteCount (delete to end) ==========

    [Fact]
    public void Array_Splice_NoDeleteCount_DeleteToEnd()
    {
        // [1,2,3].splice(1) -> deleted [2,3], arr becomes [1]
        var source = """
            let arr: number[] = [1, 2, 3];
            let deleted = arr.splice(1);
            console.log(deleted.join(","));
            console.log(arr.join(","));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("2,3\n1\n", output);
    }

    // ========== splice() - Start Beyond Length ==========

    [Fact]
    public void Array_Splice_StartBeyondLength_InsertAtEnd()
    {
        // [1,2,3].splice(10, 1, 'x') -> deleted [], arr becomes [1,2,3,'x']
        var source = """
            let arr: any[] = [1, 2, 3];
            let deleted = arr.splice(10, 1, "x");
            console.log(deleted.length);
            console.log(arr.join(","));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("0\n1,2,3,x\n", output);
    }

    // ========== splice() - Coercion Edge Cases ==========

    [Fact]
    public void Array_Splice_NaN_Start_TreatedAsZero()
    {
        // [1,2,3].splice(NaN, 1) -> start=0, deleted [1]
        var source = """
            let arr: number[] = [1, 2, 3];
            let deleted = arr.splice(NaN, 1);
            console.log(deleted.join(","));
            console.log(arr.join(","));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n2,3\n", output);
    }

    [Fact]
    public void Array_Splice_Float_Truncated()
    {
        // [1,2,3].splice(0.9, 1.9) -> truncated to (0,1), deleted [1]
        var source = """
            let arr: number[] = [1, 2, 3];
            let deleted = arr.splice(0.9, 1.9);
            console.log(deleted.join(","));
            console.log(arr.join(","));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n2,3\n", output);
    }

    // ========== splice() - Empty Array ==========

    [Fact]
    public void Array_Splice_EmptyArray_InsertElements()
    {
        // [].splice(0, 0, 1, 2) -> deleted [], arr becomes [1,2]
        var source = """
            let arr: number[] = [];
            let deleted = arr.splice(0, 0, 1, 2);
            console.log(deleted.length);
            console.log(arr.join(","));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("0\n1,2\n", output);
    }

    [Fact]
    public void Array_Splice_NoArguments_ReturnsEmpty()
    {
        // [1,2,3].splice() -> deleted [], arr unchanged
        var source = """
            let arr: number[] = [1, 2, 3];
            let deleted = arr.splice();
            console.log(deleted.length);
            console.log(arr.join(","));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("0\n1,2,3\n", output);
    }

    // ========== toSpliced() - Basic Operations ==========

    [Fact]
    public void Array_ToSpliced_ReturnsNewArray()
    {
        var source = """
            let arr: any[] = [1, 2, 3];
            let spliced = arr.toSpliced(1, 1, "a");
            console.log(spliced.join(","));
            console.log(arr.join(","));
            console.log(arr === spliced);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1,a,3\n1,2,3\nfalse\n", output);
    }

    [Fact]
    public void Array_ToSpliced_OriginalUnchanged()
    {
        var source = """
            let original: number[] = [1, 2, 3];
            let spliced = original.toSpliced(0, 1);
            console.log(original.join(","));
            console.log(spliced.join(","));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1,2,3\n2,3\n", output);
    }

    [Fact]
    public void Array_ToSpliced_NegativeStart()
    {
        var source = """
            let arr: number[] = [1, 2, 3, 4, 5];
            let spliced = arr.toSpliced(-2, 1, 99);
            console.log(spliced.join(","));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1,2,3,99,5\n", output);
    }

    [Fact]
    public void Array_ToSpliced_NoArguments_ReturnsCopy()
    {
        var source = """
            let arr: number[] = [1, 2, 3];
            let spliced = arr.toSpliced();
            console.log(spliced.join(","));
            console.log(arr === spliced);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1,2,3\nfalse\n", output);
    }

    [Fact]
    public void Array_ToSpliced_InsertMultiple()
    {
        var source = """
            let arr: any[] = [1, 2, 3];
            let spliced = arr.toSpliced(1, 0, "a", "b", "c");
            console.log(spliced.join(","));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1,a,b,c,2,3\n", output);
    }

    [Fact]
    public void Array_ToSpliced_DeleteAll()
    {
        var source = """
            let arr: number[] = [1, 2, 3, 4, 5];
            let spliced = arr.toSpliced(0);
            console.log(spliced.length);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("0\n", output);
    }

    // ========== Frozen/Sealed Arrays ==========

    [Fact]
    public void Array_Splice_OnFrozenArray_ThrowsTypeError()
    {
        var source = """
            let arr: number[] = [1, 2, 3];
            Object.freeze(arr);
            arr.splice(0, 1);
            """;

        var ex = Assert.Throws<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("TypeError", ex.Message);
    }

    [Fact]
    public void Array_ToSpliced_OnFrozenArray_Works()
    {
        // toSpliced creates a new array, so it works on frozen arrays
        var source = """
            let arr: number[] = [1, 2, 3];
            Object.freeze(arr);
            let spliced = arr.toSpliced(1, 1, 99);
            console.log(spliced.join(","));
            console.log(arr.join(","));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1,99,3\n1,2,3\n", output);
    }

    [Fact]
    public void Array_Splice_OnSealedArray_ThrowsTypeError()
    {
        var source = """
            let arr: number[] = [1, 2, 3];
            Object.seal(arr);
            arr.splice(0, 1);
            """;

        var ex = Assert.Throws<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("TypeError", ex.Message);
    }

    // ========== Additional Edge Cases ==========

    [Fact]
    public void Array_Splice_DeleteMoreThanAvailable()
    {
        // Deleting more elements than available should delete up to the end
        var source = """
            let arr: number[] = [1, 2, 3];
            let deleted = arr.splice(1, 100);
            console.log(deleted.join(","));
            console.log(arr.join(","));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("2,3\n1\n", output);
    }

    [Fact]
    public void Array_Splice_ZeroDeleteCount()
    {
        var source = """
            let arr: number[] = [1, 2, 3];
            let deleted = arr.splice(1, 0);
            console.log(deleted.length);
            console.log(arr.join(","));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("0\n1,2,3\n", output);
    }

    [Fact]
    public void Array_Splice_NegativeDeleteCount_TreatedAsZero()
    {
        // Negative deleteCount should be treated as 0
        var source = """
            let arr: number[] = [1, 2, 3];
            let deleted = arr.splice(1, -5);
            console.log(deleted.length);
            console.log(arr.join(","));
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("0\n1,2,3\n", output);
    }
}

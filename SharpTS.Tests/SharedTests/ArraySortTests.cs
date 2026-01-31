using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for array sort() and toSorted() methods. Runs against both interpreter and compiler.
/// </summary>
public class ArraySortTests
{
    #region Default Lexicographic Sort

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_Sort_DefaultLexicographic_NumbersAsStrings(ExecutionMode mode)
    {
        // JavaScript: [10, 2, 1].sort() -> [1, 10, 2] (lexicographic)
        var source = """
            let nums: number[] = [10, 2, 1];
            nums.sort();
            console.log(nums.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1,10,2\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_Sort_DefaultLexicographic_Strings(ExecutionMode mode)
    {
        var source = """
            let strs: string[] = ["banana", "apple", "cherry"];
            strs.sort();
            console.log(strs.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("apple,banana,cherry\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_Sort_ReturnsReferenceToSameArray(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [3, 1, 2];
            let result = arr.sort();
            console.log(arr === result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_Sort_MutatesOriginalArray(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [3, 1, 2];
            arr.sort();
            console.log(arr[0]);
            console.log(arr[1]);
            console.log(arr[2]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n3\n", output);
    }

    #endregion

    #region Sort With Compare Function

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_Sort_NumericAscending(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [10, 2, 1];
            nums.sort((a: number, b: number): number => a - b);
            console.log(nums.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1,2,10\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_Sort_NumericDescending(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [1, 2, 10];
            nums.sort((a: number, b: number): number => b - a);
            console.log(nums.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10,2,1\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_Sort_CustomObjectProperty(ExecutionMode mode)
    {
        var source = """
            interface Item { name: string; value: number; }
            let items: Item[] = [
                { name: "b", value: 2 },
                { name: "a", value: 1 },
                { name: "c", value: 3 }
            ];
            items.sort((a: Item, b: Item): number => a.value - b.value);
            console.log(items[0].name);
            console.log(items[1].name);
            console.log(items[2].name);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("a\nb\nc\n", output);
    }

    #endregion

    #region Stability

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_Sort_IsStable(ExecutionMode mode)
    {
        // Objects with same key should preserve original order
        var source = """
            interface Item { name: string; key: number; }
            let items: Item[] = [
                { name: "first", key: 1 },
                { name: "second", key: 1 },
                { name: "third", key: 2 }
            ];
            items.sort((a: Item, b: Item): number => a.key - b.key);
            // first and second both have key=1, should stay in original order
            console.log(items[0].name);
            console.log(items[1].name);
            console.log(items[2].name);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("first\nsecond\nthird\n", output);
    }

    #endregion

    #region Edge Cases

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_Sort_EmptyArray(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [];
            arr.sort();
            console.log(arr.length);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_Sort_SingleElement(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [42];
            arr.sort();
            console.log(arr[0]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_Sort_LargerArray(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [5, 3, 8, 1, 9, 2, 7, 4, 6];
            arr.sort((a: number, b: number): number => a - b);
            console.log(arr.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1,2,3,4,5,6,7,8,9\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_Sort_UndefinedMovedToEnd(ExecutionMode mode)
    {
        // JavaScript spec: undefined values are always sorted to end
        var source = """
            let arr: (number | undefined)[] = [3, undefined, 1, undefined, 2];
            arr.sort();
            // Default sort: numbers as strings, undefined at end
            console.log(arr.length);
            console.log(arr[0]);
            console.log(arr[1]);
            console.log(arr[2]);
            console.log(arr[3]);
            console.log(arr[4]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\n1\n2\n3\nundefined\nundefined\n", output);
    }

    #endregion

    #region ToSorted

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_ToSorted_ReturnsNewArray(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [3, 1, 2];
            let sorted = arr.toSorted();
            console.log(arr === sorted);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_ToSorted_OriginalUnchanged(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [3, 1, 2];
            arr.toSorted();
            console.log(arr.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3,1,2\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_ToSorted_DefaultLexicographic(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [10, 2, 1];
            let sorted = arr.toSorted();
            console.log(sorted.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1,10,2\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_ToSorted_WithCompareFn(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [10, 2, 1];
            let sorted = arr.toSorted((a: number, b: number): number => a - b);
            console.log(sorted.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1,2,10\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_ToSorted_Chained(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [3, 1, 2];
            let result = nums
                .toSorted((a: number, b: number): number => a - b)
                .map((n: number): number => n * 2)
                .join(",");
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2,4,6\n", output);
    }

    #endregion

    #region Frozen Array Behavior

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void Array_Sort_FrozenArray_ReturnsUnchanged(ExecutionMode mode)
    {
        // Compiler does not yet enforce frozen array restrictions
        var source = """
            let arr: number[] = [3, 1, 2];
            Object.freeze(arr);
            let result = arr.sort();
            // sort() on frozen array returns unchanged array (silent fail)
            console.log(arr === result);
            console.log(arr.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n3,1,2\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_ToSorted_FrozenArray_Works(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [3, 1, 2];
            Object.freeze(arr);
            let sorted = arr.toSorted((a: number, b: number): number => a - b);
            // toSorted() creates new array, so it works on frozen arrays
            console.log(sorted.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1,2,3\n", output);
    }

    #endregion
}

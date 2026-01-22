using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class ArraySortTests
{
    // ========== sort() - Default Lexicographic ==========

    [Fact]
    public void Array_Sort_DefaultLexicographic_NumbersAsStrings()
    {
        // JavaScript: [10, 2, 1].sort() -> [1, 10, 2] (lexicographic)
        var source = """
            let nums: number[] = [10, 2, 1];
            nums.sort();
            console.log(nums.join(","));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1,10,2\n", output);
    }

    [Fact]
    public void Array_Sort_DefaultLexicographic_Strings()
    {
        var source = """
            let strs: string[] = ["banana", "apple", "cherry"];
            strs.sort();
            console.log(strs.join(","));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("apple,banana,cherry\n", output);
    }

    [Fact]
    public void Array_Sort_ReturnsReferenceToSameArray()
    {
        var source = """
            let arr: number[] = [3, 1, 2];
            let result = arr.sort();
            console.log(arr === result);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Array_Sort_MutatesOriginalArray()
    {
        var source = """
            let arr: number[] = [3, 1, 2];
            arr.sort();
            console.log(arr[0]);
            console.log(arr[1]);
            console.log(arr[2]);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\n2\n3\n", output);
    }

    // ========== sort() - With Compare Function ==========

    [Fact]
    public void Array_Sort_NumericAscending()
    {
        var source = """
            let nums: number[] = [10, 2, 1];
            nums.sort((a: number, b: number): number => a - b);
            console.log(nums.join(","));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1,2,10\n", output);
    }

    [Fact]
    public void Array_Sort_NumericDescending()
    {
        var source = """
            let nums: number[] = [1, 2, 10];
            nums.sort((a: number, b: number): number => b - a);
            console.log(nums.join(","));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("10,2,1\n", output);
    }

    [Fact]
    public void Array_Sort_CustomObjectProperty()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("a\nb\nc\n", output);
    }

    // ========== sort() - Stability ==========

    [Fact]
    public void Array_Sort_IsStable()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("first\nsecond\nthird\n", output);
    }

    // ========== sort() - Edge Cases ==========

    [Fact]
    public void Array_Sort_EmptyArray()
    {
        var source = """
            let arr: number[] = [];
            arr.sort();
            console.log(arr.length);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("0\n", output);
    }

    [Fact]
    public void Array_Sort_SingleElement()
    {
        var source = """
            let arr: number[] = [42];
            arr.sort();
            console.log(arr[0]);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42\n", output);
    }

    // ========== toSorted() - Returns New Array ==========

    [Fact]
    public void Array_ToSorted_ReturnsNewArray()
    {
        var source = """
            let arr: number[] = [3, 1, 2];
            let sorted = arr.toSorted();
            console.log(arr === sorted);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("false\n", output);
    }

    [Fact]
    public void Array_ToSorted_OriginalUnchanged()
    {
        var source = """
            let arr: number[] = [3, 1, 2];
            arr.toSorted();
            console.log(arr.join(","));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("3,1,2\n", output);
    }

    [Fact]
    public void Array_ToSorted_DefaultLexicographic()
    {
        var source = """
            let arr: number[] = [10, 2, 1];
            let sorted = arr.toSorted();
            console.log(sorted.join(","));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1,10,2\n", output);
    }

    [Fact]
    public void Array_ToSorted_WithCompareFn()
    {
        var source = """
            let arr: number[] = [10, 2, 1];
            let sorted = arr.toSorted((a: number, b: number): number => a - b);
            console.log(sorted.join(","));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1,2,10\n", output);
    }

    [Fact]
    public void Array_ToSorted_Chained()
    {
        var source = """
            let nums: number[] = [3, 1, 2];
            let result = nums
                .toSorted((a: number, b: number): number => a - b)
                .map((n: number): number => n * 2)
                .join(",");
            console.log(result);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("2,4,6\n", output);
    }

    [Fact]
    public void Array_Sort_UndefinedMovedToEnd()
    {
        // JavaScript spec: undefined values are always sorted to end,
        // regardless of what the compareFn would return
        var source = """
            let arr: (number | undefined)[] = [3, undefined, 1, undefined, 2];
            arr.sort();
            console.log(arr.join(","));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1,2,3,undefined,undefined\n", output);
    }

    [Fact]
    public void Array_Sort_UndefinedMovedToEnd_WithCompareFn()
    {
        // Even with compareFn that would sort undefined first, undefined goes to end
        var source = """
            let arr: (number | undefined)[] = [3, undefined, 1, undefined, 2];
            arr.sort((a: number | undefined, b: number | undefined): number => {
                // This compareFn would put undefined first if called
                if (a === undefined) return -1;
                if (b === undefined) return 1;
                return (a as number) - (b as number);
            });
            // But per JS spec, undefined always goes to end
            console.log(arr.join(","));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1,2,3,undefined,undefined\n", output);
    }
}

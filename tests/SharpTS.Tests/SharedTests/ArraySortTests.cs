using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for array sort() and toSorted() methods. Runs against both interpreter and compiler.
/// </summary>
public class ArraySortTests
{
    #region Default Lexicographic Sort

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
    public void Array_Sort_ComparatorReceivesUndefinedThis(ExecutionMode mode)
    {
        var source = """
            [2, 1].sort(function (a: number, b: number): number {
                "use strict";
                console.log(this === undefined);
                return a - b;
            });
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Array_Sort_GenericObject_MutatesAndReturnsReceiver(ExecutionMode mode)
    {
        var source = """
            let receiver: any = { 0: 2, 1: 1, 2: 3, length: 3 };
            receiver.sort = Array.prototype.sort;
            let result = receiver.sort();
            console.log(result === receiver);
            console.log(receiver[0] + "," + receiver[1] + "," + receiver[2]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n1,2,3\n", output);
    }

    [Theory, ModeData]
    public void Array_Sort_SymbolReceiver_ReturnsBoxedSymbol(ExecutionMode mode)
    {
        var source = """
            let result = [].sort.call(Symbol());
            console.log(result instanceof Symbol);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Array_Sort_HoleWriteWalksPrototypeSetters(ExecutionMode mode)
    {
        var source = """
            let writes: any[] = [];
            Object.defineProperty(Object.prototype, "1", {
                get() { return 5; },
                set(value: any) { writes.push(value); }
            });
            let values: any[] = [2, , 1];
            values.sort();
            console.log(values[0]);
            console.log(writes.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n", output);
    }

    [Theory, ModeData]
    public void Array_Sort_OwnIndexAccessor_UsesObservablePath(ExecutionMode mode)
    {
        var source = """
            let values: any[] = [3, 2, 1];
            let reads: number = 0;
            let writes: any[] = [];
            Object.defineProperty(values, "1", {
                get(): number { reads++; return 2; },
                set(value: number): void { writes.push(value); },
                configurable: true
            });
            values.sort((a: number, b: number): number => a - b);
            console.log(reads);
            console.log(writes.join(","));
            console.log(values[0] + "," + values[1] + "," + values[2]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n1,2,3\n", output);
    }

    [Theory, ModeData]
    public void Array_Sort_ProxyReceiver_UsesObservablePath(ExecutionMode mode)
    {
        var source = """
            let target: any[] = [3, 2, 1];
            let reads: string[] = [];
            let writes: string[] = [];
            let receiver: any = new Proxy(target, {
                get(obj: any, key: any): any {
                    reads.push(String(key));
                    return obj[key];
                },
                set(obj: any, key: any, value: any): boolean {
                    writes.push(String(key));
                    obj[key] = value;
                    return true;
                }
            });
            Array.prototype.sort.call(
                receiver, (a: number, b: number): number => a - b);
            console.log(reads.join(","));
            console.log(writes.join(","));
            console.log(target.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("length,0,1,2\n0,1,2\n1,2,3\n", output);
    }

    [Theory, ModeData]
    public void Array_Sort_ComparatorInstallingOwnIndexAccessor_BailsOutOfDensePath(
        ExecutionMode mode)
    {
        var source = """
            let values: number[] = [3, 2, 1];
            let writes: number[] = [];
            let changed: boolean = false;
            values.sort((a: number, b: number): number => {
                if (!changed) {
                    changed = true;
                    Object.defineProperty(values, "0", {
                        get(): number { return 99; },
                        set(value: number): void { writes.push(value); },
                        configurable: true
                    });
                }
                return a - b;
            });
            console.log(writes.join(","));
            console.log(values[0] + "," + values[1] + "," + values[2]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n99,2,3\n", output);
    }

    [Theory, ModeData]
    public void Array_Sort_ComparatorInstallingPrototypeIndex_BailsOutOfDensePath(
        ExecutionMode mode)
    {
        var source = """
            let values: number[] = [3, 2, 1];
            let writes: number[] = [];
            let changed: boolean = false;
            try {
                values.sort((a: number, b: number): number => {
                    if (!changed) {
                        changed = true;
                        delete values[1];
                        Object.defineProperty(Array.prototype, "1", {
                            get(): number { return 77; },
                            set(value: number): void { writes.push(value); },
                            configurable: true
                        });
                    }
                    return a - b;
                });
                console.log(writes.join(","));
                console.log(values[0] + "," + values[1] + "," + values[2]);
            } finally {
                delete (Array.prototype as any)[1];
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n1,77,3\n", output);
    }

    [Theory, ModeData]
    public void Array_Sort_ComparatorFreezingReceiver_BailsOutOfDensePath(
        ExecutionMode mode)
    {
        var source = """
            let values: number[] = [3, 2, 1];
            try {
                values.sort((a: number, b: number): number => {
                    Object.freeze(values);
                    return a - b;
                });
            } catch (error) {
                console.log(error instanceof TypeError);
            }
            console.log(values.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n3,2,1\n", output);
    }

    #endregion

    #region Stability

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, InterpretedOnlyData]
    public void Array_Sort_FrozenArray_ThrowsTypeError(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [3, 1, 2];
            Object.freeze(arr);
            try {
                arr.sort();
            } catch (error) {
                console.log(error instanceof TypeError);
            }
            console.log(arr.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n3,1,2\n", output);
    }

    [Theory, ModeData]
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

    #region Comparator Throws (#921)

    [Theory, ModeData]
    public void Array_Sort_ComparatorThrow_ReachesGuestCatchAsError(ExecutionMode mode)
    {
        // A guest throw from the comparator must surface verbatim to the guest catch, not be
        // replaced by the .NET BCL "Failed to compare two elements" message (#921).
        var source = """
            try {
                [2, 1, 3].sort((): number => {
                    throw new TypeError("from comparator");
                });
            } catch (e: any) {
                console.log(typeof e);
                console.log(e instanceof Error);
                console.log(e.message);
                console.log(e.name);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("object\ntrue\nfrom comparator\nTypeError\n", output);
    }

    [Theory, ModeData]
    public void Array_ToSorted_ComparatorThrow_ReachesGuestCatchAsError(ExecutionMode mode)
    {
        var source = """
            try {
                [2, 1, 3].toSorted((): number => {
                    throw new TypeError("from comparator");
                });
            } catch (e: any) {
                console.log(typeof e);
                console.log(e instanceof Error);
                console.log(e.message);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("object\ntrue\nfrom comparator\n", output);
    }

    [Theory, ModeData]
    public void Array_Sort_ComparatorThrowRawString_PreservesGuestIdentity(ExecutionMode mode)
    {
        // A raw string throw keeps its guest identity (string, not Error) — consistent with #694.
        var source = """
            try {
                [2, 1, 3].sort((): number => {
                    throw "raw string";
                });
            } catch (e: any) {
                console.log(typeof e);
                console.log(e instanceof Error);
                console.log(String(e));
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("string\nfalse\nraw string\n", output);
    }

    #endregion
}

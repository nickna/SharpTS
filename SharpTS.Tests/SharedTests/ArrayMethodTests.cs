using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for array methods (find, findIndex, some, every, reduce, includes, indexOf, join, concat, reverse, etc.).
/// Runs against both interpreter and compiler.
/// </summary>
public class ArrayMethodTests
{
    [Fact]
    public void Array_Holes_InheritObjectPrototypeIndexes_InInterpreter()
    {
        var output = TestHarness.RunInterpreted("""
            const prototype: any = Object.prototype;
            prototype[2] = "inherited";
            const values: any = ["own"];
            values.length = 3;
            console.log(values[2]);
            console.log(values.hasOwnProperty("2"));
            const copy: any = values.concat();
            console.log(copy[2]);
            console.log(copy.hasOwnProperty("2"));
            delete prototype[2];
            """);

        Assert.Equal("inherited\nfalse\ninherited\ntrue\n", output);
    }

    #region Find Tests

    [Theory, ModeData]
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

    [Theory, ModeData]
    public void Array_Find_ReturnsUndefinedWhenNotFound(ExecutionMode mode)
    {
        // ECMA-262 23.1.3.10: Array.prototype.find returns undefined when no element matches.
        var source = """
            let nums: number[] = [1, 2, 3];
            let result = nums.find((n: number): boolean => n > 10);
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("undefined\n", output);
    }

    #endregion

    #region FindIndex Tests

    [Theory, ModeData]
    public void Array_FindIndex_ReturnsIndex(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [1, 2, 3, 4, 5];
            console.log(nums.findIndex((n: number): boolean => n > 3));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n", output);
    }

    [Theory, ModeData]
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

    [Theory, ModeData]
    public void Array_Some_ReturnsTrueWhenMatch(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [1, 2, 3, 4, 5];
            console.log(nums.some((n: number): boolean => n > 3));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Array_Some_ReturnsFalseWhenNoMatch(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [1, 2, 3];
            console.log(nums.some((n: number): boolean => n > 10));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\n", output);
    }

    [Theory, ModeData]
    public void Array_Every_ReturnsTrueWhenAllMatch(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [2, 4, 6, 8];
            console.log(nums.every((n: number): boolean => n % 2 == 0));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    #region ReduceRight Tests

    [Theory, ModeData]
    public void Array_ReduceRight_WithInitialValue_ReturnsResult(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [1, 2, 3, 4];
            let sum: number = nums.reduceRight((acc: number, n: number): number => acc + n, 0);
            console.log(sum);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n", output);
    }

    [Theory, ModeData]
    public void Array_With_OutOfBounds_ThrowsRangeError(ExecutionMode mode)
    {
        // Array.prototype.with must throw a real RangeError for out-of-bounds indices. Compiled
        // mode previously threw a generic Error (a CLR Exception whose message merely began
        // "RangeError:"), so Test262's assert.throws(RangeError, ...) — which checks
        // `thrown.constructor === RangeError` — failed. Assert instanceof + constructor === plus
        // valid in-range writes (positive and negative index).
        var source = """
            const a: number[] = [1, 2, 3];
            function check(fn: () => any): string {
                try { fn(); return "no-throw"; }
                catch (e) { return (e instanceof RangeError) + "/" + ((e as any).constructor === RangeError); }
            }
            console.log(check(() => a.with(5, 9)));
            console.log(check(() => a.with(-5, 9)));
            console.log(a.with(1, 9).join(","));
            console.log(a.with(-1, 9).join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true/true\ntrue/true\n1,9,3\n1,2,9\n", output);
    }

    [Theory, ModeData]
    public void Array_With_DoesNotReadReplacedIndex(ExecutionMode mode)
    {
        var source = """
            const values = [0, 1, 2, 3];
            let reads = 0;
            Object.defineProperty(values, "2", {
                get() { reads++; throw new Error("replaced index was read"); }
            });
            console.log("defined", reads);
            const result = values.with(2, 6);
            console.log("copied", reads, result.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("defined 0\ncopied 0 0,1,6,3\n", output);
    }

    [Theory, ModeData]
    public void Array_ReduceRight_WithoutInitialValue_UsesLastElement(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [1, 2, 3, 4];
            let sum: number = nums.reduceRight((acc: number, n: number): number => acc + n);
            console.log(sum);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n", output);
    }

    [Theory, ModeData]
    public void Array_ReduceRight_IteratesRightToLeft(ExecutionMode mode)
    {
        var source = """
            let strs: string[] = ["a", "b", "c", "d"];
            let result: string = strs.reduceRight((acc: string, s: string): string => acc + s, "");
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("dcba\n", output);
    }

    [Theory, ModeData]
    public void Array_ReduceRight_WithIndex_PassesCorrectIndices(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [10, 20, 30];
            let indices: number[] = [];
            nums.reduceRight((acc: number, n: number, i: number): number => {
                indices.push(i);
                return acc + n;
            }, 0);
            console.log(indices.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2,1,0\n", output);
    }

    [Theory, ModeData]
    public void Array_ReduceRight_SingleElement_WithInitial(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [5];
            let result: number = nums.reduceRight((acc: number, n: number): number => acc + n, 10);
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("15\n", output);
    }

    [Theory, ModeData]
    public void Array_ReduceRight_SingleElement_WithoutInitial(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [42];
            let result: number = nums.reduceRight((acc: number, n: number): number => acc + n);
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    #endregion

    #region Includes/IndexOf Tests

    [Theory, ModeData]
    public void Array_Includes_ReturnsTrueWhenFound(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [1, 2, 3, 4, 5];
            console.log(nums.includes(3));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Array_Includes_ReturnsFalseWhenNotFound(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [1, 2, 3, 4, 5];
            console.log(nums.includes(10));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\n", output);
    }

    [Theory, ModeData]
    public void Array_IndexOf_ReturnsIndex(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [1, 2, 3, 4, 5];
            console.log(nums.indexOf(3));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n", output);
    }

    [Theory, ModeData]
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

    [Theory, ModeData]
    public void Array_Join_ReturnsJoinedString(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [1, 2, 3];
            console.log(nums.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1,2,3\n", output);
    }

    [Theory, ModeData]
    public void Array_Join_WithEmptySeparator_ConcatenatesElements(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [1, 2, 3];
            console.log(nums.join(""));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("123\n", output);
    }

    [Theory, ModeData]
    public void Array_Join_NestedArrays_JoinRecursively(ExecutionMode mode)
    {
        // ECMA-262 23.1.3.16: each element is ToString-coerced, so a nested array
        // renders via its own join (default ","), not the debug "[1, 2]" form
        // (#922 follow-up). Also covers `"" + arr` / `${arr}` which share the path.
        var source = """
            console.log([[1, 2], [3]].join("-"));
            console.log("" + [[1, 2], [3]]);
            console.log(`${[[1, 2], [3]]}`);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1,2-3\n1,2,3\n1,2,3\n", output);
    }

    [Theory, ModeData]
    public void Array_Join_PlainObject_UsesObjectObject(ExecutionMode mode)
    {
        // A plain object element ToString-coerces to "[object Object]" (its inherited
        // Object.prototype.toString), not the console/debug "{ a: 1 }" form (#922 follow-up).
        var source = """
            console.log([{ a: 1 }, { b: 2 }].join(", "));
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("[object Object], [object Object]\n", output);
    }

    [Theory, ModeData]
    public void Array_Join_ErrorElement_DispatchesToString(ExecutionMode mode)
    {
        // An Error element in a joined/coerced array dispatches Error.prototype.toString
        // ("RangeError: boom"), not "RangeError instance" / "[object RangeError]" (#922
        // follow-up). Was interpreter-only broken; compiled already correct — pins both.
        var source = """
            const e = new RangeError("boom");
            console.log([e, "y"].join(", "));
            console.log([e].toString());
            console.log("" + [e]);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("RangeError: boom, y\nRangeError: boom\nRangeError: boom\n", output);
    }

    [Theory, ModeData]
    public void Array_Join_UserToString_DispatchesToString(ExecutionMode mode)
    {
        // A user class with its own toString() is dispatched per element (#922
        // follow-up). Compiled mode now dispatches it too — the prior gap where
        // ToJsString returned the class name for non-Error instances is fixed (#931/#933).
        var source = """
            class Pt { x = 1; y = 2; toString() { return `(${this.x},${this.y})`; } }
            console.log([new Pt(), new Pt()].join("|"));
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("(1,2)|(1,2)\n", output);
    }

    [Theory, ModeData]
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

    [Theory, InterpretedOnlyData]
    public void Array_Pop_IsGenericAndObservesInheritedIndexes(ExecutionMode mode)
    {
        var source = """
            Array.prototype[1] = "array-prototype";
            const array: any[] = [0];
            array.length = 2;
            console.log(array.pop());
            console.log(array[1]);
            delete Array.prototype[1];

            const prototype: any = { 1: "object-prototype", length: 2 };
            const object: any = Object.create(prototype);
            console.log(Array.prototype.pop.call(object));
            console.log(object.length);
            console.log(object[1]);

            const large: any = {
                9007199254740990: "large-index",
                length: 9007199254740991
            };
            console.log(Array.prototype.pop.call(large));
            console.log(large.length);
            console.log(9007199254740990 in large);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal(
            "array-prototype\narray-prototype\nobject-prototype\n1\nobject-prototype\n" +
            "large-index\n9007199254740990\nfalse\n",
            output);
    }

    [Theory, InterpretedOnlyData]
    public void Array_Push_IsGenericAndSupportsSafeIntegerLengths(ExecutionMode mode)
    {
        var source = """
            const object: any = { length: 4294967296 };
            object.push = Array.prototype.push;
            console.log(object.push("x", "y"));
            console.log(object.length);
            console.log(object[4294967296]);
            console.log(object[4294967297]);

            const large: any = { length: 9007199254740990 };
            console.log(Array.prototype.push.call(large, "last"));
            console.log(large[9007199254740990]);

            const frozen: any = [];
            Object.freeze(frozen);
            try {
                frozen.push();
            } catch (error) {
                console.log(error instanceof TypeError);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal(
            "4294967298\n4294967298\nx\ny\n9007199254740991\nlast\ntrue\n",
            output);
    }

    [Theory, InterpretedOnlyData]
    public void Array_Shift_IsGenericAndPreservesHoles(ExecutionMode mode)
    {
        var source = """
            const object: any = { 0: "first", 2: "third", length: 3 };
            object.shift = Array.prototype.shift;
            console.log(object.shift());
            console.log(object.length);
            console.log(0 in object);
            console.log(object[1]);
            console.log(2 in object);

            Object.prototype[1] = "inherited";
            const inherited: any = { 0: "own", length: 2 };
            console.log(Array.prototype.shift.call(inherited));
            console.log(inherited[0]);
            console.log(inherited.length);
            delete Object.prototype[1];

            const frozen: any = [];
            Object.freeze(frozen);
            try {
                frozen.shift();
            } catch (error) {
                console.log(error instanceof TypeError);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal(
            "first\n2\nfalse\nthird\nfalse\nown\ninherited\n1\ntrue\n",
            output);
    }

    [Theory, InterpretedOnlyData]
    public void Array_Unshift_IsGenericAndPreservesHoles(ExecutionMode mode)
    {
        var source = """
            const object: any = { 0: "first", 2: "third", length: 3 };
            object.unshift = Array.prototype.unshift;
            console.log(object.unshift("a", "b"));
            console.log(object[0]);
            console.log(object[1]);
            console.log(object[2]);
            console.log(3 in object);
            console.log(object[4]);

            const generic: any = { 0: "tail", length: 1 };
            console.log(Array.prototype.unshift.call(generic, "x"));
            console.log(generic[0]);
            console.log(generic[1]);

            const frozen: any = [];
            Object.freeze(frozen);
            try {
                frozen.unshift();
            } catch (error) {
                console.log(error instanceof TypeError);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal(
            "5\na\nb\nfirst\nfalse\nthird\n2\nx\ntail\ntrue\n",
            output);
    }

    [Theory, InterpretedOnlyData]
    public void Array_Reverse_IsGenericAndPreservesHoles(ExecutionMode mode)
    {
        var source = """
            const object: any = { 0: "first", 2: "third", length: 4 };
            object.reverse = Array.prototype.reverse;
            console.log(object.reverse() === object);
            console.log(0 in object);
            console.log(object[1]);
            console.log(2 in object);
            console.log(object[3]);

            Array.prototype[1] = "inherited";
            const array: any[] = ["a"];
            array.length = 2;
            array.reverse();
            console.log(array[0]);
            console.log(array[1]);
            delete Array.prototype[1];
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\nthird\nfalse\nfirst\ninherited\na\n", output);
    }

    [Theory, InterpretedOnlyData]
    public void Array_Fill_IsGenericAndCoercesEmptyBounds(ExecutionMode mode)
    {
        var source = """
            const object: any = { length: 4 };
            object.fill = Array.prototype.fill;
            console.log(object.fill("x", 1, -1) === object);
            console.log(0 in object);
            console.log(object[1]);
            console.log(object[2]);
            console.log(3 in object);

            let coerced = false;
            const empty: any = { length: 0 };
            Array.prototype.fill.call(empty, 1, {
                valueOf() {
                    coerced = true;
                    return 0;
                }
            });
            console.log(coerced);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\nx\nx\nfalse\ntrue\n", output);
    }

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
    public void Array_FindLast_ReturnsUndefinedWhenNotFound(ExecutionMode mode)
    {
        // ECMA-262 23.1.3.11: Array.prototype.findLast returns undefined when no element matches.
        var source = """
            let nums: number[] = [1, 2, 3];
            let result = nums.findLast((n: number): boolean => n > 10);
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("undefined\n", output);
    }

    [Theory, ModeData]
    public void Array_FindLast_EmptyArrayReturnsUndefined(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [];
            let result = nums.findLast((n: number): boolean => n > 0);
            console.log(result);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("undefined\n", output);
    }

    [Theory, ModeData]
    public void Array_FindLastIndex_ReturnsLastMatchingIndex(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [1, 2, 3, 4, 5];
            console.log(nums.findLastIndex((n: number): boolean => n > 2));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("4\n", output);
    }

    [Theory, ModeData]
    public void Array_FindLastIndex_ReturnsMinusOneWhenNotFound(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [1, 2, 3];
            console.log(nums.findLastIndex((n: number): boolean => n > 10));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("-1\n", output);
    }

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
    public void Array_At_OutOfBoundsReturnsUndefined(ExecutionMode mode)
    {
        // ECMA-262 23.1.3.1 specifies `at()` returns undefined (not null) for
        // out-of-range indices.
        var source = """
            let nums: number[] = [1, 2, 3];
            console.log(nums.at(10) === undefined);
            console.log(nums.at(-10) === undefined);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Array_At_EmptyArray(ExecutionMode mode)
    {
        var source = """
            let nums: number[] = [];
            console.log(nums.at(0) === undefined);
            console.log(nums.at(-1) === undefined);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
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

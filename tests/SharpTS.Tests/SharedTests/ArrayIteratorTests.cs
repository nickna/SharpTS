using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for array iterator methods (entries, keys, values).
/// Runs against both interpreter and compiler.
/// </summary>
public class ArrayIteratorTests
{
    #region entries() Tests

    [Theory, ModeData]
    public void Array_Entries_ReturnsIndexValuePairs(ExecutionMode mode)
    {
        var source = """
            let arr = [10, 20, 30];
            for (let entry of arr.entries()) {
                console.log(entry[0] + ":" + entry[1]);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0:10\n1:20\n2:30\n", output);
    }

    [Theory, ModeData]
    public void Array_Entries_WithManualDestructuring(ExecutionMode mode)
    {
        var source = """
            let arr = ["a", "b", "c"];
            for (let entry of arr.entries()) {
                let i = entry[0];
                let val = entry[1];
                console.log(i + "=" + val);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0=a\n1=b\n2=c\n", output);
    }

    [Theory, ModeData]
    public void Array_Entries_EmptyArray(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [];
            let count = 0;
            for (let entry of arr.entries()) {
                count++;
            }
            console.log(count);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }

    [Theory, ModeData]
    public void Array_Iterators_ObserveGrowthUntilPermanentlyExhausted(ExecutionMode mode)
    {
        var source = """
            const entriesArray: any[] = [];
            const entries: any = entriesArray.entries();
            entriesArray.push("a");
            let result: any = entries.next();
            console.log(result.done + ":" + result.value[0] + ":" + result.value[1]);
            console.log(entries.next().done);
            entriesArray.push("b");
            console.log(entries.next().done);

            const keysArray: any[] = [];
            const keys: any = keysArray.keys();
            keysArray.push("a");
            result = keys.next();
            console.log(result.done + ":" + result.value);
            console.log(keys.next().done);
            keysArray.push("b");
            console.log(keys.next().done);

            const valuesArray: any[] = [];
            const values: any = valuesArray.values();
            valuesArray.push("a");
            result = values.next();
            console.log(result.done + ":" + result.value);
            console.log(values.next().done);
            valuesArray.push("b");
            console.log(values.next().done);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal(
            "false:0:a\ntrue\ntrue\nfalse:0\ntrue\ntrue\nfalse:a\ntrue\ntrue\n",
            output);
    }

    [Theory, ModeData]
    public void Array_Entries_WithMixedTypes(ExecutionMode mode)
    {
        var source = """
            let arr: (number | string)[] = [1, "two", 3];
            for (let entry of arr.entries()) {
                console.log(entry[0] + ":" + entry[1]);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0:1\n1:two\n2:3\n", output);
    }

    [Theory, ModeData]
    public void Array_Entries_WithArrayFrom(ExecutionMode mode)
    {
        var source = """
            let arr = [10, 20];
            let entries = Array.from(arr.entries());
            console.log(entries.length);
            console.log(entries[0][0] + ":" + entries[0][1]);
            console.log(entries[1][0] + ":" + entries[1][1]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n0:10\n1:20\n", output);
    }

    #endregion

    #region keys() Tests

    [Theory, ModeData]
    public void Array_Keys_ReturnsIndices(ExecutionMode mode)
    {
        var source = """
            let arr = ["a", "b", "c"];
            for (let key of arr.keys()) {
                console.log(key);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n1\n2\n", output);
    }

    [Theory, ModeData]
    public void Array_Keys_EmptyArray(ExecutionMode mode)
    {
        var source = """
            let arr: string[] = [];
            let count = 0;
            for (let key of arr.keys()) {
                count++;
            }
            console.log(count);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }

    [Theory, ModeData]
    public void Array_Keys_CanAccessArrayElements(ExecutionMode mode)
    {
        var source = """
            let arr = [100, 200, 300];
            for (let i of arr.keys()) {
                console.log(arr[i]);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("100\n200\n300\n", output);
    }

    [Theory, ModeData]
    public void Array_Keys_WithArrayFrom(ExecutionMode mode)
    {
        var source = """
            let arr = [10, 20, 30];
            let keys = Array.from(arr.keys());
            console.log(keys.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0,1,2\n", output);
    }

    #endregion

    #region values() Tests

    [Theory, ModeData]
    public void Array_Values_ReturnsElements(ExecutionMode mode)
    {
        var source = """
            let arr = [10, 20, 30];
            for (let val of arr.values()) {
                console.log(val);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n20\n30\n", output);
    }

    [Theory, ModeData]
    public void Array_Values_EmptyArray(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [];
            let count = 0;
            for (let val of arr.values()) {
                count++;
            }
            console.log(count);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }

    [Theory, ModeData]
    public void Array_Values_WithStrings(ExecutionMode mode)
    {
        var source = """
            let arr = ["hello", "world"];
            for (let val of arr.values()) {
                console.log(val);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello\nworld\n", output);
    }

    [Theory, ModeData]
    public void Array_Values_WithArrayFrom(ExecutionMode mode)
    {
        var source = """
            let arr = [1, 2, 3];
            let values = Array.from(arr.values());
            console.log(values.join("-"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1-2-3\n", output);
    }

    [Theory, ModeData]
    public void Array_Values_CountsNullAndUndefined(ExecutionMode mode)
    {
        var source = """
            let arr: (number | null | undefined)[] = [1, null, undefined, 2];
            let count = 0;
            for (let val of arr.values()) {
                count++;
            }
            console.log(count);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("4\n", output);
    }

    #endregion

    #region Break/Continue Tests

    [Theory, ModeData]
    public void Array_Entries_WithBreak(ExecutionMode mode)
    {
        var source = """
            let arr = [1, 2, 3, 4, 5];
            for (let entry of arr.entries()) {
                let i = entry[0];
                let val = entry[1];
                if (val > 2) break;
                console.log(i + ":" + val);
            }
            console.log("done");
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0:1\n1:2\ndone\n", output);
    }

    [Theory, ModeData]
    public void Array_Keys_WithContinue(ExecutionMode mode)
    {
        var source = """
            let arr = [10, 20, 30, 40];
            for (let i of arr.keys()) {
                if (i === 1 || i === 3) continue;
                console.log(i);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n2\n", output);
    }

    [Theory, ModeData]
    public void Array_Values_WithBreak(ExecutionMode mode)
    {
        var source = """
            let arr = ["a", "b", "c", "d"];
            for (let val of arr.values()) {
                if (val === "c") break;
                console.log(val);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("a\nb\n", output);
    }

    #endregion

    #region Null and Boolean Stringification Tests

    [Theory, ModeData]
    public void Array_Entries_WithNull_StringifiesCorrectly(ExecutionMode mode)
    {
        var source = """
            let arr: (number | null)[] = [1, null, 2];
            for (let entry of arr.entries()) {
                console.log(entry[0] + ":" + entry[1]);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0:1\n1:null\n2:2\n", output);
    }

    [Theory, ModeData]
    public void Array_Entries_WithBoolean_StringifiesCorrectly(ExecutionMode mode)
    {
        var source = """
            let arr: (number | boolean)[] = [1, true, 2, false];
            for (let entry of arr.entries()) {
                console.log(entry[0] + ":" + entry[1]);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0:1\n1:true\n2:2\n3:false\n", output);
    }

    [Theory, ModeData]
    public void StringConcat_WithNull_JavaScriptStyle(ExecutionMode mode)
    {
        var source = """
            let x: string | null = null;
            console.log("value:" + x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("value:null\n", output);
    }

    [Theory, ModeData]
    public void StringConcat_WithBoolean_LowercaseTrue(ExecutionMode mode)
    {
        var source = """
            let x = true;
            console.log("bool:" + x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("bool:true\n", output);
    }

    [Theory, ModeData]
    public void StringConcat_WithBoolean_LowercaseFalse(ExecutionMode mode)
    {
        var source = """
            let x = false;
            console.log("bool:" + x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("bool:false\n", output);
    }

    [Theory, ModeData]
    public void StringConcat_MultipleNullsAndBooleans(ExecutionMode mode)
    {
        var source = """
            let a: number | null = null;
            let b = true;
            let c = false;
            console.log(a + ":" + b + ":" + c);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("null:true:false\n", output);
    }

    #endregion

    #region Edge Cases

    [Theory, ModeData]
    public void Array_Entries_SingleElement(ExecutionMode mode)
    {
        var source = """
            let arr = [42];
            for (let entry of arr.entries()) {
                console.log(entry[0] + ":" + entry[1]);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0:42\n", output);
    }

    [Theory, ModeData]
    public void Array_Keys_LargeArray(ExecutionMode mode)
    {
        var source = """
            let arr = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
            let sum = 0;
            for (let i of arr.keys()) {
                sum += i;
            }
            console.log(sum);
            """;

        var output = TestHarness.Run(source, mode);
        // 0+1+2+3+4+5+6+7+8+9 = 45
        Assert.Equal("45\n", output);
    }

    [Theory, ModeData]
    public void Array_Values_WithObjects(ExecutionMode mode)
    {
        var source = """
            let arr = [{ x: 1 }, { x: 2 }];
            for (let obj of arr.values()) {
                console.log(obj.x);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n", output);
    }

    [Theory, ModeData]
    public void Array_Entries_NestedArrays(ExecutionMode mode)
    {
        var source = """
            let arr = [[1, 2], [3, 4]];
            for (let entry of arr.entries()) {
                let i = entry[0];
                let innerArr = entry[1];
                console.log(i + ":" + innerArr.join("-"));
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0:1-2\n1:3-4\n", output);
    }

    #endregion

    #region Destructuring in for...of Tests

    [Theory, ModeData]
    public void ForOf_ArrayDestructuring_EntriesPattern(ExecutionMode mode)
    {
        var source = """
            let arr = ["a", "b", "c"];
            let results: string[] = [];
            for (const [i, val] of arr.entries()) {
                results.push(i + ":" + val);
            }
            console.log(results.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0:a,1:b,2:c\n", output);
    }

    [Theory, ModeData]
    public void ForOf_ArrayDestructuring_MapEntries(ExecutionMode mode)
    {
        var source = """
            let map = new Map<string, number>();
            map.set("x", 10);
            map.set("y", 20);
            let results: string[] = [];
            for (const [key, value] of map) {
                results.push(key + "=" + value);
            }
            console.log(results.join(";"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("x=10;y=20\n", output);
    }

    [Theory, ModeData]
    public void ForOf_ObjectDestructuring(ExecutionMode mode)
    {
        var source = """
            let items = [
                { name: "Alice", age: 30 },
                { name: "Bob", age: 25 }
            ];
            let results: string[] = [];
            for (const { name, age } of items) {
                results.push(name + " is " + age);
            }
            console.log(results.join("; "));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("Alice is 30; Bob is 25\n", output);
    }

    [Theory, ModeData]
    public void ForOf_ArrayDestructuring_WithRest(ExecutionMode mode)
    {
        var source = """
            let arr = [[1, 2, 3, 4], [5, 6, 7, 8]];
            let results: string[] = [];
            for (const [first, ...rest] of arr) {
                results.push(first + ":" + rest.length);
            }
            console.log(results.join("; "));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1:3; 5:3\n", output);
    }

    [Theory, ModeData]
    public void ForOf_ArrayDestructuring_WithBreak(ExecutionMode mode)
    {
        var source = """
            let arr = ["a", "b", "c", "d"];
            let results: string[] = [];
            for (const [i, val] of arr.entries()) {
                if (i >= 2) break;
                results.push(i + ":" + val);
            }
            console.log(results.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0:a,1:b\n", output);
    }

    [Theory, ModeData]
    public void ForOf_ArrayDestructuring_WithContinue(ExecutionMode mode)
    {
        var source = """
            let arr = ["a", "b", "c", "d"];
            let results: string[] = [];
            for (const [i, val] of arr.entries()) {
                if (i === 1 || i === 3) continue;
                results.push(i + ":" + val);
            }
            console.log(results.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0:a,2:c\n", output);
    }

    [Theory, ModeData]
    public void ForOf_ObjectDestructuring_WithRename(ExecutionMode mode)
    {
        var source = """
            let items = [{ x: 1, y: 2 }, { x: 3, y: 4 }];
            let results: string[] = [];
            for (const { x: a, y: b } of items) {
                results.push(a + "," + b);
            }
            console.log(results.join("; "));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1,2; 3,4\n", output);
    }

    [Theory, ModeData]
    public void ForOf_ArrayDestructuring_WithHole(ExecutionMode mode)
    {
        var source = """
            let arr = [[1, 2, 3], [4, 5, 6]];
            let results: string[] = [];
            for (const [a, , c] of arr) {
                results.push(a + "," + c);
            }
            console.log(results.join("; "));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1,3; 4,6\n", output);
    }

    [Theory, ModeData]
    public void ForOf_ArrayDestructuring_WithLet(ExecutionMode mode)
    {
        var source = """
            let arr = [["a", 1], ["b", 2]];
            let results: string[] = [];
            for (let [key, val] of arr) {
                results.push(key + "=" + val);
            }
            console.log(results.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("a=1,b=2\n", output);
    }

    [Theory, ModeData]
    public void ForOf_ArrayDestructuring_MapWithTypedValues(ExecutionMode mode)
    {
        // Tests destructuring Map entries with typed key/value
        var source = """
            let map = new Map<string, number>();
            map.set("alpha", 1);
            map.set("beta", 2);
            map.set("gamma", 3);
            let sum = 0;
            let keys: string[] = [];
            for (const [k, v] of map) {
                keys.push(k);
                sum += v;
            }
            console.log(keys.join(","));
            console.log(sum);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("alpha,beta,gamma\n6\n", output);
    }

    [Theory, ModeData]
    public void ForOf_ArrayDestructuring_NestedArrays(ExecutionMode mode)
    {
        // Tests destructuring nested arrays in for...of
        var source = """
            let data = [[1, 2, 3], [4, 5, 6], [7, 8, 9]];
            let results: string[] = [];
            for (const [a, b, c] of data) {
                results.push(a + "-" + b + "-" + c);
            }
            console.log(results.join("; "));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1-2-3; 4-5-6; 7-8-9\n", output);
    }

    [Theory, ModeData]
    public void ForOf_ArrayDestructuring_ArrayEntries_Computation(ExecutionMode mode)
    {
        // Tests using destructured values in computation
        var source = """
            let arr = [10, 20, 30, 40];
            let sum = 0;
            for (const [idx, val] of arr.entries()) {
                sum += idx * val;
            }
            console.log(sum);
            """;

        // 0*10 + 1*20 + 2*30 + 3*40 = 0 + 20 + 60 + 120 = 200
        var output = TestHarness.Run(source, mode);
        Assert.Equal("200\n", output);
    }

    [Theory, ModeData]
    public void ForAwaitOf_ArrayDestructuring(ExecutionMode mode)
    {
        var source = """
            async function* asyncGen() {
                yield [0, "first"];
                yield [1, "second"];
                yield [2, "third"];
            }

            async function main() {
                let results: string[] = [];
                for await (const [idx, val] of asyncGen()) {
                    results.push(idx + "=" + val);
                }
                console.log(results.join(", "));
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0=first, 1=second, 2=third\n", output);
    }

    #endregion

    #region Spread Iterator Tests

    [Theory, ModeData]
    public void Spread_ArrayEntries_CreatesArray(ExecutionMode mode)
    {
        var source = """
            let arr = ["a", "b", "c"];
            let entries = [...arr.entries()];
            console.log(entries.length);
            console.log(entries[0][0] + ":" + entries[0][1]);
            console.log(entries[1][0] + ":" + entries[1][1]);
            console.log(entries[2][0] + ":" + entries[2][1]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n0:a\n1:b\n2:c\n", output);
    }

    [Theory, ModeData]
    public void Spread_ArrayKeys_CreatesArray(ExecutionMode mode)
    {
        var source = """
            let arr = [10, 20, 30];
            let keys = [...arr.keys()];
            console.log(keys.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0,1,2\n", output);
    }

    [Theory, ModeData]
    public void Spread_ArrayValues_CreatesArray(ExecutionMode mode)
    {
        var source = """
            let arr = [10, 20, 30];
            let values = [...arr.values()];
            console.log(values.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10,20,30\n", output);
    }

    [Theory, ModeData]
    public void Spread_Set_CreatesArray(ExecutionMode mode)
    {
        var source = """
            let mySet = new Set([1, 2, 3]);
            let arr = [...mySet];
            console.log(arr.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1,2,3\n", output);
    }

    [Theory, ModeData]
    public void Spread_Map_CreatesArrayOfTuples(ExecutionMode mode)
    {
        var source = """
            let myMap = new Map();
            myMap.set("x", 10);
            myMap.set("y", 20);
            let arr = [...myMap];
            console.log(arr.length);
            console.log(arr.join("; "));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\nx,10; y,20\n", output);
    }

    [Theory, ModeData]
    public void Spread_Map_NestedElementAccess(ExecutionMode mode)
    {
        // Tests that arr[i][0] and arr[i][1] work correctly on spread Map entries
        // This specifically tests the KeyValuePair indexing fix in compiled mode
        var source = """
            let myMap = new Map<string, number>();
            myMap.set("a", 100);
            myMap.set("b", 200);
            let arr = [...myMap];
            console.log(arr[0][0]);
            console.log(arr[0][1]);
            console.log(arr[1][0]);
            console.log(arr[1][1]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("a\n100\nb\n200\n", output);
    }

    [Theory, ModeData]
    public void Spread_Map_JsonStringify_ProducesArrayOfPairs(ExecutionMode mode)
    {
        // #953: in compiled mode spread Map entries were boxed KeyValuePair structs, not real
        // arrays, so Array.isArray returned false and JSON.stringify emitted null per entry.
        var source = """
            console.log(JSON.stringify([...new Map([[0, 0.5], [1, 1.5]])]));
            console.log(JSON.stringify([...new Map([["a", 1], ["b", 2]])]));
            console.log(Array.isArray([...new Map([[0, 0.5]])][0]));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("[[0,0.5],[1,1.5]]\n[[\"a\",1],[\"b\",2]]\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Map_Iteration_MaterializesRealArrays(ExecutionMode mode)
    {
        // #953 root-cause fix is shared across for-of / Array.from / Object.fromEntries.
        var source = """
            for (const [k, v] of new Map([[1, 2]])) console.log(k, v);
            console.log(JSON.stringify(Array.from(new Map([[1, 2], [3, 4]]))));
            console.log(JSON.stringify(Object.fromEntries(new Map([["x", 1]]))));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1 2\n[[1,2],[3,4]]\n{\"x\":1}\n", output);
    }

    [Theory, ModeData]
    public void Spread_MapEntries_NestedElementAccess(ExecutionMode mode)
    {
        // Tests explicit .entries() call with nested access
        var source = """
            let myMap = new Map<string, number>();
            myMap.set("x", 1);
            myMap.set("y", 2);
            let arr = [...myMap.entries()];
            console.log(arr[0][0] + "=" + arr[0][1]);
            console.log(arr[1][0] + "=" + arr[1][1]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("x=1\ny=2\n", output);
    }

    [Theory, ModeData]
    public void Spread_Map_AccessInLoop(ExecutionMode mode)
    {
        // Tests iterating over spread Map entries with element access
        var source = """
            let myMap = new Map<string, number>();
            myMap.set("one", 1);
            myMap.set("two", 2);
            myMap.set("three", 3);
            let arr = [...myMap];
            for (let i = 0; i < arr.length; i++) {
                console.log(arr[i][0] + ":" + arr[i][1]);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("one:1\ntwo:2\nthree:3\n", output);
    }

    [Theory, ModeData]
    public void Spread_String_CreatesCharArray(ExecutionMode mode)
    {
        var source = """
            let chars = [..."hello"];
            console.log(chars.join("-"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("h-e-l-l-o\n", output);
    }

    [Theory, ModeData]
    public void Spread_Generator_CreatesArray(ExecutionMode mode)
    {
        var source = """
            function* gen() {
                yield 1;
                yield 2;
                yield 3;
            }
            let arr = [...gen()];
            console.log(arr.join(","));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1,2,3\n", output);
    }

    [Theory, ModeData]
    public void Spread_Iterator_WithOtherElements(ExecutionMode mode)
    {
        // Use any[] type to handle mixed number and tuple elements
        var source = """
            let arr = [10, 20];
            let combined: any[] = [0, ...arr.entries(), 99];
            console.log(combined.length);
            console.log(combined[0]);
            console.log(combined[1][0] + ":" + combined[1][1]);
            console.log(combined[3]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("4\n0\n0:10\n99\n", output);
    }

    #endregion
}

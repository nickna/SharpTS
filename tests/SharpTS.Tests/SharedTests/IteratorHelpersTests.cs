using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for ES2025 Iterator Helpers: map, filter, take, drop, flatMap,
/// reduce, toArray, forEach, some, every, find, plus Iterator.from().
/// Runs against both interpreter and compiler.
/// </summary>
public class IteratorHelpersTests
{
    #region map

    [Theory, ModeData]
    public void Iterator_Map_TransformsElements(ExecutionMode mode)
    {
        var source = @"
            const result = [1, 2, 3].values().map(x => x * 2).toArray();
            console.log(result[0]);
            console.log(result[1]);
            console.log(result[2]);
            console.log(result.length);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n4\n6\n3\n", output);
    }

    #endregion

    #region filter

    [Theory, ModeData]
    public void Iterator_Filter_SelectsMatchingElements(ExecutionMode mode)
    {
        var source = @"
            const result = [1, 2, 3, 4, 5].values().filter(x => x % 2 === 0).toArray();
            console.log(result[0]);
            console.log(result[1]);
            console.log(result.length);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n4\n2\n", output);
    }

    #endregion

    #region take

    [Theory, ModeData]
    public void Iterator_Take_LimitsElements(ExecutionMode mode)
    {
        var source = @"
            const result = [1, 2, 3, 4, 5].values().take(3).toArray();
            console.log(result[0]);
            console.log(result[1]);
            console.log(result[2]);
            console.log(result.length);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n3\n3\n", output);
    }

    #endregion

    #region drop

    [Theory, ModeData]
    public void Iterator_Drop_SkipsElements(ExecutionMode mode)
    {
        var source = @"
            const result = [1, 2, 3, 4, 5].values().drop(2).toArray();
            console.log(result[0]);
            console.log(result[1]);
            console.log(result[2]);
            console.log(result.length);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n4\n5\n3\n", output);
    }

    #endregion

    #region flatMap

    [Theory, ModeData]
    public void Iterator_FlatMap_FlattensResults(ExecutionMode mode)
    {
        var source = @"
            const result = [1, 2, 3].values().flatMap(x => [x, x * 10]).toArray();
            console.log(result[0]);
            console.log(result[1]);
            console.log(result[2]);
            console.log(result[3]);
            console.log(result[4]);
            console.log(result[5]);
            console.log(result.length);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n10\n2\n20\n3\n30\n6\n", output);
    }

    #endregion

    #region reduce

    [Theory, ModeData]
    public void Iterator_Reduce_WithInitialValue(ExecutionMode mode)
    {
        var source = @"
            const sum = [1, 2, 3].values().reduce((acc, x) => acc + x, 0);
            console.log(sum);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("6\n", output);
    }

    [Theory, ModeData]
    public void Iterator_Reduce_WithoutInitialValue(ExecutionMode mode)
    {
        var source = @"
            const sum = [1, 2, 3].values().reduce((acc, x) => acc + x);
            console.log(sum);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("6\n", output);
    }

    #endregion

    #region forEach

    [Theory, ModeData]
    public void Iterator_ForEach_CallsForEachElement(ExecutionMode mode)
    {
        var source = @"
            [10, 20, 30].values().forEach(x => console.log(x));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n20\n30\n", output);
    }

    #endregion

    #region some

    [Theory, ModeData]
    public void Iterator_Some_TruthyCase(ExecutionMode mode)
    {
        var source = @"
            const result = [1, 2, 3].values().some(x => x > 2);
            console.log(result);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Iterator_Some_FalsyCase(ExecutionMode mode)
    {
        var source = @"
            const result = [1, 2, 3].values().some(x => x > 10);
            console.log(result);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\n", output);
    }

    #endregion

    #region every

    [Theory, ModeData]
    public void Iterator_Every_TruthyCase(ExecutionMode mode)
    {
        var source = @"
            const result = [1, 2, 3].values().every(x => x > 0);
            console.log(result);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Iterator_Every_FalsyCase(ExecutionMode mode)
    {
        var source = @"
            const result = [1, 2, 3].values().every(x => x > 1);
            console.log(result);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\n", output);
    }

    #endregion

    #region find

    [Theory, ModeData]
    public void Iterator_Find_FoundCase(ExecutionMode mode)
    {
        var source = @"
            const result = [1, 2, 3, 4].values().find(x => x > 2);
            console.log(result);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n", output);
    }

    [Theory, ModeData]
    public void Iterator_Find_NotFoundCase(ExecutionMode mode)
    {
        var source = @"
            const result = [1, 2, 3].values().find(x => x > 10);
            console.log(result);
        ";
        var output = TestHarness.Run(source, mode);
        // find returns undefined when not found (null in our runtime)
        Assert.Contains("null", output);
    }

    #endregion

    #region toArray

    [Theory, ModeData]
    public void Iterator_ToArray_CollectsElements(ExecutionMode mode)
    {
        var source = @"
            const arr = [5, 10, 15].values().toArray();
            console.log(arr.length);
            console.log(arr[0]);
            console.log(arr[1]);
            console.log(arr[2]);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n5\n10\n15\n", output);
    }

    #endregion

    #region Chaining

    [Theory, ModeData]
    public void Iterator_Chaining_MapFilterTake(ExecutionMode mode)
    {
        var source = @"
            const result = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]
                .values()
                .map(x => x * 2)
                .filter(x => x > 5)
                .take(3)
                .toArray();
            console.log(result[0]);
            console.log(result[1]);
            console.log(result[2]);
            console.log(result.length);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("6\n8\n10\n3\n", output);
    }

    #endregion

    #region Generator + iterator helpers

    [Theory, ModeData]
    public void Iterator_GeneratorWithTake_IsLazy(ExecutionMode mode)
    {
        // Both modes support lazy generators:
        // - Compiled: IL state machine with yield resume points
        // - Interpreted: Thread-based coroutines with ManualResetEventSlim synchronization
        var source = @"
            function* naturals(): Generator<number> {
                let n = 1;
                while (true) {
                    yield n;
                    n = n + 1;
                }
            }
            const result = naturals().take(5).toArray();
            console.log(result[0]);
            console.log(result[1]);
            console.log(result[2]);
            console.log(result[3]);
            console.log(result[4]);
            console.log(result.length);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n3\n4\n5\n5\n", output);
    }

    [Theory, ModeData]
    public void Iterator_GeneratorMapFilter(ExecutionMode mode)
    {
        var source = @"
            function* range(start: number, end: number): Generator<number> {
                let i = start;
                while (i < end) {
                    yield i;
                    i = i + 1;
                }
            }
            const result = range(1, 10)
                .filter(n => n % 2 === 0)
                .map(n => n * n)
                .toArray();
            console.log(result[0]);
            console.log(result[1]);
            console.log(result[2]);
            console.log(result[3]);
            console.log(result.length);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("4\n16\n36\n64\n4\n", output);
    }

    #endregion

    #region Map/Set iterators

    [Theory, ModeData]
    public void Iterator_MapKeys_WithHelpers(ExecutionMode mode)
    {
        var source = @"
            const m = new Map<string, number>([['a', 1], ['b', 2], ['c', 3]]);
            const result = m.keys().map(k => k.toUpperCase()).toArray();
            console.log(result.length);
            console.log(result[0]);
            console.log(result[1]);
            console.log(result[2]);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\nA\nB\nC\n", output);
    }

    #endregion

    #region Iterator.from

    [Theory, ModeData]
    public void Iterator_From_Array(ExecutionMode mode)
    {
        var source = @"
            const iter = Iterator.from([1, 2, 3]);
            const result = iter.map(x => x * 2).toArray();
            console.log(result[0]);
            console.log(result[1]);
            console.log(result[2]);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n4\n6\n", output);
    }

    #endregion

    #region next() protocol

    [Theory, ModeData]
    public void Iterator_Next_ManualProtocol(ExecutionMode mode)
    {
        var source = @"
            const iter = [1, 2, 3].values();
            const r1 = iter.next();
            console.log(r1.value);
            console.log(r1.done);
            const r2 = iter.next();
            console.log(r2.value);
            console.log(r2.done);
            const r3 = iter.next();
            console.log(r3.value);
            console.log(r3.done);
            const r4 = iter.next();
            console.log(r4.done);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\nfalse\n2\nfalse\n3\nfalse\ntrue\n", output);
    }

    #endregion

    #region for...of on helper result

    [Theory, ModeData]
    public void Iterator_ForOf_OnMapResult(ExecutionMode mode)
    {
        var source = @"
            for (const x of [1, 2, 3].values().map(n => n + 10)) {
                console.log(x);
            }
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("11\n12\n13\n", output);
    }

    [Theory, ModeData]
    public void Iterator_ForOf_OnFilterResult(ExecutionMode mode)
    {
        var source = @"
            for (const x of [1, 2, 3, 4, 5].values().filter(n => n % 2 !== 0)) {
                console.log(x);
            }
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n3\n5\n", output);
    }

    #endregion

    #region drop + take combined

    [Theory, ModeData]
    public void Iterator_DropAndTake_SlicesBehavior(ExecutionMode mode)
    {
        var source = @"
            const result = [1, 2, 3, 4, 5, 6, 7].values().drop(2).take(3).toArray();
            console.log(result[0]);
            console.log(result[1]);
            console.log(result[2]);
            console.log(result.length);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n4\n5\n3\n", output);
    }

    #endregion
}

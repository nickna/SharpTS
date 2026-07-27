using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for custom iterator protocol support.
/// Verifies that objects with [Symbol.iterator] work in for...of loops
/// and objects with [Symbol.asyncIterator] work in for await...of loops.
/// </summary>
public class IteratorProtocolTests
{
    #region Sync Iterator Protocol (Symbol.iterator)

    [Theory, ModeData]
    public void CustomIterator_BasicObject_IteratesValues(ExecutionMode mode)
    {
        var source = """
            const iterable: any = {
                data: [10, 20, 30],
                [Symbol.iterator]() {
                    const iter: any = {
                        i: 0,
                        data: this.data,
                        next() {
                            if (this.i < this.data.length) {
                                const val = this.data[this.i];
                                this.i = this.i + 1;
                                return { value: val, done: false };
                            }
                            return { value: null, done: true };
                        }
                    };
                    return iter;
                }
            };

            let sum = 0;
            for (const x of iterable) {
                sum = sum + x;
            }
            console.log(sum);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("60\n", output);
    }

    [Theory, ModeData]
    public void CustomIterator_EmptyIterator_NoIterations(ExecutionMode mode)
    {
        var source = """
            const iterable: any = {
                [Symbol.iterator]() {
                    const iter: any = {
                        next() {
                            return { value: null, done: true };
                        }
                    };
                    return iter;
                }
            };

            let count = 0;
            for (const x of iterable) {
                count = count + 1;
            }
            console.log("count: " + count);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("count: 0\n", output);
    }

    [Theory, ModeData]
    public void CustomIterator_SingleValue_IteratesOnce(ExecutionMode mode)
    {
        var source = """
            const iterable: any = {
                [Symbol.iterator]() {
                    const iter: any = {
                        done: false,
                        next() {
                            if (!this.done) {
                                this.done = true;
                                return { value: 42, done: false };
                            }
                            return { value: null, done: true };
                        }
                    };
                    return iter;
                }
            };

            for (const x of iterable) {
                console.log(x);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory, ModeData]
    public void CustomIterator_WithBreak_StopsEarly(ExecutionMode mode)
    {
        var source = """
            const iterable: any = {
                [Symbol.iterator]() {
                    const iter: any = {
                        i: 0,
                        next() {
                            this.i = this.i + 1;
                            if (this.i <= 10) {
                                return { value: this.i, done: false };
                            }
                            return { value: null, done: true };
                        }
                    };
                    return iter;
                }
            };

            for (const x of iterable) {
                console.log(x);
                if (x >= 3) break;
            }
            console.log("done");
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n3\ndone\n", output);
    }

    [Theory, ModeData]
    public void CustomIterator_WithContinue_SkipsValues(ExecutionMode mode)
    {
        var source = """
            const iterable: any = {
                [Symbol.iterator]() {
                    const iter: any = {
                        i: 0,
                        next() {
                            this.i = this.i + 1;
                            if (this.i <= 5) {
                                return { value: this.i, done: false };
                            }
                            return { value: null, done: true };
                        }
                    };
                    return iter;
                }
            };

            for (const x of iterable) {
                if (x % 2 === 0) continue;
                console.log(x);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n3\n5\n", output);
    }

    [Theory, ModeData]
    public void CustomIterator_YieldingNull_Works(ExecutionMode mode)
    {
        var source = """
            const iterable: any = {
                [Symbol.iterator]() {
                    const iter: any = {
                        i: 0,
                        next() {
                            this.i = this.i + 1;
                            if (this.i === 1) return { value: null, done: false };
                            if (this.i === 2) return { value: 42, done: false };
                            if (this.i === 3) return { value: null, done: false };
                            return { value: null, done: true };
                        }
                    };
                    return iter;
                }
            };

            for (const x of iterable) {
                console.log(x);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("null\n42\nnull\n", output);
    }

    [Theory, ModeData]
    public void CustomIterator_MultipleIterations_WorksCorrectly(ExecutionMode mode)
    {
        var source = """
            const iterable: any = {
                [Symbol.iterator]() {
                    const iter: any = {
                        i: 0,
                        next() {
                            this.i = this.i + 1;
                            if (this.i <= 3) {
                                return { value: this.i, done: false };
                            }
                            return { value: null, done: true };
                        }
                    };
                    return iter;
                }
            };

            console.log("first:");
            for (const x of iterable) {
                console.log(x);
            }

            console.log("second:");
            for (const x of iterable) {
                console.log(x);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("first:\n1\n2\n3\nsecond:\n1\n2\n3\n", output);
    }

    #endregion

    #region Async Iterator Protocol (Symbol.asyncIterator)

    [Theory, ModeData]
    public void CustomAsyncIterator_BasicObject_IteratesValues(ExecutionMode mode)
    {
        var source = """
            const asyncIterable: any = {
                data: [100, 200, 300],
                [Symbol.asyncIterator]() {
                    const iter: any = {
                        i: 0,
                        data: this.data,
                        next() {
                            if (this.i < this.data.length) {
                                const val = this.data[this.i];
                                this.i = this.i + 1;
                                return Promise.resolve({ value: val, done: false });
                            }
                            return Promise.resolve({ value: null, done: true });
                        }
                    };
                    return iter;
                }
            };

            async function main() {
                let sum = 0;
                for await (const x of asyncIterable) {
                    sum = sum + x;
                }
                console.log(sum);
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("600\n", output);
    }

    [Theory, ModeData]
    public void CustomAsyncIterator_EmptyIterator_NoIterations(ExecutionMode mode)
    {
        var source = """
            const asyncIterable: any = {
                [Symbol.asyncIterator]() {
                    const iter: any = {
                        next() {
                            return Promise.resolve({ value: null, done: true });
                        }
                    };
                    return iter;
                }
            };

            async function main() {
                let count = 0;
                for await (const x of asyncIterable) {
                    count = count + 1;
                }
                console.log("count: " + count);
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("count: 0\n", output);
    }

    [Theory, ModeData]
    public void CustomAsyncIterator_WithBreak_StopsEarly(ExecutionMode mode)
    {
        var source = """
            const asyncIterable: any = {
                [Symbol.asyncIterator]() {
                    const iter: any = {
                        i: 0,
                        next() {
                            this.i = this.i + 1;
                            if (this.i <= 10) {
                                return Promise.resolve({ value: this.i, done: false });
                            }
                            return Promise.resolve({ value: null, done: true });
                        }
                    };
                    return iter;
                }
            };

            async function main() {
                for await (const x of asyncIterable) {
                    console.log(x);
                    if (x >= 3) break;
                }
                console.log("done");
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n3\ndone\n", output);
    }

    [Theory, ModeData]
    public void CustomAsyncIterator_WithContinue_SkipsValues(ExecutionMode mode)
    {
        var source = """
            const asyncIterable: any = {
                [Symbol.asyncIterator]() {
                    const iter: any = {
                        i: 0,
                        next() {
                            this.i = this.i + 1;
                            if (this.i <= 5) {
                                return Promise.resolve({ value: this.i, done: false });
                            }
                            return Promise.resolve({ value: null, done: true });
                        }
                    };
                    return iter;
                }
            };

            async function main() {
                for await (const x of asyncIterable) {
                    if (x % 2 === 0) continue;
                    console.log(x);
                }
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n3\n5\n", output);
    }

    #endregion

    #region Fallback to Built-in Iteration

    [Theory, ModeData]
    public void ArrayWithoutSymbolIterator_StillIterates(ExecutionMode mode)
    {
        var source = """
            const arr = [1, 2, 3];
            let sum = 0;
            for (const x of arr) {
                sum = sum + x;
            }
            console.log(sum);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("6\n", output);
    }

    [Theory, ModeData]
    public void PlainObject_UsesIndexBasedIteration(ExecutionMode mode)
    {
        var source = """
            const obj = { a: 1, b: 2, c: 3 };
            for (const key of Object.keys(obj)) {
                console.log(key);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("a\nb\nc\n", output);
    }

    #endregion

    #region Spread with Custom Iterator

    [Theory, ModeData]
    public void SpreadCustomIterator_InArrayLiteral_CollectsAllValues(ExecutionMode mode)
    {
        var source = """
            const iterable: any = {
                data: [10, 20, 30],
                [Symbol.iterator]() {
                    const iter: any = {
                        i: 0,
                        data: this.data,
                        next() {
                            if (this.i < this.data.length) {
                                const val = this.data[this.i];
                                this.i = this.i + 1;
                                return { value: val, done: false };
                            }
                            return { value: null, done: true };
                        }
                    };
                    return iter;
                }
            };

            const arr = [1, ...iterable, 100];
            console.log(arr.length);
            console.log(arr[0]);
            console.log(arr[1]);
            console.log(arr[2]);
            console.log(arr[3]);
            console.log(arr[4]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\n1\n10\n20\n30\n100\n", output);
    }

    [Theory, ModeData]
    public void SpreadEmptyIterator_InArrayLiteral_AddsNothing(ExecutionMode mode)
    {
        var source = """
            const empty: any = {
                [Symbol.iterator]() {
                    const iter: any = {
                        next() {
                            return { value: null, done: true };
                        }
                    };
                    return iter;
                }
            };

            const arr = [1, ...empty, 2];
            console.log(arr.length);
            console.log(arr[0]);
            console.log(arr[1]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n1\n2\n", output);
    }

    [Theory, ModeData]
    public void SpreadCustomIterator_InFunctionCall_ExpandsArguments(ExecutionMode mode)
    {
        var source = """
            function sum(...args: number[]): number {
                let total = 0;
                for (const x of args) {
                    total = total + x;
                }
                return total;
            }

            const iterable: any = {
                data: [10, 20, 30],
                [Symbol.iterator]() {
                    const iter: any = {
                        i: 0,
                        data: this.data,
                        next() {
                            if (this.i < this.data.length) {
                                const val = this.data[this.i];
                                this.i = this.i + 1;
                                return { value: val, done: false };
                            }
                            return { value: null, done: true };
                        }
                    };
                    return iter;
                }
            };

            console.log(sum(1, ...iterable, 100));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("161\n", output);
    }

    [Theory, ModeData]
    public void SpreadGenerator_InArrayLiteral_CollectsAllValues(ExecutionMode mode)
    {
        var source = """
            function* nums(): Generator<number> {
                yield 1;
                yield 2;
                yield 3;
            }

            const gen: any = nums();
            const arr = [...gen];
            console.log(arr.length);
            console.log(arr[0]);
            console.log(arr[1]);
            console.log(arr[2]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n1\n2\n3\n", output);
    }

    #endregion

    #region Yield* with Custom Iterator

    [Theory, ModeData]
    public void YieldStar_CustomIterator_DelegatesAllValues(ExecutionMode mode)
    {
        var source = """
            const iterable: any = {
                data: [10, 20, 30],
                [Symbol.iterator]() {
                    const iter: any = {
                        i: 0,
                        data: this.data,
                        next() {
                            if (this.i < this.data.length) {
                                const val = this.data[this.i];
                                this.i = this.i + 1;
                                return { value: val, done: false };
                            }
                            return { value: null, done: true };
                        }
                    };
                    return iter;
                }
            };

            function* gen(): Generator<number> {
                yield 1;
                yield* iterable;
                yield 100;
            }

            for (const x of gen()) {
                console.log(x);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n10\n20\n30\n100\n", output);
    }

    [Theory, ModeData]
    public void YieldStar_EmptyIterator_YieldsNothing(ExecutionMode mode)
    {
        var source = """
            const empty: any = {
                [Symbol.iterator]() {
                    const iter: any = {
                        next() {
                            return { value: null, done: true };
                        }
                    };
                    return iter;
                }
            };

            function* gen(): Generator<number> {
                yield 1;
                yield* empty;
                yield 2;
            }

            for (const x of gen()) {
                console.log(x);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n", output);
    }

    [Theory, ModeData]
    public void YieldStar_Generator_DelegatesAllValues(ExecutionMode mode)
    {
        var source = """
            function* inner(): Generator<number> {
                yield 10;
                yield 20;
                yield 30;
            }

            function* outer(): Generator<number> {
                yield 1;
                yield* inner();
                yield 100;
            }

            for (const x of outer()) {
                console.log(x);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n10\n20\n30\n100\n", output);
    }

    #endregion

    #region Generator Variable Capture

    [Theory, ModeData]
    public void Generator_CapturesOuterVariable_ReadsCorrectly(ExecutionMode mode)
    {
        var source = """
            const x = 42;
            function* gen(): Generator<number> {
                yield x;
            }

            for (const v of gen()) {
                console.log(v);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory, ModeData]
    public void Generator_CapturesOuterObject_AccessesProperty(ExecutionMode mode)
    {
        var source = """
            const obj: any = { value: 100 };
            function* gen(): Generator<number> {
                yield obj.value;
            }

            for (const v of gen()) {
                console.log(v);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("100\n", output);
    }

    [Theory, ModeData]
    public void Generator_CapturesMultipleVariables_AllAccessible(ExecutionMode mode)
    {
        var source = """
            const a = 10;
            const b = 20;
            const c = 30;
            function* gen(): Generator<number> {
                yield a;
                yield b;
                yield c;
            }

            let sum = 0;
            for (const v of gen()) {
                sum = sum + v;
            }
            console.log(sum);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("60\n", output);
    }

    [Theory, ModeData]
    public void Generator_CapturesVariable_UsedMultipleTimes(ExecutionMode mode)
    {
        var source = """
            const multiplier = 10;
            function* gen(): Generator<number> {
                yield 1 * multiplier;
                yield 2 * multiplier;
                yield 3 * multiplier;
            }

            for (const v of gen()) {
                console.log(v);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n20\n30\n", output);
    }

    [Theory, ModeData]
    public void Generator_CapturesArrayFromOuter_AccessesCorrectly(ExecutionMode mode)
    {
        var source = """
            const data = [5, 10, 15];
            function* gen(): Generator<number> {
                yield data.length;
                for (const item of data) {
                    yield item * 2;
                }
            }

            for (const v of gen()) {
                console.log(v);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n10\n20\n30\n", output);
    }

    [Theory, ModeData]
    public void Generator_CapturesAndUsesParameter_BothWork(ExecutionMode mode)
    {
        var source = """
            const outer = 100;
            function* gen(inner: number): Generator<number> {
                yield outer;
                yield inner;
                yield outer + inner;
            }

            for (const v of gen(5)) {
                console.log(v);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("100\n5\n105\n", output);
    }

    #endregion

    #region For...Of with Yield Inside Generators

    [Theory, ModeData]
    public void ForOfWithYield_BasicLoop_IteratesAllValues(ExecutionMode mode)
    {
        var source = """
            function* gen(): Generator<number> {
                for (const x of [1, 2, 3]) {
                    yield x * 2;
                }
            }

            for (const v of gen()) {
                console.log(v);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n4\n6\n", output);
    }

    [Theory, ModeData]
    public void ForOfWithYield_ParameterArray_IteratesAllValues(ExecutionMode mode)
    {
        var source = """
            function* gen(data: number[]): Generator<number> {
                for (const item of data) {
                    yield item * 2;
                }
            }

            for (const v of gen([5, 10, 15])) {
                console.log(v);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n20\n30\n", output);
    }

    [Theory, ModeData]
    public void ForOfWithYield_CapturedArray_IteratesAllValues(ExecutionMode mode)
    {
        var source = """
            const data = [5, 10, 15];
            function* gen(): Generator<number> {
                for (const item of data) {
                    yield item;
                }
            }

            for (const v of gen()) {
                console.log(v);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\n10\n15\n", output);
    }

    [Theory, ModeData]
    public void ForOfWithYield_NestedLoops_IteratesAllCombinations(ExecutionMode mode)
    {
        var source = """
            function* gen(): Generator<number> {
                for (const x of [1, 2]) {
                    for (const y of [10, 20]) {
                        yield x + y;
                    }
                }
            }

            for (const v of gen()) {
                console.log(v);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("11\n21\n12\n22\n", output);
    }

    [Theory, ModeData]
    public void ForOfWithYield_MultipleLoops_IteratesAllSequentially(ExecutionMode mode)
    {
        var source = """
            function* gen(): Generator<number> {
                for (const x of [1, 2]) {
                    yield x;
                }
                for (const y of [10, 20]) {
                    yield y;
                }
            }

            for (const v of gen()) {
                console.log(v);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n10\n20\n", output);
    }

    [Theory, ModeData]
    public void ForOfWithYield_WithBreak_StopsEarly(ExecutionMode mode)
    {
        var source = """
            function* gen(): Generator<number> {
                for (const x of [1, 2, 3, 4, 5]) {
                    yield x;
                    if (x >= 3) break;
                }
                yield 100;
            }

            for (const v of gen()) {
                console.log(v);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n3\n100\n", output);
    }

    [Theory, ModeData]
    public void ForOfWithYield_WithContinue_SkipsValues(ExecutionMode mode)
    {
        var source = """
            function* gen(): Generator<number> {
                for (const x of [1, 2, 3, 4, 5]) {
                    if (x % 2 === 0) continue;
                    yield x;
                }
            }

            for (const v of gen()) {
                console.log(v);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n3\n5\n", output);
    }

    #endregion

    #region Iterator-result fields read via Get (accessor getters)

    // Regression guard for the iterator-protocol fix in
    // Interpreter.EnumerateWithIteratorProtocol. ECMA-262 7.4.4/7.4.5
    // (IteratorValue/IteratorComplete) read the result's `value`/`done` through
    // Get(), which invokes accessor getters and walks the prototype chain. The
    // interpreter previously read them with a raw field accessor that skipped
    // `_getters`, so a result defining `value`/`done` as an accessor behaved
    // wrongly — and a *throwing* `value` getter (the shape in Test262's
    // Array/from/iter-get-iter-val-err and call/spread-err-*-itr-value, where
    // `done` is absent → falsy) made the loop spin forever (15s VM timeout),
    // leaking a CPU-pegged orphan thread. The bounded Test262 baseline guards
    // the throwing/non-terminating shapes (a unit-test reproduction of those
    // would hang the suite if the fix regressed); this terminating case pins
    // the same code path safely: a `value` accessor with a data-property `done`.
    // Interpreter-only — compiled mode emits its own iterator IL.
    [Fact]
    public void CustomIterator_ValueDefinedAsAccessor_InvokesGetter()
    {
        var source = """
            let i = 0;
            const iterable: any = {
                [Symbol.iterator]() {
                    return {
                        next() {
                            i = i + 1;
                            const r: any = { done: i > 3 };
                            Object.defineProperty(r, "value", { get() { return i * 10; } });
                            return r;
                        }
                    };
                }
            };
            const out: string[] = [];
            for (const x of iterable) { out.push(String(x)); }
            console.log(out.join(","));
            """;

        // Pre-fix: the `value` accessor was never invoked → "undefined,undefined,undefined".
        var output = TestHarness.Run(source, ExecutionMode.Interpreted);
        Assert.Equal("10,20,30\n", output);
    }

    // IteratorClose (ECMA-262 7.4.6): for-of abandoned early (break) must invoke
    // the iterator's return(). Uses an infinite iterator + break so it terminates
    // either way (pre-fix it terminated but never closed → close=0).
    // Interpreter-only — compiled mode emits its own iteration IL.
    [Fact]
    public void ForOf_BreakOverCustomIterator_CallsReturn()
    {
        var source = """
            let closed = 0;
            const iter: any = {
                [Symbol.iterator]() {
                    let i = 0;
                    return {
                        next() { i = i + 1; return { value: i, done: false }; },
                        return() { closed = closed + 1; return {}; }
                    };
                }
            };
            for (const x of iter) { if (x >= 2) break; }
            console.log("closed=" + closed);
            """;

        var output = TestHarness.Run(source, ExecutionMode.Interpreted);
        Assert.Equal("closed=1\n", output);
    }

    // Array.from(items, mapFn): mapfn is applied DURING iteration, and a throwing
    // mapfn triggers IteratorClose. Finite iterator so it terminates regardless of
    // the fix (pre-fix the throw still surfaced after materializing, but return()
    // was never called → close=0). Interpreter-only.
    [Fact]
    public void ArrayFrom_MapFnThrows_AppliedDuringIterationAndClosesIterator()
    {
        var source = """
            let closed = 0;
            let calls = 0;
            const iterable: any = {
                [Symbol.iterator]() {
                    let i = 0;
                    return {
                        next() { i = i + 1; return i <= 3 ? { value: i, done: false } : { value: undefined, done: true }; },
                        return() { closed = closed + 1; return {}; }
                    };
                }
            };
            let caught = "none";
            try {
                Array.from(iterable, (v: any) => { calls = calls + 1; if (v === 2) throw new Error("stop"); return v; });
            } catch (e: any) { caught = e.message; }
            console.log(caught + " calls=" + calls + " closed=" + closed);
            """;

        var output = TestHarness.Run(source, ExecutionMode.Interpreted);
        Assert.Equal("stop calls=2 closed=1\n", output);
    }

    #endregion
}

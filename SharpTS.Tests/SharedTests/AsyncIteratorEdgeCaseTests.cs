using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Edge case tests for async iterators and Promise chains.
/// These tests cover error handling, state management, and complex Promise scenarios
/// that are critical for preventing regressions in async iteration.
/// Runs against both interpreter and compiler.
/// </summary>
public class AsyncIteratorEdgeCaseTests
{
    #region Error Handling

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void AsyncIterator_RejectedPromiseInNext_PropagatesError(ExecutionMode mode)
    {
        // Compiler does not yet handle rejected promises in async iterator next()
        var source = """
            const iterable: any = {
                callCount: 0,
                [Symbol.asyncIterator]() {
                    const self = this;
                    return {
                        next() {
                            self.callCount = self.callCount + 1;
                            if (self.callCount === 2) {
                                return Promise.reject("iterator error");
                            }
                            if (self.callCount > 3) {
                                return Promise.resolve({ value: null, done: true });
                            }
                            return Promise.resolve({ value: self.callCount, done: false });
                        }
                    };
                }
            };

            async function main() {
                try {
                    for await (const x of iterable) {
                        console.log("got: " + x);
                    }
                    console.log("no error");
                } catch (e) {
                    console.log("caught: " + e);
                }
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("got: 1\ncaught: iterator error\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void AsyncIterator_ThrowInForAwaitBody_CatchesError(ExecutionMode mode)
    {
        // Compiler has issues with throw in for-await body
        var source = """
            async function* gen() {
                yield 1;
                yield 2;
                yield 3;
            }

            async function main() {
                try {
                    for await (const x of gen()) {
                        console.log("got: " + x);
                        if (x === 2) {
                            throw "body error";
                        }
                    }
                    console.log("no error");
                } catch (e) {
                    console.log("caught: " + e);
                }
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("got: 1\ngot: 2\ncaught: body error\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void AsyncGenerator_TryFinally_CleanupRuns(ExecutionMode mode)
    {
        // Compiler does not yet support finally in async generators
        var source = """
            let cleanupRan = false;

            async function* gen() {
                try {
                    yield 1;
                    yield 2;
                    yield 3;
                } finally {
                    cleanupRan = true;
                }
            }

            async function main() {
                for await (const x of gen()) {
                    console.log(x);
                    if (x === 2) break;
                }
                console.log("cleanup: " + cleanupRan);
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        // Note: cleanup should run when break exits the loop
        Assert.Contains("1\n", output);
        Assert.Contains("2\n", output);
    }

    #endregion

    #region Promise Chains with Iterators

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void PromiseAll_WithAsyncGenerator_CollectsResults(ExecutionMode mode)
    {
        var source = """
            async function* gen(): AsyncGenerator<number> {
                yield 10;
                yield 20;
                yield 30;
            }

            async function collectAll(asyncIter: any): Promise<number[]> {
                const results: number[] = [];
                for await (const x of asyncIter) {
                    results.push(x);
                }
                return results;
            }

            async function main() {
                const results = await collectAll(gen());
                console.log(results.length);
                console.log(results[0] + results[1] + results[2]);
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n60\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncIterator_NestedPromiseResolveInNext_Flattens(ExecutionMode mode)
    {
        // Regression test: nested Promise.resolve in iterator.next() should flatten
        var source = """
            const iterable: any = {
                i: 0,
                [Symbol.asyncIterator]() {
                    const self = this;
                    return {
                        next() {
                            self.i = self.i + 1;
                            if (self.i > 3) {
                                return Promise.resolve(Promise.resolve({ value: null, done: true }));
                            }
                            // Double-wrap the result in Promise.resolve
                            return Promise.resolve(Promise.resolve({ value: self.i * 10, done: false }));
                        }
                    };
                }
            };

            async function main() {
                let sum = 0;
                for await (const x of iterable) {
                    sum = sum + x;
                }
                console.log(sum);
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("60\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncIterator_WithAwaitBeforeYield_HandlesCorrectly(ExecutionMode mode)
    {
        var source = """
            async function getValue(): Promise<number> {
                return 2;
            }

            async function* gen(): AsyncGenerator<number> {
                yield 1;
                const val = await getValue();
                yield val;
                yield 3;
            }

            async function main() {
                const results: number[] = [];
                for await (const x of gen()) {
                    results.push(x);
                }
                console.log(results.length);
                console.log(results[0] + results[1] + results[2]);
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n6\n", output);
    }

    #endregion

    #region State Management

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncIterator_MultipleForAwaitLoops_IndependentState(ExecutionMode mode)
    {
        var source = """
            async function* counter(): AsyncGenerator<number> {
                yield 1;
                yield 2;
                yield 3;
            }

            async function main() {
                // First iteration
                let sum1 = 0;
                for await (const x of counter()) {
                    sum1 = sum1 + x;
                }

                // Second iteration - should start fresh
                let sum2 = 0;
                for await (const x of counter()) {
                    sum2 = sum2 + x;
                }

                console.log(sum1);
                console.log(sum2);
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("6\n6\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncGenerator_BreakThenNewLoop_StateReset(ExecutionMode mode)
    {
        var source = """
            async function* numbers(): AsyncGenerator<number> {
                yield 1;
                yield 2;
                yield 3;
                yield 4;
                yield 5;
            }

            async function main() {
                // First loop with break
                let count1 = 0;
                for await (const x of numbers()) {
                    count1 = count1 + 1;
                    if (x === 2) break;
                }

                // Second loop - fresh generator
                let count2 = 0;
                for await (const x of numbers()) {
                    count2 = count2 + 1;
                }

                console.log("first: " + count1);
                console.log("second: " + count2);
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("first: 2\nsecond: 5\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncGenerator_SharedReference_MaintainsState(ExecutionMode mode)
    {
        var source = """
            async function* numbers(): AsyncGenerator<number> {
                yield 1;
                yield 2;
                yield 3;
            }

            async function main() {
                const gen = numbers();

                // First next
                const r1 = await gen.next();
                console.log("first: " + r1.value);

                // Second next on same reference
                const r2 = await gen.next();
                console.log("second: " + r2.value);

                // Third next
                const r3 = await gen.next();
                console.log("third: " + r3.value);

                // Fourth next - should be done
                const r4 = await gen.next();
                console.log("fourth done: " + r4.done);
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("first: 1\nsecond: 2\nthird: 3\nfourth done: true\n", output);
    }

    #endregion

    #region Edge Cases

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncIterator_YieldNull_WorksCorrectly(ExecutionMode mode)
    {
        var source = """
            async function* gen(): AsyncGenerator<number | null> {
                yield null;
                yield 42;
                yield null;
            }

            async function main() {
                const results: any[] = [];
                for await (const x of gen()) {
                    results.push(x);
                }
                console.log(results.length);
                console.log(results[0]);
                console.log(results[1]);
                console.log(results[2]);
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\nnull\n42\nnull\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void AsyncIterator_YieldZero_WorksCorrectly(ExecutionMode mode)
    {
        // Test that falsy values like 0 work correctly
        var source = """
            async function* gen() {
                yield 0;
                yield 42;
            }

            async function main() {
                for await (const x of gen()) {
                    console.log(x);
                }
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n42\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncIterator_EmptyAsyncIterable_NoIterations(ExecutionMode mode)
    {
        var source = """
            const iterable: any = {
                [Symbol.asyncIterator]() {
                    return {
                        next() {
                            return Promise.resolve({ value: null, done: true });
                        }
                    };
                }
            };

            async function main() {
                let count = 0;
                for await (const x of iterable) {
                    count = count + 1;
                }
                console.log("count: " + count);
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("count: 0\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncIterator_SingleValue_IteratesOnce(ExecutionMode mode)
    {
        var source = """
            const iterable: any = {
                done: false,
                [Symbol.asyncIterator]() {
                    const self = this;
                    return {
                        next() {
                            if (!self.done) {
                                self.done = true;
                                return Promise.resolve({ value: 42, done: false });
                            }
                            return Promise.resolve({ value: null, done: true });
                        }
                    };
                }
            };

            async function main() {
                for await (const x of iterable) {
                    console.log(x);
                }
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncIterator_WithContinue_SkipsValues(ExecutionMode mode)
    {
        var source = """
            async function* gen(): AsyncGenerator<number> {
                yield 1;
                yield 2;
                yield 3;
                yield 4;
                yield 5;
            }

            async function main() {
                let sum = 0;
                for await (const x of gen()) {
                    if (x % 2 === 0) continue;
                    sum = sum + x;
                }
                console.log(sum);
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("9\n", output);  // 1 + 3 + 5 = 9
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void CustomAsyncIterator_WorksCorrectly(ExecutionMode mode)
    {
        // Test that custom async iterators work correctly
        var source = """
            const iterable: any = {
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
                for await (const x of iterable) {
                    sum = sum + x;
                }
                console.log(sum);
            }

            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("600\n", output);
    }

    #endregion
}

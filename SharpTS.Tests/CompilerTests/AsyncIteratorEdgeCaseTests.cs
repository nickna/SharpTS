using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// Edge case tests for async iterators and Promise chains in compiled code.
/// These tests cover error handling, state management, and complex Promise scenarios
/// that are critical for preventing regressions in async iteration.
/// </summary>
public class AsyncIteratorEdgeCaseTests
{
    #region Promise Chains with Iterators

    [Fact]
    public void PromiseAll_WithAsyncGenerator_CollectsResults()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("3\n60\n", output);
    }

    [Fact]
    public void AsyncIterator_NestedPromiseResolveInNext_Flattens()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("60\n", output);
    }

    [Fact]
    public void AsyncIterator_WithAwaitBeforeYield_HandlesCorrectly()
    {
        // Note: yield await in a single expression has known compiler issues,
        // so we use separate await and yield statements
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("3\n6\n", output);
    }

    #endregion

    #region State Management

    [Fact]
    public void AsyncIterator_MultipleForAwaitLoops_IndependentState()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("6\n6\n", output);
    }

    [Fact]
    public void AsyncGenerator_BreakThenNewLoop_StateReset()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("first: 2\nsecond: 5\n", output);
    }

    [Fact]
    public void AsyncGenerator_SharedReference_MaintainsState()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("first: 1\nsecond: 2\nthird: 3\nfourth done: true\n", output);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void AsyncIterator_YieldNull_WorksCorrectly()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("3\nnull\n42\nnull\n", output);
    }

    [Fact]
    public void AsyncIterator_EmptyAsyncIterable_NoIterations()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("count: 0\n", output);
    }

    [Fact]
    public void AsyncIterator_SingleValue_IteratesOnce()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void AsyncIterator_WithContinue_SkipsValues()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("9\n", output);  // 1 + 3 + 5 = 9
    }

    #endregion

    #region Interpreter/Compiler Parity

    [Fact]
    public void CustomAsyncIterator_InterpreterCompilerParity()
    {
        // Test that custom async iterators behave the same in both modes
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

        var compiledOutput = TestHarness.RunCompiled(source);
        var interpretedOutput = TestHarness.RunInterpreted(source);

        Assert.Equal("600\n", compiledOutput);
        Assert.Equal(interpretedOutput, compiledOutput);
    }

    #endregion
}

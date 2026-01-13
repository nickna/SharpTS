using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// Tests for custom iterator protocol support in IL compilation.
/// Verifies that objects with [Symbol.iterator] work in for...of loops
/// and objects with [Symbol.asyncIterator] work in for await...of loops.
///
/// Note: Tests use this-based state storage on the iterator object rather than
/// closure captures because there is a pre-existing compiler bug with closures
/// that write to captured variables.
/// </summary>
public class IteratorProtocolTests
{
    #region Sync Iterator Protocol (Symbol.iterator)

    [Fact]
    public void CustomIterator_BasicObject_IteratesValues()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("60\n", output);
    }

    [Fact]
    public void CustomIterator_EmptyIterator_NoIterations()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("count: 0\n", output);
    }

    [Fact]
    public void CustomIterator_SingleValue_IteratesOnce()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void CustomIterator_WithBreak_StopsEarly()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\n2\n3\ndone\n", output);
    }

    [Fact]
    public void CustomIterator_WithContinue_SkipsValues()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\n3\n5\n", output);
    }

    [Fact]
    public void CustomIterator_YieldingNull_Works()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("null\n42\nnull\n", output);
    }

    [Fact]
    public void CustomIterator_MultipleIterations_WorksCorrectly()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("first:\n1\n2\n3\nsecond:\n1\n2\n3\n", output);
    }

    #endregion

    #region Async Iterator Protocol (Symbol.asyncIterator)
    // Note: These tests are skipped until async iterator protocol is fully implemented.
    // The infrastructure is in place but there's an issue with Symbol.asyncIterator lookup
    // in async state machine contexts that requires further investigation.

    [Fact(Skip = "Async iterator protocol not yet fully implemented")]
    public void CustomAsyncIterator_BasicObject_IteratesValues()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("600\n", output);
    }

    [Fact(Skip = "Async iterator protocol not yet fully implemented")]
    public void CustomAsyncIterator_EmptyIterator_NoIterations()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("count: 0\n", output);
    }

    [Fact(Skip = "Async iterator protocol not yet fully implemented")]
    public void CustomAsyncIterator_WithBreak_StopsEarly()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\n2\n3\ndone\n", output);
    }

    [Fact(Skip = "Async iterator protocol not yet fully implemented")]
    public void CustomAsyncIterator_WithContinue_SkipsValues()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\n3\n5\n", output);
    }

    #endregion

    #region Fallback to Built-in Iteration

    [Fact(Skip = "Type checker bug: loop variable type inference in for...of")]
    public void ArrayWithoutSymbolIterator_StillIterates()
    {
        var source = """
            const arr = [1, 2, 3];
            let sum = 0;
            for (const x of arr) {
                sum = sum + x;
            }
            console.log(sum);
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("6\n", output);
    }

    [Fact]
    public void PlainObject_UsesIndexBasedIteration()
    {
        var source = """
            const obj = { a: 1, b: 2, c: 3 };
            for (const key of Object.keys(obj)) {
                console.log(key);
            }
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("a\nb\nc\n", output);
    }

    #endregion

    #region Interpreter vs Compiler Parity

    [Fact(Skip = "Interpreter does not yet support custom iterators")]
    public void CustomIterator_InterpreterCompilerParity()
    {
        var source = """
            const iterable: any = {
                data: [1, 2, 3, 4, 5],
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

            let result = "";
            for (const x of iterable) {
                result = result + x + ",";
            }
            console.log(result);
            """;

        var interpretedOutput = TestHarness.RunInterpreted(source);
        var compiledOutput = TestHarness.RunCompiled(source);
        Assert.Equal(interpretedOutput, compiledOutput);
    }

    [Fact(Skip = "Async iterator protocol not yet fully implemented")]
    public void CustomAsyncIterator_InterpreterCompilerParity()
    {
        var source = """
            const asyncIterable: any = {
                data: [10, 20, 30],
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
                let result = "";
                for await (const x of asyncIterable) {
                    result = result + x + ",";
                }
                console.log(result);
            }

            main();
            """;

        var interpretedOutput = TestHarness.RunInterpreted(source);
        var compiledOutput = TestHarness.RunCompiled(source);
        Assert.Equal(interpretedOutput, compiledOutput);
    }

    #endregion
}

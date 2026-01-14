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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    #region Spread with Custom Iterator

    [Fact]
    public void SpreadCustomIterator_InArrayLiteral_CollectsAllValues()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("5\n1\n10\n20\n30\n100\n", output);
    }

    [Fact]
    public void SpreadCustomIterator_InArrayLiteral_InterpreterParity()
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
            console.log(arr.join(","));
            """;

        var interpretedOutput = TestHarness.RunInterpreted(source);
        var compiledOutput = TestHarness.RunCompiled(source);
        Assert.Equal(interpretedOutput, compiledOutput);
    }

    [Fact]
    public void SpreadEmptyIterator_InArrayLiteral_AddsNothing()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("2\n1\n2\n", output);
    }

    [Fact]
    public void SpreadCustomIterator_InFunctionCall_ExpandsArguments()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("161\n", output);
    }

    [Fact]
    public void SpreadCustomIterator_InFunctionCall_InterpreterParity()
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

        var interpretedOutput = TestHarness.RunInterpreted(source);
        var compiledOutput = TestHarness.RunCompiled(source);
        Assert.Equal(interpretedOutput, compiledOutput);
    }

    [Fact]
    public void SpreadGenerator_InArrayLiteral_CollectsAllValues()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("3\n1\n2\n3\n", output);
    }

    [Fact]
    public void SpreadGenerator_InArrayLiteral_InterpreterParity()
    {
        var source = """
            function* nums(): Generator<number> {
                yield 1;
                yield 2;
                yield 3;
            }

            const gen: any = nums();
            const arr = [...gen];
            console.log(arr.join(","));
            """;

        var interpretedOutput = TestHarness.RunInterpreted(source);
        var compiledOutput = TestHarness.RunCompiled(source);
        Assert.Equal(interpretedOutput, compiledOutput);
    }

    #endregion

    #region Yield* with Custom Iterator

    [Fact]
    public void YieldStar_CustomIterator_DelegatesAllValues()
    {
        // Tests yield* with captured custom iterable (Symbol.iterator protocol)
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\n10\n20\n30\n100\n", output);
    }

    [Fact]
    public void YieldStar_CustomIterator_InterpreterParity()
    {
        // Tests yield* with captured custom iterable - interpreter vs compiled parity
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

            let result = "";
            for (const x of gen()) {
                result = result + x + ",";
            }
            console.log(result);
            """;

        var interpretedOutput = TestHarness.RunInterpreted(source);
        var compiledOutput = TestHarness.RunCompiled(source);
        Assert.Equal(interpretedOutput, compiledOutput);
    }

    [Fact]
    public void YieldStar_EmptyIterator_YieldsNothing()
    {
        // Tests yield* with captured empty custom iterator
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\n2\n", output);
    }

    [Fact]
    public void YieldStar_Generator_DelegatesAllValues()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\n10\n20\n30\n100\n", output);
    }

    #endregion

    #region Generator Variable Capture

    [Fact]
    public void Generator_CapturesOuterVariable_ReadsCorrectly()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void Generator_CapturesOuterObject_AccessesProperty()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("100\n", output);
    }

    [Fact]
    public void Generator_CapturesMultipleVariables_AllAccessible()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("60\n", output);
    }

    [Fact]
    public void Generator_CapturesVariable_UsedMultipleTimes()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("10\n20\n30\n", output);
    }

    [Fact]
    public void Generator_CapturesVariable_InterpreterParity()
    {
        var source = """
            const prefix = "value: ";
            function* gen(): Generator<string> {
                yield prefix + "one";
                yield prefix + "two";
            }

            for (const v of gen()) {
                console.log(v);
            }
            """;

        var interpretedOutput = TestHarness.RunInterpreted(source);
        var compiledOutput = TestHarness.RunCompiled(source);
        Assert.Equal(interpretedOutput, compiledOutput);
    }

    [Fact]
    public void Generator_CapturesArrayFromOuter_AccessesCorrectly()
    {
        // Test both index-based access and for...of iteration with captured array
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("3\n10\n20\n30\n", output);
    }

    [Fact]
    public void Generator_CapturesCustomIterable_YieldStarWorks()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\n10\n20\n30\n100\n", output);
    }

    [Fact]
    public void Generator_CapturesCustomIterable_InterpreterParity()
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

            let result = "";
            for (const x of gen()) {
                result = result + x + ",";
            }
            console.log(result);
            """;

        var interpretedOutput = TestHarness.RunInterpreted(source);
        var compiledOutput = TestHarness.RunCompiled(source);
        Assert.Equal(interpretedOutput, compiledOutput);
    }

    [Fact]
    public void Generator_CapturesAndUsesParameter_BothWork()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("100\n5\n105\n", output);
    }

    #endregion

    #region For...Of with Yield Inside Generators

    [Fact]
    public void ForOfWithYield_BasicLoop_IteratesAllValues()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("2\n4\n6\n", output);
    }

    [Fact]
    public void ForOfWithYield_ParameterArray_IteratesAllValues()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("10\n20\n30\n", output);
    }

    [Fact]
    public void ForOfWithYield_CapturedArray_IteratesAllValues()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("5\n10\n15\n", output);
    }

    [Fact]
    public void ForOfWithYield_NestedLoops_IteratesAllCombinations()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("11\n21\n12\n22\n", output);
    }

    [Fact]
    public void ForOfWithYield_MultipleLoops_IteratesAllSequentially()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\n2\n10\n20\n", output);
    }

    [Fact]
    public void ForOfWithYield_WithBreak_StopsEarly()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\n2\n3\n100\n", output);
    }

    [Fact]
    public void ForOfWithYield_WithContinue_SkipsValues()
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\n3\n5\n", output);
    }

    [Fact]
    public void ForOfWithYield_InterpreterParity()
    {
        var source = """
            function* gen(): Generator<number> {
                for (const x of [1, 2, 3]) {
                    yield x * 10;
                }
            }

            let result = "";
            for (const v of gen()) {
                result = result + v + ",";
            }
            console.log(result);
            """;

        var interpretedOutput = TestHarness.RunInterpreted(source);
        var compiledOutput = TestHarness.RunCompiled(source);
        Assert.Equal("10,20,30,\n", interpretedOutput);
        Assert.Equal(interpretedOutput, compiledOutput);
    }

    [Fact]
    public void ForOfWithYield_NestedLoops_InterpreterParity()
    {
        var source = """
            function* gen(): Generator<number> {
                for (const x of [1, 2]) {
                    for (const y of [10, 20]) {
                        yield x * y;
                    }
                }
            }

            let result = "";
            for (const v of gen()) {
                result = result + v + ",";
            }
            console.log(result);
            """;

        var interpretedOutput = TestHarness.RunInterpreted(source);
        var compiledOutput = TestHarness.RunCompiled(source);
        Assert.Equal("10,20,20,40,\n", interpretedOutput);
        Assert.Equal(interpretedOutput, compiledOutput);
    }

    #endregion
}

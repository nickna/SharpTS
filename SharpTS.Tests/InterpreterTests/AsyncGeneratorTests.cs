using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

/// <summary>
/// Tests for async generators (async function*) and async iteration (for await...of).
/// </summary>
public class AsyncGeneratorTests
{
    // Basic Async Generator Tests

    [Fact]
    public void AsyncGenerator_BasicYield_ReturnsValues()
    {
        var source = """
            async function* asyncCounter() {
                yield 1;
                yield 2;
                yield 3;
            }

            async function main() {
                const gen = asyncCounter();
                let result = await gen.next();
                console.log(result.value, result.done);
                result = await gen.next();
                console.log(result.value, result.done);
                result = await gen.next();
                console.log(result.value, result.done);
                result = await gen.next();
                console.log(result.value, result.done);
            }

            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1 false\n2 false\n3 false\nnull true\n", output);
    }

    [Fact]
    public void AsyncGenerator_EmptyGenerator_ReturnsDoneImmediately()
    {
        var source = """
            async function* empty() {}

            async function main() {
                const gen = empty();
                const result = await gen.next();
                console.log(result.done);
            }

            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void AsyncGenerator_WithAwait_AwaitsBeforeYield()
    {
        var source = """
            async function delay(value: number): Promise<number> {
                return value * 10;
            }

            async function* asyncGen() {
                const a = await delay(1);
                yield a;
                const b = await delay(2);
                yield b;
                const c = await delay(3);
                yield c;
            }

            async function main() {
                const gen = asyncGen();
                console.log((await gen.next()).value);
                console.log((await gen.next()).value);
                console.log((await gen.next()).value);
            }

            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("10\n20\n30\n", output);
    }

    [Fact]
    public void AsyncGenerator_WithParameters_UsesParameters()
    {
        var source = """
            async function* range(start: number, end: number) {
                for (let i = start; i <= end; i++) {
                    yield i;
                }
            }

            async function main() {
                const gen = range(5, 8);
                let result = await gen.next();
                while (!result.done) {
                    console.log(result.value);
                    result = await gen.next();
                }
            }

            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("5\n6\n7\n8\n", output);
    }

    // for await...of Tests

    [Fact]
    public void ForAwaitOf_AsyncGenerator_IteratesValues()
    {
        var source = """
            async function* asyncNumbers() {
                yield 1;
                yield 2;
                yield 3;
            }

            async function main() {
                for await (const num of asyncNumbers()) {
                    console.log(num);
                }
            }

            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n2\n3\n", output);
    }

    [Fact]
    public void ForAwaitOf_AsyncGeneratorWithAwait_IteratesAwaitedValues()
    {
        var source = """
            async function delay(value: number): Promise<number> {
                return value * 2;
            }

            async function* asyncGen() {
                yield await delay(5);
                yield await delay(10);
                yield await delay(15);
            }

            async function main() {
                for await (const val of asyncGen()) {
                    console.log(val);
                }
            }

            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("10\n20\n30\n", output);
    }

    // Note: Infinite generators are not supported due to eager evaluation.
    // The generator body executes fully before yielding any values.
    // This test uses a finite generator with break.
    [Fact]
    public void ForAwaitOf_WithBreak_StopsIteration()
    {
        var source = """
            async function* numbers() {
                yield 0;
                yield 1;
                yield 2;
                yield 3;
                yield 4;
                yield 5;
            }

            async function main() {
                for await (const num of numbers()) {
                    console.log(num);
                    if (num >= 3) break;
                }
                console.log("done");
            }

            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("0\n1\n2\n3\ndone\n", output);
    }

    [Fact]
    public void ForAwaitOf_WithContinue_SkipsIteration()
    {
        var source = """
            async function* numbers() {
                yield 1;
                yield 2;
                yield 3;
                yield 4;
                yield 5;
            }

            async function main() {
                for await (const num of numbers()) {
                    if (num % 2 === 0) continue;
                    console.log(num);
                }
            }

            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n3\n5\n", output);
    }

    // Async Generator .return() and .throw() Tests

    [Fact]
    public void AsyncGenerator_Return_ClosesGenerator()
    {
        var source = """
            async function* asyncGen() {
                yield 1;
                yield 2;
                yield 3;
            }

            async function main() {
                const gen = asyncGen();
                console.log((await gen.next()).value);
                const returnResult = await gen.return(42);
                console.log(returnResult.value, returnResult.done);
                const nextResult = await gen.next();
                console.log(nextResult.done);
            }

            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n42 true\ntrue\n", output);
    }

    [Fact]
    public void AsyncGenerator_Throw_ThrowsError()
    {
        var source = """
            async function* asyncGen() {
                yield 1;
                yield 2;
            }

            async function main() {
                const gen = asyncGen();
                console.log((await gen.next()).value);
                try {
                    await gen.throw("Test error");
                } catch (e) {
                    console.log("Caught:", e);
                }
            }

            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\nCaught: Test error\n", output);
    }

    // Async Iterator Protocol Tests

    [Fact]
    public void AsyncIterator_CustomAsyncIterable_WorksWithForAwaitOf()
    {
        var source = """
            const asyncIterable = {
                [Symbol.asyncIterator]() {
                    let count = 0;
                    return {
                        next: async () => {
                            if (count < 3) {
                                return { value: count++, done: false };
                            }
                            return { value: null, done: true };
                        }
                    };
                }
            };

            async function main() {
                for await (const val of asyncIterable) {
                    console.log(val);
                }
            }

            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("0\n1\n2\n", output);
    }

    [Fact]
    public void AsyncIterator_SymbolAsyncIterator_ReturnsIterator()
    {
        var source = """
            const obj = {
                data: [10, 20, 30],
                [Symbol.asyncIterator]() {
                    let index = 0;
                    const data = this.data;
                    return {
                        next: async () => {
                            if (index < data.length) {
                                return { value: data[index++], done: false };
                            }
                            return { value: null, done: true };
                        }
                    };
                }
            };

            async function main() {
                for await (const item of obj) {
                    console.log(item);
                }
            }

            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("10\n20\n30\n", output);
    }

    // yield* in Async Generators

    [Fact]
    public void AsyncGenerator_YieldStar_DelegatesToSyncIterable()
    {
        var source = """
            async function* asyncGen() {
                yield* [1, 2, 3];
            }

            async function main() {
                for await (const val of asyncGen()) {
                    console.log(val);
                }
            }

            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n2\n3\n", output);
    }

    [Fact]
    public void AsyncGenerator_YieldStar_DelegatesToAsyncGenerator()
    {
        var source = """
            async function* inner() {
                yield "a";
                yield "b";
            }

            async function* outer() {
                yield "start";
                yield* inner();
                yield "end";
            }

            async function main() {
                for await (const val of outer()) {
                    console.log(val);
                }
            }

            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("start\na\nb\nend\n", output);
    }

    // Edge Cases

    [Fact]
    public void AsyncGenerator_InNamespace_Works()
    {
        var source = """
            namespace Utils {
                export async function* counter(max: number) {
                    for (let i = 1; i <= max; i++) {
                        yield i;
                    }
                }
            }

            async function main() {
                for await (const n of Utils.counter(3)) {
                    console.log(n);
                }
            }

            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n2\n3\n", output);
    }

    [Fact]
    public void AsyncGenerator_ReturnsValue_IncludedInFinalResult()
    {
        var source = """
            async function* genWithReturn() {
                yield 1;
                yield 2;
                return "final";
            }

            async function main() {
                const gen = genWithReturn();
                let result = await gen.next();
                console.log(result.value, result.done);
                result = await gen.next();
                console.log(result.value, result.done);
                result = await gen.next();
                console.log(result.value, result.done);
            }

            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1 false\n2 false\nfinal true\n", output);
    }

    [Fact]
    public void ForAwaitOf_EmptyAsyncGenerator_NoIterations()
    {
        var source = """
            async function* empty() {}

            async function main() {
                let count = 0;
                for await (const _ of empty()) {
                    count++;
                }
                console.log("iterations:", count);
            }

            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("iterations: 0\n", output);
    }
}

using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// Tests for async generator IL compilation (async function*) and async iteration (for await...of).
/// Verifies parity between interpreter and compiler for async generator features.
/// Note: Tests use single-arg console.log or string concatenation due to a multi-arg
/// console.log bug in compiled async functions.
/// </summary>
public class AsyncGeneratorCompilerTests
{
    #region Basic Async Generator Tests

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
                console.log(result.value + " " + result.done);
                result = await gen.next();
                console.log(result.value + " " + result.done);
                result = await gen.next();
                console.log(result.value + " " + result.done);
                result = await gen.next();
                console.log(result.value + " " + result.done);
            }

            main();
            """;

        var output = TestHarness.RunCompiled(source);
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

        var output = TestHarness.RunCompiled(source);
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
                for await (const val of asyncGen()) {
                    console.log(val);
                }
            }

            main();
            """;

        var output = TestHarness.RunCompiled(source);
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
                for await (const n of range(5, 8)) {
                    console.log(n);
                }
            }

            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("5\n6\n7\n8\n", output);
    }

    [Fact]
    public void AsyncGenerator_SingleYield_Works()
    {
        var source = """
            async function* single() {
                yield 42;
            }

            async function main() {
                const gen = single();
                const r1 = await gen.next();
                console.log(r1.value + " " + r1.done);
                const r2 = await gen.next();
                console.log(r2.value + " " + r2.done);
            }

            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42 false\nnull true\n", output);
    }

    [Fact]
    public void AsyncGenerator_YieldInLoop_Works()
    {
        var source = """
            async function* countdown(start: number) {
                while (start > 0) {
                    yield start;
                    start = start - 1;
                }
            }

            async function main() {
                for await (const n of countdown(3)) {
                    console.log(n);
                }
            }

            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("3\n2\n1\n", output);
    }

    #endregion

    #region for await...of Tests

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

        var output = TestHarness.RunCompiled(source);
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("10\n20\n30\n", output);
    }

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

        var output = TestHarness.RunCompiled(source);
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\n3\n5\n", output);
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
                console.log("iterations: " + count);
            }

            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("iterations: 0\n", output);
    }

    [Fact]
    public void ForAwaitOf_MultipleLoops_Works()
    {
        var source = """
            async function* gen() {
                yield 1;
                yield 2;
            }

            async function main() {
                for await (const x of gen()) {
                    console.log("first: " + x);
                }
                for await (const y of gen()) {
                    console.log("second: " + y);
                }
            }

            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("first: 1\nfirst: 2\nsecond: 1\nsecond: 2\n", output);
    }

    #endregion

    #region Async Generator .return() and .throw() Tests

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
                const r1 = await gen.next();
                console.log(r1.value);
                const returnResult = await gen.return(42);
                console.log(returnResult.value + " " + returnResult.done);
                const nextResult = await gen.next();
                console.log(nextResult.done);
            }

            main();
            """;

        var output = TestHarness.RunCompiled(source);
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
                const r1 = await gen.next();
                console.log(r1.value);
                try {
                    await gen.throw("Test error");
                } catch (e) {
                    console.log("Caught: " + e);
                }
            }

            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\nCaught: Test error\n", output);
    }

    [Fact]
    public void AsyncGenerator_ReturnWithoutValue_ReturnsNull()
    {
        var source = """
            async function* gen() {
                yield 1;
                yield 2;
            }

            async function main() {
                const g = gen();
                await g.next();
                const r = await g.return();
                console.log(r.value + " " + r.done);
            }

            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("null true\n", output);
    }

    #endregion

    #region yield* Delegation Tests

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

        var output = TestHarness.RunCompiled(source);
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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("start\na\nb\nend\n", output);
    }

    [Fact]
    public void AsyncGenerator_YieldStar_EmptyIterable()
    {
        var source = """
            async function* gen() {
                yield 1;
                yield* [];
                yield 2;
            }

            async function main() {
                for await (const val of gen()) {
                    console.log(val);
                }
            }

            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\n2\n", output);
    }

    [Fact]
    public void AsyncGenerator_YieldStar_NestedDelegation()
    {
        var source = """
            async function* level1() {
                yield 1;
            }

            async function* level2() {
                yield* level1();
                yield 2;
            }

            async function* level3() {
                yield* level2();
                yield 3;
            }

            async function main() {
                for await (const val of level3()) {
                    console.log(val);
                }
            }

            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\n2\n3\n", output);
    }

    #endregion

    #region Return Value Tests

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
                console.log(result.value + " " + result.done);
                result = await gen.next();
                console.log(result.value + " " + result.done);
                result = await gen.next();
                console.log(result.value + " " + result.done);
            }

            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1 false\n2 false\nfinal true\n", output);
    }

    [Fact]
    public void AsyncGenerator_ImplicitReturn_ReturnsNull()
    {
        var source = """
            async function* gen() {
                yield 1;
            }

            async function main() {
                const g = gen();
                await g.next();
                const r = await g.next();
                console.log(r.value + " " + r.done);
            }

            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("null true\n", output);
    }

    [Fact]
    public void AsyncGenerator_EarlyReturn_SkipsRemainingYields()
    {
        var source = """
            async function* gen(earlyReturn: boolean) {
                yield 1;
                if (earlyReturn) return "early";
                yield 2;
                return "normal";
            }

            async function main() {
                const g = gen(true);
                let r = await g.next();
                console.log(r.value + " " + r.done);
                r = await g.next();
                console.log(r.value + " " + r.done);
            }

            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1 false\nearly true\n", output);
    }

    #endregion

    #region Edge Cases

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

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\n2\n3\n", output);
    }

    [Fact]
    public void AsyncGenerator_WithStringYields_Works()
    {
        var source = """
            async function* greetings() {
                yield "hello";
                yield "world";
            }

            async function main() {
                for await (const s of greetings()) {
                    console.log(s);
                }
            }

            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("hello\nworld\n", output);
    }

    [Fact]
    public void AsyncGenerator_YieldingObjects_Works()
    {
        var source = """
            async function* objects() {
                yield { name: "Alice", age: 30 };
                yield { name: "Bob", age: 25 };
            }

            async function main() {
                for await (const obj of objects()) {
                    console.log(obj.name + " " + obj.age);
                }
            }

            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Alice 30\nBob 25\n", output);
    }

    [Fact]
    public void AsyncGenerator_YieldingArrays_Works()
    {
        var source = """
            async function* arrays() {
                yield [1, 2, 3];
                yield [4, 5, 6];
            }

            async function main() {
                for await (const arr of arrays()) {
                    console.log(arr.join(","));
                }
            }

            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1,2,3\n4,5,6\n", output);
    }

    [Fact]
    public void AsyncGenerator_ConditionalYield_Works()
    {
        var source = """
            async function* conditional(includeMiddle: boolean) {
                yield 1;
                if (includeMiddle) {
                    yield 2;
                }
                yield 3;
            }

            async function main() {
                console.log("With middle:");
                for await (const n of conditional(true)) {
                    console.log(n);
                }
                console.log("Without middle:");
                for await (const n of conditional(false)) {
                    console.log(n);
                }
            }

            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("With middle:\n1\n2\n3\nWithout middle:\n1\n3\n", output);
    }

    [Fact]
    public void AsyncGenerator_YieldNull_Works()
    {
        var source = """
            async function* gen() {
                yield null;
                yield 1;
                yield null;
            }

            async function main() {
                for await (const val of gen()) {
                    console.log(val);
                }
            }

            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("null\n1\nnull\n", output);
    }

    [Fact]
    public void AsyncGenerator_YieldBoolean_Works()
    {
        var source = """
            async function* gen() {
                yield true;
                yield false;
            }

            async function main() {
                for await (const val of gen()) {
                    console.log(val);
                }
            }

            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\nfalse\n", output);  // JavaScript uses lowercase booleans
    }

    [Fact]
    public void AsyncGenerator_AwaitInYieldExpression_Works()
    {
        var source = """
            async function getValue(): Promise<number> {
                return 42;
            }

            async function* gen() {
                yield await getValue();
            }

            async function main() {
                for await (const val of gen()) {
                    console.log(val);
                }
            }

            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void AsyncGenerator_MultipleAwaitsBetweenYields_Works()
    {
        var source = """
            async function add(a: number, b: number): Promise<number> {
                return a + b;
            }

            async function* gen() {
                const x = await add(1, 2);
                const y = await add(x, 3);
                yield y;
                const z = await add(y, 4);
                yield z;
            }

            async function main() {
                for await (const val of gen()) {
                    console.log(val);
                }
            }

            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("6\n10\n", output);
    }

    #endregion
}

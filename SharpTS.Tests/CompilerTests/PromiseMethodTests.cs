using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// Tests for Promise methods compilation.
/// </summary>
public class PromiseMethodTests
{
    #region Promise.resolve() Tests

    [Fact]
    public void Resolve_WrapsValue()
    {
        var source = """
            async function main(): Promise<void> {
                let result = await Promise.resolve(42);
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void Resolve_NoArgs()
    {
        var source = """
            async function main(): Promise<void> {
                let p = Promise.resolve();
                let result = await p;
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("null\n", output);
    }

    /// <summary>
    /// Regression test: Nested Promise.resolve must flatten correctly.
    /// Previously, this caused double-wrapping: Promise(Task(Promise(Task(value))))
    /// which led to infinite loops in async iterators.
    /// </summary>
    [Fact]
    public void Resolve_NestedPromiseResolve_Flattens()
    {
        var source = """
            async function main(): Promise<void> {
                let innerPromise = Promise.resolve(42);
                let outerPromise = Promise.resolve(innerPromise);
                let result = await outerPromise;
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42\n", output);
    }

    /// <summary>
    /// Regression test: Triple-nested Promise.resolve must flatten correctly.
    /// </summary>
    [Fact]
    public void Resolve_TripleNestedPromise_Flattens()
    {
        var source = """
            async function main(): Promise<void> {
                let p1 = Promise.resolve(100);
                let p2 = Promise.resolve(p1);
                let p3 = Promise.resolve(p2);
                let result = await p3;
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("100\n", output);
    }

    /// <summary>
    /// Regression test: Promise.resolve with object containing done:true.
    /// This is the exact pattern that caused infinite loops in async iterators
    /// when the iterator result object was double-wrapped.
    /// </summary>
    [Fact]
    public void Resolve_IteratorResultObject_NotDoubleWrapped()
    {
        var source = """
            async function main(): Promise<void> {
                let iterResult = { value: 42, done: true };
                let p = Promise.resolve(iterResult);
                let result = await p;
                console.log(result.value);
                console.log(result.done);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42\ntrue\n", output);
    }

    /// <summary>
    /// Regression test: Async function returning Promise.resolve should not double-wrap.
    /// </summary>
    [Fact]
    public void Resolve_FromAsyncFunction_NotDoubleWrapped()
    {
        var source = """
            async function getValue(): Promise<number> {
                return await Promise.resolve(99);
            }
            async function main(): Promise<void> {
                let result = await getValue();
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("99\n", output);
    }

    #endregion

    #region Promise.reject() Tests

    [Fact]
    public void Reject_CreatesRejectedPromise()
    {
        // Note: try/catch inside async functions is not yet supported in compiled mode
        // Test that Promise.reject creates a promise (but don't await it)
        var source = """
            async function main(): Promise<void> {
                let p = Promise.reject("error message");
                console.log("created");
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("created\n", output);
    }

    #endregion

    #region Promise.all() Tests

    [Fact]
    public void All_ResolvesAllPromises()
    {
        var source = """
            async function a(): Promise<number> { return 1; }
            async function b(): Promise<number> { return 2; }
            async function c(): Promise<number> { return 3; }
            async function main(): Promise<void> {
                let results = await Promise.all([a(), b(), c()]);
                console.log(results[0]);
                console.log(results[1]);
                console.log(results[2]);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\n2\n3\n", output);
    }

    [Fact]
    public void All_EmptyArray()
    {
        var source = """
            async function main(): Promise<void> {
                let results = await Promise.all([]);
                console.log(results.length);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("0\n", output);
    }

    #endregion

    #region Promise.race() Tests

    [Fact]
    public void Race_FirstResolvedWins()
    {
        var source = """
            async function main(): Promise<void> {
                let p1 = Promise.resolve("first");
                let p2 = Promise.resolve("second");
                let result = await Promise.race([p1, p2]);
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("first\n", output);
    }

    #endregion

    #region Promise.allSettled() Tests

    [Fact]
    public void AllSettled_AllResolve()
    {
        var source = """
            async function a(): Promise<number> { return 1; }
            async function b(): Promise<number> { return 2; }
            async function main(): Promise<void> {
                let results = await Promise.allSettled([a(), b()]);
                console.log(results[0].status);
                console.log(results[0].value);
                console.log(results[1].status);
                console.log(results[1].value);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("fulfilled\n1\nfulfilled\n2\n", output);
    }

    [Fact]
    public void AllSettled_EmptyArray()
    {
        var source = """
            async function main(): Promise<void> {
                let results = await Promise.allSettled([]);
                console.log(results.length);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("0\n", output);
    }

    [Fact]
    public void AllSettled_NonTaskValues()
    {
        // Test that non-Promise values are handled as immediately fulfilled
        var source = """
            async function main(): Promise<void> {
                let results = await Promise.allSettled([42, "hello"]);
                console.log(results[0].status);
                console.log(results[0].value);
                console.log(results[1].status);
                console.log(results[1].value);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("fulfilled\n42\nfulfilled\nhello\n", output);
    }

    #endregion

    #region Promise.any() Tests

    [Fact]
    public void Any_FirstResolves()
    {
        var source = """
            async function main(): Promise<void> {
                let p1 = Promise.resolve("first");
                let p2 = Promise.resolve("second");
                let result = await Promise.any([p1, p2]);
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("first\n", output);
    }

    #endregion

    #region Promise.then() Tests

    [Fact]
    public void Then_BasicChaining()
    {
        var source = """
            async function getValue(): Promise<number> {
                return 10;
            }
            async function main(): Promise<void> {
                let p = getValue();
                let result = await p.then((x: number): number => x * 2);
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("20\n", output);
    }

    [Fact]
    public void Then_MultipleChains()
    {
        var source = """
            async function getValue(): Promise<number> {
                return 5;
            }
            async function main(): Promise<void> {
                let result = await getValue()
                    .then((x: number): number => x + 1)
                    .then((x: number): number => x * 2)
                    .then((x: number): number => x + 3);
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("15\n", output);
    }

    [Fact]
    public void Then_PassesValueThrough()
    {
        var source = """
            async function main(): Promise<void> {
                let result = await Promise.resolve(42).then((x: number): number => x);
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42\n", output);
    }

    #endregion

    #region Promise.catch() Tests

    [Fact]
    public void Catch_PassesThroughResolved()
    {
        var source = """
            async function main(): Promise<void> {
                let p = Promise.resolve(42);
                let result = await p.catch((err: string): number => -1);
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42\n", output);
    }

    #endregion

    #region Promise.finally() Tests

    [Fact]
    public void Finally_RunsOnResolved()
    {
        // Note: Avoids capturing variable in zero-param arrow due to display class IL bug
        var source = """
            async function main(): Promise<void> {
                let result = await Promise.resolve(42).finally((): void => {
                    console.log("finally ran");
                });
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("finally ran\n42\n", output);
    }

    [Fact]
    public void Finally_DoesNotAlterValue()
    {
        var source = """
            async function main(): Promise<void> {
                let result = await Promise.resolve(42).finally((): number => {
                    return 999;
                });
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42\n", output);
    }

    #endregion

    #region Promise Executor Constructor Tests

    [Fact]
    public void ExecutorConstructor_ImmediateResolve()
    {
        var source = """
            async function main(): Promise<void> {
                let p = new Promise<number>((resolve, reject) => {
                    resolve(42);
                });
                let result = await p;
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void ExecutorConstructor_ImmediateReject()
    {
        var source = """
            async function main(): Promise<void> {
                let p = new Promise<number>((resolve, reject) => {
                    reject("something went wrong");
                });
                try {
                    await p;
                    console.log("should not reach");
                } catch (e) {
                    console.log("caught");
                }
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("caught\n", output);
    }

    [Fact]
    public void ExecutorConstructor_ResolveWithObject()
    {
        var source = """
            async function main(): Promise<void> {
                let p = new Promise<{name: string}>((resolve, reject) => {
                    resolve({ name: "test" });
                });
                let result = await p;
                console.log(result.name);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("test\n", output);
    }

    [Fact]
    public void ExecutorConstructor_ResolveWithUndefined()
    {
        var source = """
            async function main(): Promise<void> {
                let p = new Promise<void>((resolve, reject) => {
                    resolve();
                });
                let result = await p;
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("null\n", output);
    }

    [Fact]
    public void ExecutorConstructor_ExecutorThrows()
    {
        var source = """
            async function main(): Promise<void> {
                let p = new Promise<number>((resolve, reject) => {
                    throw new Error("executor error");
                });
                try {
                    await p;
                    console.log("should not reach");
                } catch (e) {
                    console.log("caught executor error");
                }
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("caught executor error\n", output);
    }

    [Fact]
    public void ExecutorConstructor_OnlyFirstSettlementCounts()
    {
        var source = """
            async function main(): Promise<void> {
                let p = new Promise<number>((resolve, reject) => {
                    resolve(1);
                    resolve(2);
                    reject("error");
                });
                let result = await p;
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("1\n", output);
    }

    [Fact]
    public void ExecutorConstructor_ChainWithThen()
    {
        var source = """
            async function main(): Promise<void> {
                let p = new Promise<number>((resolve, reject) => {
                    resolve(10);
                });
                let result = await p.then((x: number): number => x * 2);
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("20\n", output);
    }

    [Fact]
    public void ExecutorConstructor_ChainWithCatch()
    {
        var source = """
            async function main(): Promise<void> {
                let p = new Promise<number>((resolve, reject) => {
                    reject("error");
                });
                let result = await p.catch((e: string): number => 99);
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("99\n", output);
    }

    [Fact]
    public void ExecutorConstructor_UseInPromiseAll()
    {
        var source = """
            async function main(): Promise<void> {
                let p1 = new Promise<number>((resolve, reject) => resolve(1));
                let p2 = new Promise<number>((resolve, reject) => resolve(2));
                let p3 = new Promise<number>((resolve, reject) => resolve(3));
                let results = await Promise.all([p1, p2, p3]);
                console.log(results[0] + results[1] + results[2]);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("6\n", output);
    }

    [Fact]
    public void ExecutorConstructor_UseInPromiseRace()
    {
        var source = """
            async function main(): Promise<void> {
                let p1 = new Promise<string>((resolve, reject) => resolve("first"));
                let p2 = new Promise<string>((resolve, reject) => resolve("second"));
                let result = await Promise.race([p1, p2]);
                console.log(result);
            }
            main();
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("first\n", output);
    }

    #endregion
}

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
}

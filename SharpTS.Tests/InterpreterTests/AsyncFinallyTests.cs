using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

/// <summary>
/// Tests for finally blocks with awaits in async functions.
/// </summary>
public class AsyncFinallyTests
{
    [Fact]
    public void Finally_WithAwait_BasicCase()
    {
        var source = """
            async function cleanup(): Promise<void> {
                console.log("cleanup called");
            }
            async function main(): Promise<void> {
                try {
                    console.log("in try");
                } finally {
                    await cleanup();
                }
                console.log("after try/finally");
            }
            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("in try\ncleanup called\nafter try/finally\n", output);
    }

    [Fact]
    public void Finally_WithAwait_ExceptionInTry_WithCatch()
    {
        var source = """
            async function cleanup(): Promise<void> {
                console.log("cleanup called");
            }
            async function main(): Promise<void> {
                try {
                    console.log("in try");
                    throw "error from try";
                } catch (e) {
                    console.log("in catch: " + e);
                } finally {
                    await cleanup();
                }
                console.log("after try/catch/finally");
            }
            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("in try\nin catch: error from try\ncleanup called\nafter try/catch/finally\n", output);
    }

    [Fact]
    public void Finally_WithAwait_ExceptionInTry_NoCatch_Rethrows()
    {
        var source = """
            async function cleanup(): Promise<void> {
                console.log("cleanup called");
            }
            async function testRethrow(): Promise<void> {
                try {
                    console.log("in try");
                    throw "error from try";
                } finally {
                    await cleanup();
                }
                console.log("should not print");
            }
            async function main(): Promise<void> {
                try {
                    await testRethrow();
                } catch (e) {
                    console.log("outer caught: " + e);
                }
                console.log("done");
            }
            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("in try\ncleanup called\nouter caught: error from try\ndone\n", output);
    }

    [Fact]
    public void Finally_WithAwait_AwaitInBothTryAndFinally()
    {
        var source = """
            async function getValue(): Promise<number> {
                return 42;
            }
            async function cleanup(): Promise<void> {
                console.log("cleanup called");
            }
            async function main(): Promise<void> {
                try {
                    let v = await getValue();
                    console.log("got: " + v);
                } finally {
                    await cleanup();
                }
                console.log("done");
            }
            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("got: 42\ncleanup called\ndone\n", output);
    }

    [Fact]
    public void Finally_WithAwait_ExceptionInAwaitedCall_Rethrows()
    {
        var source = """
            async function throwingFunc(): Promise<number> {
                throw "async error";
            }
            async function cleanup(): Promise<void> {
                console.log("cleanup called");
            }
            async function testAsyncError(): Promise<void> {
                try {
                    let v = await throwingFunc();
                    console.log("should not print: " + v);
                } finally {
                    await cleanup();
                }
                console.log("should not print");
            }
            async function main(): Promise<void> {
                try {
                    await testAsyncError();
                } catch (e) {
                    console.log("caught: " + e);
                }
                console.log("done");
            }
            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("cleanup called\ncaught: async error\ndone\n", output);
    }

    [Fact]
    public void Finally_WithAwait_MultipleAwaitsInFinally()
    {
        var source = """
            async function step1(): Promise<void> {
                console.log("step1");
            }
            async function step2(): Promise<void> {
                console.log("step2");
            }
            async function main(): Promise<void> {
                try {
                    console.log("in try");
                } finally {
                    await step1();
                    await step2();
                }
                console.log("done");
            }
            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("in try\nstep1\nstep2\ndone\n", output);
    }

    [Fact]
    public void Finally_WithAwait_NestedTryFinally()
    {
        var source = """
            async function cleanup1(): Promise<void> {
                console.log("cleanup1");
            }
            async function cleanup2(): Promise<void> {
                console.log("cleanup2");
            }
            async function main(): Promise<void> {
                try {
                    console.log("outer try");
                    try {
                        console.log("inner try");
                    } finally {
                        await cleanup1();
                    }
                } finally {
                    await cleanup2();
                }
                console.log("done");
            }
            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("outer try\ninner try\ncleanup1\ncleanup2\ndone\n", output);
    }

    [Fact]
    public void Finally_WithAwait_ExceptionInFinallyReplacesOriginal()
    {
        var source = """
            async function throwInFinally(): Promise<void> {
                throw "finally error";
            }
            async function test(): Promise<void> {
                try {
                    throw "try error";
                } finally {
                    await throwInFinally();
                }
            }
            async function main(): Promise<void> {
                try {
                    await test();
                } catch (e) {
                    console.log("caught: " + e);
                }
            }
            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("caught: finally error\n", output);
    }

    [Fact]
    public void Finally_WithAwait_ReturnsValueFromTry()
    {
        var source = """
            async function cleanup(): Promise<void> {
                console.log("cleanup");
            }
            async function getValue(): Promise<number> {
                try {
                    return 42;
                } finally {
                    await cleanup();
                }
            }
            async function main(): Promise<void> {
                let v = await getValue();
                console.log("got: " + v);
            }
            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("cleanup\ngot: 42\n", output);
    }

    [Fact]
    public void Finally_WithAwait_AwaitInAllBlocks()
    {
        var source = """
            async function tryOp(): Promise<number> {
                console.log("try op");
                throw "error";
            }
            async function catchOp(): Promise<void> {
                console.log("catch op");
            }
            async function finallyOp(): Promise<void> {
                console.log("finally op");
            }
            async function main(): Promise<void> {
                try {
                    let v = await tryOp();
                    console.log("got: " + v);
                } catch (e) {
                    await catchOp();
                    console.log("caught: " + e);
                } finally {
                    await finallyOp();
                }
                console.log("done");
            }
            main();
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("try op\ncatch op\ncaught: error\nfinally op\ndone\n", output);
    }
}

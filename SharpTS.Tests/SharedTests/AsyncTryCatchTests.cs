using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for try/catch/finally inside async functions with await expressions.
/// Runs against both interpreter and compiler.
/// </summary>
public class AsyncTryCatchTests
{
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncTryCatch_AwaitInTry_ExceptionCaught(ExecutionMode mode)
    {
        var source = """
            async function throwError(): Promise<number> {
                throw "async error!";
            }
            async function main(): Promise<void> {
                try {
                    let x = await throwError();
                    console.log("should not reach: " + x);
                } catch (e) {
                    console.log("caught: " + e);
                }
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("caught: async error!\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncTryCatch_AwaitInTry_NoException(ExecutionMode mode)
    {
        var source = """
            async function getValue(): Promise<number> {
                return 42;
            }
            async function main(): Promise<void> {
                try {
                    let x = await getValue();
                    console.log("got: " + x);
                } catch (e) {
                    console.log("should not catch: " + e);
                }
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("got: 42\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncTryCatch_MultipleAwaitsInTry(ExecutionMode mode)
    {
        var source = """
            async function getValue(): Promise<number> {
                return 10;
            }
            async function throwError(): Promise<number> {
                throw "error on second";
            }
            async function main(): Promise<void> {
                try {
                    let a = await getValue();
                    console.log("a: " + a);
                    let b = await throwError();
                    console.log("should not reach: " + b);
                } catch (e) {
                    console.log("caught: " + e);
                }
                console.log("after");
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("a: 10\ncaught: error on second\nafter\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncTryCatch_NestedTryCatch(ExecutionMode mode)
    {
        var source = """
            async function throwError(): Promise<number> {
                throw "inner error";
            }
            async function main(): Promise<void> {
                try {
                    try {
                        let x = await throwError();
                        console.log("should not reach: " + x);
                    } catch (e) {
                        console.log("inner caught: " + e);
                        throw "rethrow from inner";
                    }
                } catch (e) {
                    console.log("outer caught: " + e);
                }
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("inner caught: inner error\nouter caught: rethrow from inner\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncTryCatch_SyncExceptionBeforeAwait(ExecutionMode mode)
    {
        var source = """
            async function getValue(): Promise<number> {
                return 42;
            }
            async function main(): Promise<void> {
                try {
                    throw "sync error";
                    let x = await getValue();
                    console.log("should not reach: " + x);
                } catch (e) {
                    console.log("caught: " + e);
                }
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("caught: sync error\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncTryCatch_SyncExceptionAfterAwait(ExecutionMode mode)
    {
        var source = """
            async function getValue(): Promise<number> {
                return 42;
            }
            async function main(): Promise<void> {
                try {
                    let x = await getValue();
                    console.log("got: " + x);
                    throw "sync error after";
                } catch (e) {
                    console.log("caught: " + e);
                }
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("got: 42\ncaught: sync error after\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncTryCatch_CatchParameter(ExecutionMode mode)
    {
        var source = """
            async function throwError(): Promise<number> {
                throw "the error message";
            }
            async function main(): Promise<void> {
                try {
                    let x = await throwError();
                } catch (err) {
                    console.log("error was: " + err);
                }
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("error was: the error message\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncTryCatch_CodeAfterCatch(ExecutionMode mode)
    {
        var source = """
            async function throwError(): Promise<number> {
                throw "error";
            }
            async function getValue(): Promise<number> {
                return 99;
            }
            async function main(): Promise<void> {
                try {
                    let x = await throwError();
                } catch (e) {
                    console.log("caught");
                }
                let y = await getValue();
                console.log("after: " + y);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("caught\nafter: 99\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncTryCatch_TryWithoutAwait_CatchAfterTry(ExecutionMode mode)
    {
        // Try block has no await, but there's await after the try/catch
        var source = """
            async function getValue(): Promise<number> {
                return 42;
            }
            async function main(): Promise<void> {
                try {
                    throw "sync error";
                } catch (e) {
                    console.log("caught: " + e);
                }
                let x = await getValue();
                console.log("got: " + x);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("caught: sync error\ngot: 42\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncTryCatch_InnerCatchRethrows(ExecutionMode mode)
    {
        var source = """
            async function getValue(): Promise<number> {
                return 1;
            }
            async function throwError(): Promise<number> {
                throw "original";
            }
            async function main(): Promise<void> {
                try {
                    try {
                        await getValue();
                        let x = await throwError();
                    } catch (inner) {
                        console.log("inner: " + inner);
                        throw "wrapped: " + inner;
                    }
                } catch (outer) {
                    console.log("outer: " + outer);
                }
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("inner: original\nouter: wrapped: original\n", output);
    }
}

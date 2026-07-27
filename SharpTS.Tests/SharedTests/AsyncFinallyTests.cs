using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for finally blocks with awaits in async functions.
/// Runs against both interpreter and compiler.
/// </summary>
public class AsyncFinallyTests
{
    [Theory, ModeData]
    public void Finally_WithAwait_BasicCase(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("in try\ncleanup called\nafter try/finally\n", output);
    }

    [Theory, ModeData]
    public void Finally_WithAwait_ExceptionInTry_WithCatch(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("in try\nin catch: error from try\ncleanup called\nafter try/catch/finally\n", output);
    }

    [Theory, ModeData]
    public void Finally_WithAwait_ExceptionInTry_NoCatch_Rethrows(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("in try\ncleanup called\nouter caught: error from try\ndone\n", output);
    }

    [Theory, ModeData]
    public void Finally_WithAwait_AwaitInBothTryAndFinally(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("got: 42\ncleanup called\ndone\n", output);
    }

    [Theory, ModeData]
    public void Finally_WithAwait_ExceptionInAwaitedCall_Rethrows(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("cleanup called\ncaught: async error\ndone\n", output);
    }

    [Theory, ModeData]
    public void Finally_WithAwait_MultipleAwaitsInFinally(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("in try\nstep1\nstep2\ndone\n", output);
    }

    [Theory, ModeData]
    public void Finally_WithAwait_NestedTryFinally(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("outer try\ninner try\ncleanup1\ncleanup2\ndone\n", output);
    }

    [Theory, ModeData]
    public void Finally_WithAwait_ExceptionInFinallyReplacesOriginal(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("caught: finally error\n", output);
    }

    [Theory, ModeData]
    public void Finally_WithAwait_ReturnsValueFromTry(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("cleanup\ngot: 42\n", output);
    }

    [Theory, ModeData]
    public void Finally_WithAwait_AwaitInAllBlocks(ExecutionMode mode)
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

        var output = TestHarness.Run(source, mode);
        Assert.Equal("try op\ncatch op\ncaught: error\nfinally op\ndone\n", output);
    }

    // --- #774: a non-local exit (break/continue/return) leaving a try whose body awaits must still
    // run the try's finally. Previously the compiled state machine jumped straight to the loop label,
    // skipping the (now flag-based) finally. ---

    [Theory, ModeData]
    public void Finally_ContinueOutOfTryWithAwait_RunsFinally(ExecutionMode mode)
    {
        // The issue repro: `continue` crosses the finally on iteration i==1.
        var source = """
            function delay(v: number): Promise<number> { return new Promise(r => r(v)); }
            async function main(): Promise<void> {
                let log = "";
                for (let i = 0; i < 3; i++) {
                    try {
                        const x = await delay(i);
                        if (x === 1) continue;
                        log += "b" + x;
                    } finally {
                        log += "f" + i;
                    }
                }
                console.log(log);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("b0f0f1b2f2\n", output);
    }

    [Theory, ModeData]
    public void Finally_BreakOutOfTryWithAwait_RunsFinally(ExecutionMode mode)
    {
        var source = """
            function delay(v: number): Promise<number> { return new Promise(r => r(v)); }
            async function main(): Promise<void> {
                let log = "";
                for (let i = 0; i < 3; i++) {
                    try {
                        const x = await delay(i);
                        if (x === 1) break;
                        log += "b" + x;
                    } finally {
                        log += "f" + i;
                    }
                }
                console.log(log);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("b0f0f1\n", output);
    }

    [Theory, ModeData]
    public void Finally_ContinueOutOfTryWithAwait_AwaitingFinally_RunsFinally(ExecutionMode mode)
    {
        // The finally itself awaits — previously dropped entirely on the continue.
        var source = """
            function delay(v: number): Promise<number> { return new Promise(r => r(v)); }
            async function main(): Promise<void> {
                let log = "";
                for (let i = 0; i < 3; i++) {
                    try {
                        const x = await delay(i);
                        if (x === 1) continue;
                        log += "b" + x;
                    } finally {
                        await delay(0);
                        log += "f" + i;
                    }
                }
                console.log(log);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("b0f0f1b2f2\n", output);
    }

    [Theory, ModeData]
    public void Finally_ReturnOutOfTryWithAwait_NoAwaitFinally_RunsFinally(ExecutionMode mode)
    {
        // The `return` half: a no-await finally was previously only run when the finally itself awaited.
        // The finally logs (observable side effect) and the returned value is delivered afterwards.
        var source = """
            function delay(v: number): Promise<number> { return new Promise(r => r(v)); }
            async function compute(): Promise<string> {
                try {
                    const x = await delay(5);
                    if (x === 5) return "ret" + x;
                    return "z";
                } finally {
                    console.log("finally ran");
                }
            }
            async function main(): Promise<void> {
                const r = await compute();
                console.log(r);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("finally ran\nret5\n", output);
    }

    [Theory, ModeData]
    public void Finally_BreakOutOfNestedTriesWithAwait_RunsBothFinallysInnermostFirst(ExecutionMode mode)
    {
        var source = """
            function delay(v: number): Promise<number> { return new Promise(r => r(v)); }
            async function main(): Promise<void> {
                let log = "";
                for (let i = 0; i < 2; i++) {
                    try {
                        try {
                            const x = await delay(i);
                            if (x === 0) continue;
                            log += "b" + x;
                        } finally {
                            log += "i" + i;
                        }
                    } finally {
                        log += "o" + i;
                    }
                }
                console.log(log);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("i0o0b1i1o1\n", output);
    }

    [Theory, ModeData]
    public void Finally_LabeledBreakOutOfTryWithAwait_RunsFinally(ExecutionMode mode)
    {
        var source = """
            function delay(v: number): Promise<number> { return new Promise(r => r(v)); }
            async function main(): Promise<void> {
                let log = "";
                outer: for (let i = 0; i < 2; i++) {
                    for (let j = 0; j < 2; j++) {
                        try {
                            const x = await delay(j);
                            if (x === 1) break outer;
                            log += i + "" + j;
                        } finally {
                            log += "f";
                        }
                    }
                }
                console.log(log);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("00ff\n", output);
    }

    [Theory, ModeData]
    public void Finally_BreakTargetingLoopInsideTryWithAwait_DoesNotRunFinally(ExecutionMode mode)
    {
        // Regression: a break whose target loop is *inside* the try crosses no finally, so the finally
        // must run only once (on normal completion of the try), not on the inner break.
        var source = """
            function delay(v: number): Promise<number> { return new Promise(r => r(v)); }
            async function main(): Promise<void> {
                let log = "";
                try {
                    for (let i = 0; i < 3; i++) {
                        const x = await delay(i);
                        if (x === 1) break;
                        log += "b" + x;
                    }
                    log += "t";
                } finally {
                    log += "f";
                }
                console.log(log);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("b0tf\n", output);
    }
}

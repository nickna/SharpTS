using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Async arrows nested inside an async CLASS METHOD. Two distinct compiled-mode defects are covered:
///
/// 1. #682: an arrow that WRITES a variable captured from the method must mutate it through the
///    method state machine's (reference-type) function display class, not by `unbox`+`stfld` on the
///    boxed value-type state machine (which fails IL verification — `unbox` yields a readonly managed
///    pointer). This is the async-method analogue of the free-async-function fix (#625).
///
/// 2. A pre-existing crash surfaced while fixing #682: ANY suspending (real-await) async arrow inside
///    an async method emitted invalid IL (InvalidProgramException), capture or not. The inline arrow
///    emission reused the method's compilation context — whose IL generator targets the method's
///    MoveNext, not the arrow's — so a strategy emitting via `ctx.IL` (e.g. `Promise.resolve`) wrote
///    into the method's IL stream after its `ret`. Emitting arrows through the shared
///    EmitAsyncArrowMoveNext (fresh per-arrow context) fixes both.
/// </summary>
public class AsyncMethodArrowCaptureTests
{
    [Theory, ModeData]
    public void AsyncMethod_Arrow_WritesCapturedVariable(ExecutionMode mode)
    {
        var source = """
            class C {
              async run() {
                let n = 0;
                const w = async () => { n = 9; };
                await w();
                return n;
              }
            }
            new C().run().then(r => console.log(r));
            """;

        Assert.Equal("9\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncMethod_Arrow_WritesCapturedVariableAfterAwait(ExecutionMode mode)
    {
        var source = """
            class C {
              async run() {
                let total = 0;
                const w = async () => { total = await Promise.resolve(99); };
                await w();
                return total;
              }
            }
            new C().run().then(r => console.log(r));
            """;

        Assert.Equal("99\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncMethod_Arrow_CompoundWriteWithAwaitAndMultipleCaptures(ExecutionMode mode)
    {
        var source = """
            class C {
              async run() {
                let total = 0;
                let order = "";
                const w = async () => {
                  total += 5;
                  order += "a";
                  total = await Promise.resolve(total + 7);
                };
                await w();
                return total + ":" + order;
              }
            }
            new C().run().then(r => console.log(r));
            """;

        Assert.Equal("12:a\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncMethod_SuspendingArrow_NoCapture(ExecutionMode mode)
    {
        // Pre-existing InvalidProgramException: a suspending async arrow in an async method, even
        // with no captures at all, miscompiled before the EmitAsyncArrowMoveNext delegation fix.
        var source = """
            class C {
              async run() {
                const w = async () => { await Promise.resolve(1); return 5; };
                return await w();
              }
            }
            new C().run().then(r => console.log(r));
            """;

        Assert.Equal("5\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncMethod_SuspendingArrow_ReadsCapture(ExecutionMode mode)
    {
        var source = """
            class C {
              async run() {
                let base = 100;
                const w = async () => { return await Promise.resolve(base + 1); };
                return await w();
              }
            }
            new C().run().then(r => console.log(r));
            """;

        Assert.Equal("101\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncMethod_Arrow_WritesCapturedParameter(ExecutionMode mode)
    {
        // The promoted variable is a method PARAMETER (copied into the function DC by the stub), not a
        // local — exercises the stub's captured-parameter copy path through the DC.
        var source = """
            class C {
              async run(x: number) {
                const w = async () => { x = await Promise.resolve(x + 1); };
                await w();
                return x;
              }
            }
            new C().run(10).then(r => console.log(r));
            """;

        Assert.Equal("11\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncStaticMethod_Arrow_WritesCapturedVariableAfterAwait(ExecutionMode mode)
    {
        // Static async methods take a separate emission path (EmitStaticAsyncMethodBody) with the
        // same defects as instance methods.
        var source = """
            class C {
              static async run() {
                let n = 0;
                const w = async () => { n = await Promise.resolve(7); };
                await w();
                return n;
              }
            }
            C.run().then(r => console.log(r));
            """;

        Assert.Equal("7\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncStaticMethod_SuspendingArrow_NoCapture(ExecutionMode mode)
    {
        var source = """
            class C {
              static async run() {
                const w = async () => { await Promise.resolve(1); return 8; };
                return await w();
              }
            }
            C.run().then(r => console.log(r));
            """;

        Assert.Equal("8\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void AsyncStaticMethod_Arrow_WritesCapturedVariable_PassesILVerification()
    {
        var source = """
            class C {
              static async run() {
                let n = 0;
                const w = async () => { n = await Promise.resolve(7); };
                await w();
                return n;
              }
            }
            C.run().then(r => console.log(r));
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("7\n", output);
    }

    [Fact]
    public void AsyncMethod_Arrow_WritesCapturedVariable_PassesILVerification()
    {
        // The core of #682: the captured-write store inside an async method must be VERIFIABLE.
        var source = """
            class C {
              async run() {
                let n = 0;
                const w = async () => { n = 9; };
                await w();
                return n;
              }
            }
            new C().run().then(r => console.log(r));
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("9\n", output);
    }

    [Fact]
    public void AsyncMethod_SuspendingArrow_PassesILVerificationAndRuns()
    {
        // A suspending arrow that writes a captured variable across the await — must both verify and
        // run (the write is routed through the method's function display class).
        var source = """
            class C {
              async run() {
                let total = 0;
                const w = async () => { total = await Promise.resolve(42); };
                await w();
                return total;
              }
            }
            new C().run().then(r => console.log(r));
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("42\n", output);
    }
}

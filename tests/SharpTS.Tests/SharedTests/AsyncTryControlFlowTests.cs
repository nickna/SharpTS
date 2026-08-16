using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regression tests for #727: in compiled mode, a <c>break</c>/<c>continue</c> that leaves a
/// <c>try</c> inside an <c>async function</c> body previously emitted a <c>Br</c> out of the real IL
/// exception region instead of a <c>Leave</c> — unverifiable IL (<c>BranchOutOfTry</c>) surfacing as an
/// <c>InvalidProgramException</c> when the async state machine starts. The fix mirrors the generator
/// emitters: <see cref="SharpTS.Compilation.AsyncMoveNextEmitter"/> now overrides
/// <c>EmitBranchToLabel</c> to emit <c>Leave</c> while inside a real IL exception block (depth tracked
/// in <c>EmitSimpleTryCatch</c>), and treats an escaping <c>break</c>/<c>continue</c> as a
/// segment-breaker in <c>EmitTryBodyWithAwaits</c> so its jump lands at the top level (outside the
/// mini IL try). The interpreter already behaved correctly, so the cross-mode theories double as a
/// parity guard. The <c>CompileVerifyAndRun</c> facts pin IL validity (the runtime JIT is lenient).
/// </summary>
public class AsyncTryControlFlowTests
{
    [Theory, ModeData]
    public void BreakOutOfSimpleTry_InAsync(ExecutionMode mode)
    {
        // The exact #727 break repro.
        var source = """
            async function main() {
              let n = 0;
              for (let i = 0; i < 5; i++) {
                try { if (i === 2) break; n++; } catch (e) {}
              }
              console.log("n=" + n);
            }
            main();
            """;

        Assert.Equal("n=2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ContinueOutOfSimpleTry_InAsync(ExecutionMode mode)
    {
        // The exact #727 continue repro.
        var source = """
            async function main() {
              let sum = 0;
              for (let i = 0; i < 5; i++) {
                try { if (i === 1) continue; sum += i; } catch (e) {}
              }
              console.log("sum=" + sum);
            }
            main();
            """;

        Assert.Equal("sum=9\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void BreakOutOfTryContainingAwait_InAsync(ExecutionMode mode)
    {
        // break leaves a try whose body awaits → the flag-based try path. The escaping break must be
        // emitted at the top level (outside the mini IL try), not Br out of it.
        var source = """
            function delay(v: number): Promise<number> { return new Promise(r => r(v)); }
            async function main() {
              let n = 0;
              for (let i = 0; i < 5; i++) {
                try {
                  const v = await delay(i);
                  if (v === 2) break;
                  n += v;
                } catch (e) {}
              }
              console.log("n=" + n);
            }
            main();
            """;

        Assert.Equal("n=1\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void BreakOutOfTryWithFinally_NoAwait_RunsFinally(ExecutionMode mode)
    {
        // A try with no awaits is a real IL try/finally (EmitSimpleTryCatch). The break Leaves it,
        // which runs the real finally — so cleanup happens even on the early exit.
        var source = """
            async function main() {
              let log = "";
              for (let i = 0; i < 4; i++) {
                try { if (i === 2) break; log += "t" + i; } finally { log += "f" + i; }
              }
              console.log(log);
            }
            main();
            """;

        Assert.Equal("t0f0t1f1f2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void BreakToInnerLoopInsideTry_DoesNotEscapeTry(ExecutionMode mode)
    {
        // The break targets a loop nested INSIDE the try, so it stays a legal in-region branch and the
        // statements after the inner loop (still inside the try) run.
        var source = """
            async function main() {
              let log = "";
              try {
                for (let j = 0; j < 3; j++) { if (j === 1) break; log += "j" + j; }
                log += "after";
              } catch (e) {}
              console.log(log);
            }
            main();
            """;

        Assert.Equal("j0after\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void SwitchBreakInsideTry_InAsync(ExecutionMode mode)
    {
        // An unlabeled break inside a switch that is inside a try belongs to the switch, not the loop.
        var source = """
            async function main() {
              let log = "";
              for (let i = 0; i < 3; i++) {
                try {
                  switch (i) { case 1: log += "one"; break; default: log += "d" + i; }
                  log += "|";
                } catch (e) {}
              }
              console.log(log);
            }
            main();
            """;

        Assert.Equal("d0|one|d2|\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void LabeledBreakOutOfTryWithAwait_AcrossNestedLoops_Compiled()
    {
        // Labeled `break outer` leaves a try whose body awaits, across nested loops. Compiled mode is
        // correct; the interpreter mishandles this shape (tracked separately), so this is compiled-only.
        var source = """
            async function main() {
              let log = "";
              outer: for (let i = 0; i < 3; i++) {
                for (let j = 0; j < 3; j++) {
                  try {
                    const x = await Promise.resolve(i * 10 + j);
                    if (x === 11) break outer;
                    log += x + ",";
                  } catch (e) {}
                }
              }
              console.log(log);
            }
            main();
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal("0,1,2,10,\n", output);
    }

    [Fact]
    public void BreakContinueOutOfTry_ProduceValidIL()
    {
        // Pin IL validity directly (the runtime JIT is lenient and would run invalid IL anyway).
        var source = """
            async function main() {
              let n = 0;
              for (let i = 0; i < 5; i++) {
                try { if (i === 2) break; n++; } catch (e) {}
              }
              for (let i = 0; i < 5; i++) {
                try { if (i === 1) continue; n += i; } catch (e) {}
              }
              for (let i = 0; i < 5; i++) {
                try { const v = await Promise.resolve(i); if (v === 2) break; n += v; } catch (e) {}
              }
              console.log("n=" + n);
            }
            main();
            """;

        var (errors, _) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
    }
}

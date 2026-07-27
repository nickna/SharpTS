using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regression tests for #559: a compiled <em>async</em> generator with a <c>yield</c>/<c>await</c>
/// inside <c>try</c>/<c>catch</c>/<c>finally</c> previously mishandled non-local exits that cross the
/// protected region. <c>break</c>/<c>continue</c> leaving the try emitted invalid IL
/// (<c>InvalidProgramException</c> in <c>MoveNextAsync</c>), and <c>return</c> / a <c>throw</c> from a
/// catch skipped the enclosing <c>finally</c>. This is the async analog of #500 (plain generator); the
/// fix ports the same unified exit-scope + pending-action dispatch into
/// <c>AsyncGeneratorMoveNextEmitter</c> so every non-local exit runs the enclosing <c>finally</c>(s)
/// before transferring control. See <c>AsyncGeneratorMoveNextEmitter.Statements.TryCatch.cs</c>.
///
/// <para>
/// COMPILED-ONLY. The interpreter eagerly drains an async generator's body: its internal side effects
/// (a finally's <c>console.log</c>, a catch's logging) run before the consumer's <c>for await…of</c>
/// body observes the yielded values, and a throwing async generator drops the consumer's processing of
/// already-yielded values entirely. That ordering / value-delivery divergence is pre-existing and
/// independent of try/finally control flow — it affects any async generator with observable internal
/// effects — and is tracked separately (#564 ordering, #566 manual next() rejection). These tests
/// therefore assert the compiled path,
/// where #559 lives and where output matches Node. The IL-verification cases at the bottom — emitted
/// IL must verify — are the heart of the fix.
/// </para>
/// </summary>
public class AsyncGeneratorTryFinallyTests
{
    [Theory, CompiledOnlyData]
    public void BreakOutOfTryFinally_RunsFinallyBeforeBreaking(ExecutionMode mode)
    {
        // The exact #559 repro: break leaving the try must run the finally first (was invalid IL).
        var source = """
            async function* g() {
              while (true) {
                try { yield 1; break; } finally { console.log("FIN"); }
              }
            }
            async function main() { for await (const v of g()) console.log("v" + v); }
            main();
            """;

        Assert.Equal("v1\nFIN\n", TestHarness.Run(source, mode));
    }

    [Theory, CompiledOnlyData]
    public void ContinueOutOfTryFinally_RunsFinallyThatIteration(ExecutionMode mode)
    {
        // `continue` from inside the try must run the finally before the next iteration, and the code
        // after the continue must be skipped on that iteration only (was invalid IL).
        var source = """
            async function* g() {
              for (let i = 0; i < 3; i++) {
                try {
                  yield i;
                  if (i === 1) continue;
                  console.log("after" + i);
                } finally {
                  console.log("fin" + i);
                }
              }
            }
            async function main() { for await (const v of g()) console.log("got" + v); }
            main();
            """;

        Assert.Equal("got0\nafter0\nfin0\ngot1\nfin1\ngot2\nafter2\nfin2\n", TestHarness.Run(source, mode));
    }

    [Theory, CompiledOnlyData]
    public void ThrowFromCatch_RunsFinallyThenPropagates(ExecutionMode mode)
    {
        // The exact #559 repro: a throw inside the catch must still run the finally before the
        // exception propagates out of the generator to the consumer (finally was skipped).
        var source = """
            async function* g() {
              try { yield 1; throw "a"; } catch (e) { throw "b"; } finally { console.log("FIN"); }
            }
            async function main() {
              try { for await (const v of g()) console.log("v" + v); } catch (e) { console.log("outer " + e); }
            }
            main();
            """;

        Assert.Equal("v1\nFIN\nouter b\n", TestHarness.Run(source, mode));
    }

    [Theory, CompiledOnlyData]
    public void ReturnFromCatch_RunsFinallyBeforeCompleting(ExecutionMode mode)
    {
        // A `return` from the catch body must run the finally; the yield after the try must not run.
        var source = """
            async function* g() {
              try { yield 1; throw "x"; } catch (e) { return; } finally { console.log("FIN"); }
              yield 99;
            }
            async function main() { for await (const v of g()) console.log("v" + v); }
            main();
            """;

        Assert.Equal("v1\nFIN\n", TestHarness.Run(source, mode));
    }

    [Theory, CompiledOnlyData]
    public void ReturnInsideTryFinally_RunsFinallyBeforeCompleting(ExecutionMode mode)
    {
        // `return` inside the try must run the finally before the generator completes; the statement
        // after the return must not execute (was invalid IL — the return's `ret` sat in a mini block).
        var source = """
            async function* g() {
              try {
                yield 1;
                return;
                yield 99;
              } finally {
                console.log("fin");
              }
            }
            async function main() { for await (const v of g()) console.log("v" + v); }
            main();
            """;

        Assert.Equal("v1\nfin\n", TestHarness.Run(source, mode));
    }

    [Theory, CompiledOnlyData]
    public void ReturnInsideNestedTryFinally_RunsAllEnclosingFinallys(ExecutionMode mode)
    {
        var source = """
            async function* g() {
              try {
                try {
                  yield 1;
                  return;
                } finally {
                  console.log("inner");
                }
              } finally {
                console.log("outer");
              }
              yield 99;
            }
            async function main() { for await (const v of g()) console.log("v" + v); }
            main();
            """;

        Assert.Equal("v1\ninner\nouter\n", TestHarness.Run(source, mode));
    }

    [Theory, CompiledOnlyData]
    public void BreakThroughNestedFinallys_RunsInnerThenOuter(ExecutionMode mode)
    {
        // A break that leaves two enclosing trys runs both finallys, innermost first.
        var source = """
            async function* g() {
              while (true) {
                try {
                  try { yield 1; break; } finally { console.log("inner"); }
                } finally { console.log("outer"); }
              }
            }
            async function main() { for await (const v of g()) console.log("v" + v); }
            main();
            """;

        Assert.Equal("v1\ninner\nouter\n", TestHarness.Run(source, mode));
    }

    [Theory, CompiledOnlyData]
    public void LabeledBreakToOuterLoop_RunsInterveningFinally(ExecutionMode mode)
    {
        // A labeled break targeting the outer loop runs the finally of the inner loop's try.
        var source = """
            async function* g() {
              outer: for (let i = 0; i < 3; i++) {
                for (let j = 0; j < 3; j++) {
                  try { yield i * 10 + j; if (j === 1) break outer; } finally { console.log("fin" + i + j); }
                }
              }
            }
            async function main() { for await (const v of g()) console.log("v" + v); }
            main();
            """;

        Assert.Equal("v0\nfin00\nv1\nfin01\n", TestHarness.Run(source, mode));
    }

    [Theory, CompiledOnlyData]
    public void LabeledContinueToOuterLoop_RunsInterveningFinally(ExecutionMode mode)
    {
        // The labeled-`continue` sibling of LabeledBreakToOuterLoop (#586/#589). `continue outer` must
        // run the inner loop's finally and then advance the *outer* loop — skipping the rest of the
        // inner loop — rather than continuing the inner loop. The same EnterLoop pending-label adoption
        // (#586) that resolves the labeled break drives this path; without behavioral coverage the
        // continue direction could regress silently (the labeled-break test alone would not catch it).
        var source = """
            async function* g() {
              outer: for (let i = 0; i < 2; i++) {
                for (let j = 0; j < 3; j++) {
                  try { yield i * 10 + j; if (j === 0) continue outer; } finally { console.log("fin" + i + j); }
                }
              }
            }
            async function main() { for await (const v of g()) console.log("v" + v); }
            main();
            """;

        Assert.Equal("v0\nfin00\nv10\nfin10\n", TestHarness.Run(source, mode));
    }

    [Theory, CompiledOnlyData]
    public void BreakToLoopBetweenTwoTrys_RunsOnlyInnerFinally(ExecutionMode mode)
    {
        // The break targets a loop that sits *between* two trys: only the finally inside that loop
        // runs at the break; the outer finally runs once, later, when the generator completes.
        var source = """
            async function* g() {
              try {
                for (let i = 0; i < 3; i++) {
                  try { yield i; if (i === 1) break; } finally { console.log("inner" + i); }
                }
                console.log("after-loop");
              } finally { console.log("OUTER"); }
            }
            async function main() { for await (const v of g()) console.log("v" + v); }
            main();
            """;

        Assert.Equal("v0\ninner0\nv1\ninner1\nafter-loop\nOUTER\n", TestHarness.Run(source, mode));
    }

    [Theory, CompiledOnlyData]
    public void BreakWithYieldingFinally_DrivesFinallyThenBreaks(ExecutionMode mode)
    {
        // The finally that runs on the break path itself yields; the break completes only after the
        // finally's yields are driven, then control resumes after the loop.
        var source = """
            async function* g() {
              while (true) {
                try { yield 1; break; } finally { yield 2; }
              }
              yield 3;
            }
            async function main() { for await (const v of g()) console.log("v" + v); }
            main();
            """;

        Assert.Equal("v1\nv2\nv3\n", TestHarness.Run(source, mode));
    }

    [Theory, CompiledOnlyData]
    public void ReturnInTry_WithYieldingFinally_CompletesAfterFinallyYields(ExecutionMode mode)
    {
        // The finally itself yields, suspending MoveNextAsync between the `return` and the completion.
        // The pending-return state must survive that suspension (it lives in a field, not a local), so
        // the generator completes after the finally rather than running `yield 99`.
        var source = """
            async function* g() {
              try {
                yield 1;
                return;
              } finally {
                yield 2;
              }
              yield 99;
            }
            async function main() { for await (const v of g()) console.log("v" + v); }
            main();
            """;

        Assert.Equal("v1\nv2\n", TestHarness.Run(source, mode));
    }

    [Theory, CompiledOnlyData]
    public void ThrowFromFinally_RunsEnclosingFinallyThenPropagates(ExecutionMode mode)
    {
        // A throw raised inside a finally body must still run the enclosing finally before it
        // propagates to the consumer.
        var source = """
            async function* g() {
              try {
                try { yield 1; } finally { throw "boom"; }
              } finally { console.log("OUTER"); }
            }
            async function main() {
              try { for await (const v of g()) console.log("v" + v); } catch (e) { console.log("caught " + e); }
            }
            main();
            """;

        Assert.Equal("v1\nOUTER\ncaught boom\n", TestHarness.Run(source, mode));
    }

    [Theory, CompiledOnlyData]
    public void BreakOutOfInnerCatchlessTry_RunsOuterFinally(ExecutionMode mode)
    {
        // The break leaves an inner try/catch that has no finally, nested in an outer try-with-
        // finally; the outer finally must still run on the way out.
        var source = """
            async function* g() {
              while (true) {
                try {
                  try { yield 1; break; } catch (e) {}
                } finally { console.log("OUTERFIN"); }
              }
            }
            async function main() { for await (const v of g()) console.log("v" + v); }
            main();
            """;

        Assert.Equal("v1\nOUTERFIN\n", TestHarness.Run(source, mode));
    }

    [Theory, CompiledOnlyData]
    public void YieldStarInTryFinally_DelegatesThenRunsFinally(ExecutionMode mode)
    {
        var source = """
            async function* inner() { yield 2; yield 3; }
            async function* g() {
              try {
                yield 1;
                yield* inner();
                yield 4;
              } finally {
                console.log("fin");
              }
            }
            async function main() { for await (const v of g()) console.log("v" + v); }
            main();
            """;

        Assert.Equal("v1\nv2\nv3\nv4\nfin\n", TestHarness.Run(source, mode));
    }

    // ---- await inside the protected region (async-generator-specific) ----

    [Theory, CompiledOnlyData]
    public void AwaitThenReturnInTryFinally_RunsFinally(ExecutionMode mode)
    {
        // An await suspension precedes the return inside the try; the finally must still run.
        var source = """
            async function* g() {
              try { await Promise.resolve(0); yield 1; return; } finally { console.log("FIN"); }
            }
            async function main() { for await (const v of g()) console.log("v" + v); }
            main();
            """;

        Assert.Equal("v1\nFIN\n", TestHarness.Run(source, mode));
    }

    [Theory, CompiledOnlyData]
    public void AwaitThenBreakOutOfTryFinally_RunsFinally(ExecutionMode mode)
    {
        var source = """
            async function* g() {
              while (true) {
                try { const x = await Promise.resolve(5); yield x; break; } finally { console.log("FIN"); }
              }
            }
            async function main() { for await (const v of g()) console.log("v" + v); }
            main();
            """;

        Assert.Equal("v5\nFIN\n", TestHarness.Run(source, mode));
    }

    [Theory, CompiledOnlyData]
    public void AwaitThenContinueOutOfTryFinally_RunsFinallyEachIteration(ExecutionMode mode)
    {
        var source = """
            async function* g() {
              for (let i = 0; i < 2; i++) {
                try { await Promise.resolve(0); yield i; continue; } finally { console.log("fin" + i); }
              }
            }
            async function main() { for await (const v of g()) console.log("v" + v); }
            main();
            """;

        Assert.Equal("v0\nfin0\nv1\nfin1\n", TestHarness.Run(source, mode));
    }

    // ---- #597: return in a NO-yield try, and return value preserved across a yielding finally ----

    [Theory, CompiledOnlyData]
    public void ReturnInNoYieldTry_RunsFinally(ExecutionMode mode)
    {
        // #597(a): the `return` sits in a try whose body has NO suspension (a real IL try via
        // EmitSimpleTryCatch). Previously the return completed the state machine directly inside the
        // protected region — illegal IL (InvalidProgramException). It now Leaves a deferred-return
        // landing pad that runs the no-yield finally first. The outer `yield 0` makes g a state machine.
        var source = """
            async function* g() { yield 0; try { return; } finally { console.log("f"); } }
            async function main() { for await (const v of g()) console.log("v" + v); }
            main();
            """;

        Assert.Equal("v0\nf\n", TestHarness.Run(source, mode));
    }

    [Theory, CompiledOnlyData]
    public void ReturnValueInTry_WithYieldingFinally_PreservesReturnValue(ExecutionMode mode)
    {
        // #597(b): when the finally yields, its yielded value must not clobber the return value in
        // <>2__current. The return value (5) is stashed in <>pendingReturnValue at the `return` and
        // restored to Current by the return terminal after the finally has run. Driven via next() so
        // the exact { value, done } records are observable.
        var source = """
            async function* g() { try { return 5; } finally { yield 9; } }
            async function main() {
                const it = g();
                console.log(JSON.stringify(await it.next()));
                console.log(JSON.stringify(await it.next()));
            }
            main();
            """;

        Assert.Equal("{\"value\":9,\"done\":false}\n{\"value\":5,\"done\":true}\n", TestHarness.Run(source, mode));
    }

    [Theory, CompiledOnlyData]
    public void BreakOutOfNoYieldTry_NestedInYieldingTry_RunsOuterFinally(ExecutionMode mode)
    {
        // A break inside an inner NO-yield try (real IL) nested in an outer flag-based try-with-finally
        // must still run the outer finally. Aligning the async EmitBreak/EmitContinue routing with the
        // sync emitter (route through the flag finally with `Leave` even inside a real IL block) fixed
        // this latent gap alongside #597; the old code Leave'd straight to the break label, skipping it.
        var source = """
            async function* g() {
              while (true) {
                try {
                  yield 0;
                  try { break; } catch (e) {}
                } finally { console.log("OUTERFIN"); }
              }
            }
            async function main() { for await (const v of g()) console.log("v" + v); }
            main();
            """;

        Assert.Equal("v0\nOUTERFIN\n", TestHarness.Run(source, mode));
    }

    [Theory, CompiledOnlyData]
    public void ContinueOutOfNoYieldTry_NestedInYieldingTry_RunsOuterFinallyEachIteration(ExecutionMode mode)
    {
        // The continue sibling of the break case above: continue from an inner no-yield try nested in
        // an outer flag-based try-with-finally runs the outer finally each iteration (and skips the
        // statement after the continue).
        var source = """
            async function* g() {
              for (let i = 0; i < 2; i++) {
                try {
                  yield i;
                  try { continue; } catch (e) {}
                  console.log("unreached");
                } finally { console.log("fin" + i); }
              }
            }
            async function main() { for await (const v of g()) console.log("v" + v); }
            main();
            """;

        Assert.Equal("v0\nfin0\nv1\nfin1\n", TestHarness.Run(source, mode));
    }

    // ---- #569: an async-generator catch parameter read after a suspension in the catch body ----
    // The parameter is hoisted to a state-machine field because it lives across the suspension, but the
    // catch binding stored it only to a fresh IL local, so the post-resume read resolved the unset field
    // and came back null. The binding is now field-aware (mirrors the plain-generator fix in #477/#500).
    // CompiledOnly: the interpreter eagerly drains the async generator body, so its internal logs
    // interleave out of Node order (a separate concern, #564).

    [Theory, CompiledOnlyData]
    public void CatchParamAfterYieldInCatchBody_PreservesValue(ExecutionMode mode)
    {
        // The exact #569 repro: `e` is read after the `yield 0` inside the catch.
        var source = """
            async function* g() {
              try { yield 1; throw "boom"; } catch (e) { yield 0; console.log("caught " + e); }
            }
            async function main() { for await (const v of g()) console.log("v" + v); }
            main();
            """;

        Assert.Equal("v1\nv0\ncaught boom\n", TestHarness.Run(source, mode));
    }

    [Theory, CompiledOnlyData]
    public void CatchParamAfterAwaitInCatchBody_PreservesValue(ExecutionMode mode)
    {
        // The await sibling: `e` is read after an `await` suspension inside the catch.
        var source = """
            async function* g() {
              try { yield 1; throw "boom"; } catch (e) { await Promise.resolve(0); console.log("caught " + e); yield 9; }
            }
            async function main() { for await (const v of g()) console.log("v" + v); }
            main();
            """;

        Assert.Equal("v1\ncaught boom\nv9\n", TestHarness.Run(source, mode));
    }

    // ---- #628: throwing/rejecting null/undefined into a flag-based try/catch must engage the catch ----
    // The async analog of #619: the flag-based scheme inferred "was an exception thrown?" from the
    // captured value's nullness, so a thrown/rejected null/undefined (a null CLR reference) read as "no
    // exception" — skipping the catch. A dedicated present flag now records presence independent of the
    // value, set by both the sync-segment capture and the rejected-await routing (#617). CompiledOnly:
    // the interpreter's eager-drain ordering (#564) is a separate concern.

    [Theory, CompiledOnlyData]
    public void ThrowNullIntoFlagBasedTryCatch_IsCaught(ExecutionMode mode)
    {
        // The exact #628 repro: throw null after a yield, caught in the same try's catch.
        var source = """
            async function* g() { try { yield 1; throw null; } catch (e) { console.log("caught isNull=" + (e === null)); } }
            async function main() { for await (const v of g()) console.log("v" + v); }
            main();
            """;

        Assert.Equal("v1\ncaught isNull=true\n", TestHarness.Run(source, mode));
    }

    [Theory, CompiledOnlyData]
    public void ThrowUndefinedIntoFlagBasedTryCatch_IsCaught(ExecutionMode mode)
    {
        // throw undefined likewise reaches the catch (was skipped). Asserted via `=== undefined`.
        var source = """
            async function* g() { try { yield 1; throw undefined; } catch (e) { console.log("isUndef=" + (e === undefined)); } }
            async function main() { for await (const v of g()) console.log("v" + v); }
            main();
            """;

        Assert.Equal("v1\nisUndef=true\n", TestHarness.Run(source, mode));
    }

    [Theory, CompiledOnlyData]
    public void RejectedNullAwaitInTry_IsCaughtWithNullBinding(ExecutionMode mode)
    {
        // A rejected await whose reason is null must reach the catch (the await-routing present flag,
        // #617 + #628), and the catch param must bind the null reason.
        var source = """
            async function* g() { try { await Promise.reject(null); } catch (e) { console.log("isNull=" + (e === null)); } yield 1; }
            async function main() { for await (const v of g()) console.log("v" + v); }
            main();
            """;

        Assert.Equal("isNull=true\nv1\n", TestHarness.Run(source, mode));
    }

    [Theory, CompiledOnlyData]
    public void FalsyNonNullThrowIntoFlagBasedTryCatch_StillCaught(ExecutionMode mode)
    {
        // Boundary guard: a falsy-but-non-null thrown value (0) boxes to a non-null reference and was
        // already caught; it must remain so under the present-flag scheme.
        var source = """
            async function* g() { try { yield 1; throw 0; } catch (e) { console.log("caught e=" + e); } }
            async function main() { for await (const v of g()) console.log("v" + v); }
            main();
            """;

        Assert.Equal("v1\ncaught e=0\n", TestHarness.Run(source, mode));
    }

    // ---- #632: a throw escaping a handler body must reach an enclosing flag-based try's catch ----
    // Async analog of the plain-generator #632: a handler-body throw is routed into the enclosing
    // flag-based try's capture local (running the finally(s) inside that try first) and branched to its
    // cleanup, instead of a real IL throw that bypasses the flag-based catch.

    [Theory, CompiledOnlyData]
    public void RethrowFromCatch_CaughtByEnclosingTryCatch(ExecutionMode mode)
    {
        // The exact async #632 repro: a rejected await is caught by the inner catch, which rethrows; the
        // outer catch must catch the rethrown value.
        var source = """
            async function* g() {
              try {
                try { await Promise.reject("inner"); } catch (e: any) { console.log("inner caught " + e); throw "rethrown"; }
              } catch (e: any) { console.log("outer caught " + e); }
              yield 1;
            }
            async function main() { for await (const v of g()) console.log("v" + v); }
            main();
            """;

        Assert.Equal("inner caught inner\nouter caught rethrown\nv1\n", TestHarness.Run(source, mode));
    }

    [Theory, CompiledOnlyData]
    public void RethrowFromCatch_WithInterveningFinally_RunsFinallyThenOuterCatch(ExecutionMode mode)
    {
        // The inner try has a finally: a throw escaping the inner catch runs that finally before the
        // outer catch sees the value.
        var source = """
            async function* g() {
              try {
                try { yield 0; throw "inner"; }
                catch (e: any) { console.log("C1 " + e); throw "rethrown"; }
                finally { console.log("F1"); }
              } catch (e: any) { console.log("outer " + e); }
              yield 1;
            }
            async function main() { for await (const v of g()) console.log("v" + v); }
            main();
            """;

        Assert.Equal("v0\nC1 inner\nF1\nouter rethrown\nv1\n", TestHarness.Run(source, mode));
    }

    [Theory, CompiledOnlyData]
    public void UncaughtThrowFromCatchlessTryFinally_CaughtByEnclosingTry(ExecutionMode mode)
    {
        // An uncaught exception leaving a catch-less inner try/finally must, after its finally runs,
        // propagate to the enclosing flag-based catch rather than escape the state machine.
        var source = """
            async function* g() {
              try {
                try { yield 0; throw "x"; } finally { console.log("F"); }
              } catch (e: any) { console.log("outer " + e); }
              yield 1;
            }
            async function main() { for await (const v of g()) console.log("v" + v); }
            main();
            """;

        Assert.Equal("v0\nF\nouter x\nv1\n", TestHarness.Run(source, mode));
    }

    // ---- #675: a real exception escaping a nested real-IL try/catch inside a flag-based catch/finally
    // body must reach an enclosing flag-based try's catch (async-generator analog of the sync #675) ----

    [Theory, CompiledOnlyData]
    public void RealThrowEscapingNestedTryInCatchBody_CaughtByEnclosingTry(ExecutionMode mode)
    {
        // A nested real-IL try/catch inside a flag-based catch body rethrows; the in-flight value must
        // reach the enclosing flag-based catch rather than escape MoveNextAsync.
        var source = """
            async function* g() {
              try {
                try { yield 0; throw "a"; }
                catch (e: any) {
                  try { throw "b"; }
                  catch (e2: any) { console.log("C3 " + e2); throw "c"; }
                }
              } catch (e: any) { console.log("outer " + e); }
              yield 1;
            }
            async function main() { for await (const v of g()) console.log("v" + v); }
            main();
            """;

        Assert.Equal("v0\nC3 b\nouter c\nv1\n", TestHarness.Run(source, mode));
    }

    [Theory, CompiledOnlyData]
    public void RealThrowEscapingNestedTryInFinallyBody_CaughtByEnclosingTry(ExecutionMode mode)
    {
        // The finally-body analog.
        var source = """
            async function* g() {
              try {
                try { yield 0; }
                finally {
                  try { throw "b"; }
                  catch (e2: any) { console.log("F3 " + e2); throw "c"; }
                }
              } catch (e: any) { console.log("outer " + e); }
              yield 1;
            }
            async function main() { for await (const v of g()) console.log("v" + v); }
            main();
            """;

        Assert.Equal("v0\nF3 b\nouter c\nv1\n", TestHarness.Run(source, mode));
    }

    // ---- IL-verification guards (the heart of #559: emitted IL must verify) ----

    [Theory]
    [InlineData("async function* g() { try { yield 1; yield 2; } finally { console.log('f'); } } async function main(){ for await (const v of g()) {} } main();")]
    [InlineData("async function* g() { try { yield 1; throw 'x'; } catch (e) { console.log(e); } } async function main(){ for await (const v of g()) {} } main();")]
    [InlineData("async function* g() { try { yield 1; } catch (e) {} finally { console.log('f'); } yield 2; } async function main(){ for await (const v of g()) {} } main();")]
    [InlineData("async function* g() { for (let i=0;i<2;i++){ try { yield i; } finally { console.log(i); } } } async function main(){ for await (const v of g()) {} } main();")]
    [InlineData("async function* g() { try { try { yield 1; } finally { console.log('a'); } } finally { console.log('b'); } } async function main(){ for await (const v of g()) {} } main();")]
    [InlineData("async function* g() { try { yield 1; return; } finally { console.log('f'); } } async function main(){ for await (const v of g()) {} } main();")]
    [InlineData("async function* g() { try { yield 1; } finally { yield 2; } } async function main(){ for await (const v of g()) {} } main();")]
    [InlineData("async function* g() { while (true) { try { yield 1; break; } finally { console.log('f'); } } } async function main(){ for await (const v of g()) {} } main();")]
    [InlineData("async function* inner(){ yield 2; } async function* g() { try { yield 1; yield* inner(); } finally { console.log('f'); } } async function main(){ for await (const v of g()) {} } main();")]
    // #559 control-flow shapes: continue, throw-from-catch, return-from-catch, nested-finally break,
    // labeled break, labeled continue (#586/#589), break to a loop sitting between two trys, and a
    // yielding finally on the break path.
    [InlineData("async function* g() { for (let i=0;i<2;i++){ try { yield i; continue; } finally { console.log('f'); } } } async function main(){ for await (const v of g()) {} } main();")]
    [InlineData("async function* g() { try { yield 1; throw 'a'; } catch (e) { throw 'b'; } finally { console.log('f'); } } async function main(){ try { for await (const v of g()) {} } catch (e) {} } main();")]
    [InlineData("async function* g() { try { yield 1; throw 'a'; } catch (e) { return; } finally { console.log('f'); } } async function main(){ for await (const v of g()) {} } main();")]
    [InlineData("async function* g() { while (true) { try { try { yield 1; break; } finally { console.log('a'); } } finally { console.log('b'); } } } async function main(){ for await (const v of g()) {} } main();")]
    [InlineData("async function* g() { outer: for(let i=0;i<2;i++){ for(let j=0;j<2;j++){ try { yield j; break outer; } finally { console.log('f'); } } } } async function main(){ for await (const v of g()) {} } main();")]
    [InlineData("async function* g() { outer: for(let i=0;i<2;i++){ for(let j=0;j<2;j++){ try { yield j; continue outer; } finally { console.log('f'); } } } } async function main(){ for await (const v of g()) {} } main();")]
    [InlineData("async function* g() { try { for(let i=0;i<2;i++){ try { yield i; break; } finally { console.log('a'); } } } finally { console.log('b'); } } async function main(){ for await (const v of g()) {} } main();")]
    [InlineData("async function* g() { while (true) { try { yield 1; break; } finally { yield 2; } } } async function main(){ for await (const v of g()) {} } main();")]
    // await suspensions crossing the protected region alongside the non-local exits.
    [InlineData("async function* g() { try { await Promise.resolve(0); yield 1; return; } finally { console.log('f'); } } async function main(){ for await (const v of g()) {} } main();")]
    [InlineData("async function* g() { while (true) { try { await Promise.resolve(0); yield 1; break; } finally { console.log('f'); } } } async function main(){ for await (const v of g()) {} } main();")]
    // #597: return in a NO-yield try (deferred-return landing pad), a value return preserved across a
    // yielding finally, and break/continue from an inner no-yield try nested in an outer yielding try.
    [InlineData("async function* g() { yield 0; try { return; } finally { console.log('f'); } } async function main(){ for await (const v of g()) {} } main();")]
    [InlineData("async function* g() { yield 0; try { return 5; } finally { console.log('f'); } } async function main(){ for await (const v of g()) {} } main();")]
    [InlineData("async function* g() { try { return 5; } finally { yield 9; } } async function main(){ for await (const v of g()) {} } main();")]
    [InlineData("async function* g() { while (true) { try { yield 0; try { break; } catch (e) {} } finally { console.log('f'); } } } async function main(){ for await (const v of g()) {} } main();")]
    [InlineData("async function* g() { for (let i=0;i<2;i++) { try { yield i; try { continue; } catch (e) {} } finally { console.log('f'); } } } async function main(){ for await (const v of g()) {} } main();")]
    // #569: catch parameter read after a suspension (yield / await) in the catch body — hoisted-field binding.
    [InlineData("async function* g() { try { yield 1; throw 'boom'; } catch (e) { yield 0; console.log(e); } } async function main(){ for await (const v of g()) {} } main();")]
    [InlineData("async function* g() { try { yield 1; throw 'boom'; } catch (e) { await Promise.resolve(0); console.log(e); } } async function main(){ for await (const v of g()) {} } main();")]
    // #628: thrown/rejected null/undefined into a flag-based try — the present-flag gates must verify.
    [InlineData("async function* g() { try { yield 1; throw null; } catch (e) { console.log(e); } } async function main(){ for await (const v of g()) {} } main();")]
    [InlineData("async function* g() { try { await Promise.reject(null); } catch (e) { console.log(e); } yield 1; } async function main(){ for await (const v of g()) {} } main();")]
    // #632: a throw/rethrow escaping a handler routed into an enclosing flag-based try's catch.
    [InlineData("async function* g() { try { try { yield 0; throw 'a'; } catch (e) { throw 'b'; } } catch (e) { console.log(e); } yield 1; } async function main(){ for await (const v of g()) {} } main();")]
    [InlineData("async function* g() { try { try { yield 0; throw 'a'; } catch (e) { throw 'b'; } finally { console.log('f'); } } catch (e) { console.log(e); } } async function main(){ for await (const v of g()) {} } main();")]
    [InlineData("async function* g() { try { try { yield 0; throw 'x'; } finally { yield 7; } } catch (e) { console.log(e); } } async function main(){ for await (const v of g()) {} } main();")]
    // #675: a real exception escaping a nested real-IL try/catch inside a flag-based catch/finally body —
    // the handler-body sync-segment capture + outward routing must verify in the async generator too.
    [InlineData("async function* g() { try { try { yield 0; throw 'a'; } catch (e) { try { throw 'b'; } catch (e2) { throw 'c'; } } } catch (e) { console.log(e); } yield 1; } async function main(){ for await (const v of g()) {} } main();")]
    [InlineData("async function* g() { try { try { yield 0; } finally { try { throw 'b'; } catch (e2) { throw 'c'; } } } catch (e) { console.log(e); } } async function main(){ for await (const v of g()) {} } main();")]
    [InlineData("async function* g() { try { try { yield 0; throw 'a'; } catch (e) { try { throw 'b'; } catch (e2) { throw 'c'; } } finally { console.log('f'); } } catch (e) { console.log(e); } } async function main(){ for await (const v of g()) {} } main();")]
    public void AsyncGeneratorTryFinallyWithSuspension_EmitsVerifiableIL(string source)
    {
        var errors = TestHarness.CompileAndVerifyOnly(source);
        Assert.Empty(errors);
    }
}

using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regression tests for the compiled async-function suspension walker (the shared
/// <c>ExpressionEmitterBase.StmtContainsSuspension/ExprContainsSuspension</c> pair
/// since #1121; formerly a per-family <c>AsyncFunctionMoveNextEmitter.ContainsAwaitInStmt/Expr</c>
/// copy), which had drifted from the generator/async-generator walkers: it was missing the
/// <c>Switch</c>, <c>Throw</c>, <c>Print</c>, and <c>LabeledStatement</c>
/// statement arms and the <c>CompoundAssign</c>, <c>GetIndex</c>, and
/// <c>SetIndex</c> expression arms. When an <c>await</c> sat inside one of those
/// positions <em>within a <c>try</c></em>, the walker under-reported suspension,
/// so the emitter chose the real-IL <c>try</c> lowering whose resume label
/// became an illegal <c>BranchIntoTry</c> target — the #631/#850 failure mode
/// (InvalidProgramException / ILVerify failure) in compiled mode, while the
/// interpreter was unaffected. Most cases are dual-mode (compiled pinned against
/// the interpreter); two constructs — <c>throw await</c> and <c>arr[i] += await</c>
/// inside a try — remain compiled-mode bugs in the flag-based state-machine emission
/// (not the walker) and are pinned interpreter-only with a note. Numbers are kept
/// integral to avoid the separate number-to-string formatting differences.
/// </summary>
public class AsyncTrySuspensionWalkerTests
{
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncTry_AwaitInsideSwitch(ExecutionMode mode)
    {
        var source = """
            async function val(): Promise<number> { return 1; }
            async function main(): Promise<void> {
                try {
                    switch (1) {
                        case 1: {
                            const v = await val();
                            console.log("case: " + v);
                            break;
                        }
                    }
                } catch (e) {
                    console.log("caught: " + e);
                }
                console.log("done");
            }
            main();
            """;
        Assert.Equal("case: 1\ndone\n", TestHarness.Run(source, mode));
    }

    // `throw await f()` inside a try is a segment-breaker emitted at the top level (outside the
    // sync-segment mini try/catch), so before #914 the compiled raw throw escaped the guest try and
    // faulted the Task. Fixed by AsyncFunctionMoveNextEmitter.EmitThrow routing a top-level throw into
    // the active flag-based try's exception local (running any intervening finally first).
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncTry_AwaitInsideThrow(ExecutionMode mode)
    {
        var source = """
            async function makeErr(): Promise<string> { return "boom"; }
            async function main(): Promise<void> {
                try {
                    throw await makeErr();
                } catch (e) {
                    console.log("caught: " + e);
                }
            }
            main();
            """;
        Assert.Equal("caught: boom\n", TestHarness.Run(source, mode));
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncTry_AwaitInsideConsoleLog(ExecutionMode mode)
    {
        // console.log(...) lowers to a Stmt.Print, whose arm was missing.
        var source = """
            async function val(): Promise<string> { return "hi"; }
            async function main(): Promise<void> {
                try {
                    console.log(await val());
                } catch (e) {
                    console.log("caught");
                }
            }
            main();
            """;
        Assert.Equal("hi\n", TestHarness.Run(source, mode));
    }

    // `arr[i] += await f()` parses to Expr.CompoundSetIndex, which the async-function
    // suspension walker did not descend into — so the try under-reported suspension and
    // took the real-IL lowering (illegal BranchIntoTry resume → InvalidProgramException).
    // Fixed in #914 by delegating the walker to the canonical ExprContainsSuspension
    // (the CompoundSetIndex emission already spilled the receiver/index correctly).
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncTry_AwaitInCompoundIndexedAssign(ExecutionMode mode)
    {
        // arr[0] += await ... exercises CompoundAssign + GetIndex + SetIndex.
        var source = """
            async function inc(): Promise<number> { return 5; }
            async function main(): Promise<void> {
                const arr = [10];
                try {
                    arr[0] += await inc();
                    console.log("arr0: " + arr[0]);
                } catch (e) {
                    console.log("caught");
                }
            }
            main();
            """;
        Assert.Equal("arr0: 15\n", TestHarness.Run(source, mode));
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncTry_AwaitInIndexExpression(ExecutionMode mode)
    {
        // arr[await ...] exercises GetIndex with the await in the index.
        var source = """
            async function idx(): Promise<number> { return 2; }
            async function main(): Promise<void> {
                const arr = [10, 20, 30];
                try {
                    const v = arr[await idx()];
                    console.log("v: " + v);
                } catch (e) {
                    console.log("caught");
                }
            }
            main();
            """;
        Assert.Equal("v: 30\n", TestHarness.Run(source, mode));
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncTry_AwaitInsideLabeledStatement(ExecutionMode mode)
    {
        // The labeled loop wraps the await; pre-fix the missing LabeledStatement
        // arm short-circuited before descending into the loop body.
        var source = """
            async function val(): Promise<number> { return 7; }
            async function main(): Promise<void> {
                try {
                    outer: for (let i = 0; i < 1; i++) {
                        const v = await val();
                        console.log("labeled: " + v);
                    }
                } catch (e) {
                    console.log("caught");
                }
            }
            main();
            """;
        Assert.Equal("labeled: 7\n", TestHarness.Run(source, mode));
    }

    // The walker arms below were all absent before #914 (only CompoundAssign/GetIndex/SetIndex
    // had been added). Each puts an await inside a composite the walker must descend into, within
    // a try; under-reporting any of them would re-expose the BranchIntoTry resume failure.

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncTry_AwaitInCompoundMemberAssign(ExecutionMode mode)
    {
        // obj.x += await ... is Expr.CompoundSet.
        var source = """
            async function inc(): Promise<number> { return 5; }
            async function main(): Promise<void> {
                const obj = { x: 10 };
                try {
                    obj.x += await inc();
                    console.log("x: " + obj.x);
                } catch (e) {
                    console.log("caught");
                }
            }
            main();
            """;
        Assert.Equal("x: 15\n", TestHarness.Run(source, mode));
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncTry_AwaitInLogicalIndexedAssign(ExecutionMode mode)
    {
        // arr[0] ||= await ... is Expr.LogicalSetIndex; arr[0] is falsy so the RHS await runs.
        var source = """
            async function inc(): Promise<number> { return 5; }
            async function main(): Promise<void> {
                const arr = [0];
                try {
                    arr[0] ||= await inc();
                    console.log("arr0: " + arr[0]);
                } catch (e) {
                    console.log("caught");
                }
            }
            main();
            """;
        Assert.Equal("arr0: 5\n", TestHarness.Run(source, mode));
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncTry_AwaitInArrayLiteral(ExecutionMode mode)
    {
        // [await ...] is Expr.ArrayLiteral with a suspending element.
        var source = """
            async function val(): Promise<number> { return 3; }
            async function main(): Promise<void> {
                try {
                    const a = [await val(), 9];
                    console.log("a: " + a[0] + "," + a[1]);
                } catch (e) {
                    console.log("caught");
                }
            }
            main();
            """;
        Assert.Equal("a: 3,9\n", TestHarness.Run(source, mode));
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncTry_AwaitInTemplateLiteral(ExecutionMode mode)
    {
        // `v=${await ...}` is Expr.TemplateLiteral with a suspending interpolation.
        var source = """
            async function val(): Promise<number> { return 4; }
            async function main(): Promise<void> {
                try {
                    const s = `v=${await val()}`;
                    console.log(s);
                } catch (e) {
                    console.log("caught");
                }
            }
            main();
            """;
        Assert.Equal("v=4\n", TestHarness.Run(source, mode));
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncTry_ThrowAwait_RunsOwnFinallyThenCatch(ExecutionMode mode)
    {
        // The routed throw lands in this try's catch and its own finally still runs after (#914).
        var source = """
            async function makeErr(): Promise<string> { return "boom"; }
            async function main(): Promise<void> {
                try {
                    throw await makeErr();
                } catch (e) {
                    console.log("caught: " + e);
                } finally {
                    console.log("finally");
                }
            }
            main();
            """;
        Assert.Equal("caught: boom\nfinally\n", TestHarness.Run(source, mode));
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsyncTry_ThrowAwait_NestedInIf_RoutesToCatch(ExecutionMode mode)
    {
        // The `if` is the segment-breaker (its branch contains the await); the throw is emitted while
        // descending into it, still at the try-body top level — routing must fire there too.
        var source = """
            async function makeErr(): Promise<string> { return "deep"; }
            async function main(): Promise<void> {
                try {
                    if (1 < 2) {
                        throw await makeErr();
                    }
                    console.log("unreached");
                } catch (e) {
                    console.log("caught: " + e);
                }
            }
            main();
            """;
        Assert.Equal("caught: deep\n", TestHarness.Run(source, mode));
    }
}

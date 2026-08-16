using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests.NarrowingTests;

/// <summary>
/// A reassignment that takes a variable OUT of its <c>if</c>-guard narrowing must widen that
/// narrowing for later statements, even when the reassignment is in a nested branch (#570). The gap:
/// <c>if</c>-guard variable narrowing is applied by redefining the variable in the guard's child
/// <see cref="SharpTS.TypeSystem.TypeEnvironment"/>; a reassignment inside a further-nested block ran
/// against a deeper env that is discarded at the join, so the outer narrowing wrongly survived and
/// SharpTS accepted code <c>tsc</c> rejects.
///
/// The fix gates invalidation on whether the assigned value escapes the current narrowing — so a
/// reassignment that STAYS within the narrowing keeps it (matching <c>tsc</c>, and incidentally
/// fixing the symmetric loop case where a reassign-to-narrow used to over-widen a still-valid
/// loop-condition narrowing).
/// </summary>
public class NestedReassignmentNarrowingTests
{
    // ---- #570: nested-branch reassignment OUT of the narrowing widens the outer guard ----

    [Fact]
    public void IfGuard_NestedUnguardedReassignToExcluded_WidensNarrowing()
    {
        // The verbatim #570 repro. `x` is narrowed to string by the guard; the nested `if (foo)`
        // reassigns it to undefined, so at `return x.length` it is `string | undefined` again.
        var source = """
            function g(x: string | undefined, foo: boolean): number {
                if (x !== undefined) {
                    if (foo) { x = undefined; }
                    return x.length;
                }
                return 0;
            }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void IfGuard_NestedGuardedReassignToExcluded_WidensNarrowing()
    {
        // Sibling shape: the reassignment sits inside a nested guard on a DIFFERENT variable. The
        // outer narrowing of `x` must still widen at the join.
        var source = """
            function g(x: string | undefined, y: string | undefined): number {
                if (x !== undefined) {
                    if (y !== undefined) { x = undefined; }
                    return x.length;
                }
                return 0;
            }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void IfGuard_NestedReassignWithinNarrowing_KeepsNarrowing()
    {
        // No regression: reassigning to a value the guard still admits (a string) keeps `x` narrowed
        // to string, so `x.length` is fine — matching tsc, which narrows `x` to the assigned type.
        var source = """
            function g(x: string | undefined, cond: boolean): number {
                if (x !== undefined) {
                    if (cond) { x = "other"; }
                    return x.length;
                }
                return 0;
            }
            console.log(g("hello", true));
            """;
        Assert.Equal("5\n", TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void IfGuard_DirectReassignToExcluded_StillWidens()
    {
        // The same-scope case was already handled; guard against a fix that only patched the nested
        // path and broke the direct one.
        var source = """
            function g(x: string | undefined): number {
                if (x !== undefined) {
                    x = undefined;
                    return x.length;
                }
                return 0;
            }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    // ---- the symmetric loop case: reassign-to-narrow keeps the loop-condition narrowing ----

    [Fact]
    public void LoopGuard_NestedReassignWithinNarrowing_KeepsNarrowing()
    {
        // The loop-condition narrowing re-holds each iteration; a body reassignment that stays within
        // it must not widen subsequent uses. Before the gated invalidation this over-widened and the
        // `x.length` was wrongly rejected.
        var source = """
            function h(x: string | undefined, cond: boolean): number {
                while (x !== undefined) {
                    if (cond) { x = "other"; }
                    return x.length;
                }
                return 0;
            }
            console.log(h("hi", false));
            """;
        Assert.Equal("2\n", TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void LoopGuard_NestedReassignToExcluded_StillWidens()
    {
        // The soundness direction stays intact for loops: a reassignment to undefined widens, so the
        // post-reassignment `x.length` is rejected.
        var source = """
            function h(x: string | undefined, cond: boolean): number {
                while (x !== undefined) {
                    if (cond) { x = undefined; }
                    return x.length;
                }
                return 0;
            }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    // ---- #654: nested guards on the SAME variable must widen EVERY enclosing narrowing ----

    [Fact]
    public void IfGuard_NestedSameVarGuardReassignToExcluded_WidensOuterNarrowing()
    {
        // The verbatim #654 repro. Both guards narrow `x`; the inner reassignment to undefined must
        // widen the OUTER guard's narrowing too, so the read after the inner block is rejected.
        // Before the fix WidenEnclosingNarrowing only widened the nearest (inner) guard, leaving the
        // outer narrowing stale — SharpTS accepted code tsc rejects.
        var source = """
            function f(x: string | undefined): void {
                if (x !== undefined) {
                    if (x !== "foo") { x = undefined; }
                    x.length;
                }
            }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void IfGuard_ThreeLevelSameVarGuards_WidenAtEveryOuterLevel()
    {
        // Three guards deep on the same variable: an escaping reassignment at the deepest level must
        // widen the narrowing at BOTH shallower levels. The read sits at the middle level here.
        var source = """
            function f(x: string | undefined): number {
                if (x !== undefined) {
                    if (x !== "a") {
                        if (x !== "b") { x = undefined; }
                        return x.length;
                    }
                    return x.length;
                }
                return 0;
            }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void IfGuard_ShadowingSameNameVar_DoesNotWidenOuterVariable()
    {
        // No regression from the widen-EVERY-scope change: an inner block that SHADOWS `x` with its
        // own binding and reassigns it must not widen the OUTER `x`'s guard narrowing. The widening
        // walk must stop at the inner declaration. Outer `x` stays string, so `x.length` is fine.
        var source = """
            function f(x: string | undefined): number {
                if (x !== undefined) {
                    {
                        let x: string | number = "y";
                        if (typeof x === "string") { x = 42; }
                        void x;
                    }
                    return x.length;
                }
                return 0;
            }
            console.log(f("hello"));
            """;
        Assert.Equal("5\n", TestHarness.RunInterpreted(source));
    }
}

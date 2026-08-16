using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests.NarrowingTests;

/// <summary>
/// Tests for flow narrowing across VARIABLE assignments (issue #653) — the symmetric companion to
/// the property-write narrowing of #48 (see <see cref="AssignmentFlowNarrowingTests"/>). TypeScript's
/// CFA narrows a reference to the assigned value's type after a write, so <c>x = "s"</c> on a
/// <c>string | null</c> variable lets the subsequent <c>x.length</c> type-check. SharpTS previously
/// restored the full declared type after every variable assignment, rejecting code <c>tsc</c> accepts.
///
/// The narrowed type is the declared union filtered to the members the RHS can be
/// (<c>NarrowToDeclaredSlot</c>), so literal members survive and the declared type is the fallback
/// when nothing narrows. Both function-local/parameter and module/top-level variables are tracked in
/// the declared-type stack (#743 pushes a module frame in <c>Check</c>/<c>CheckWithRecovery</c>), so
/// post-write narrowing applies at module scope too — assignments are still checked against the
/// declared type, not the narrowed one.
/// </summary>
public class VariableAssignmentFlowNarrowingTests
{
    [Fact]
    public void Reassign_WriteThenRead_NarrowsToRhsType()
    {
        // Primary reproduction from issue #653.
        var source = """
            function f(x: string | null): number {
                x = "hi";
                return x.length;
            }
            console.log(f(null));
            """;

        Assert.Equal("2\n", TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void Reassign_InGuardRecoveryPath_NarrowsToRhsType()
    {
        // Second #653 repro: reassigning inside the branch that recovers from `undefined`.
        var source = """
            function f(x: string | undefined): number {
                if (x === undefined) {
                    x = "recovered";
                    return x.length;
                }
                return x.length;
            }
            console.log(f(undefined));
            console.log(f("hello"));
            """;

        Assert.Equal("9\n5\n", TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void Reassign_CompatibleValueInsideGuard_KeepsNarrowing()
    {
        // Reassigning to a value the guard still admits keeps `x` narrowed to string, so `x.length`
        // type-checks in the same branch (previously rejected by the restore-to-declared).
        var source = """
            function f(x: string | null): number {
                if (x !== null) {
                    x = "hi";
                    return x.length;
                }
                return 0;
            }
            console.log(f("a"));
            """;

        Assert.Equal("2\n", TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void Reassign_LiteralUnion_PreservesLiteralMember()
    {
        // The narrowed type is the surviving declared member, so a literal-union declared type keeps
        // the assigned literal: `x = "a"` narrows `x` to `"a"`, assignable to an `"a"` return slot.
        var source = """
            function f(x: "a" | "b" | null): "a" {
                x = "a";
                return x;
            }
            console.log(f(null));
            """;

        Assert.Equal("a\n", TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void Reassign_DeclaredNonUnion_NoOp()
    {
        // When the declared type isn't a union there's nothing to narrow; still type-checks and runs.
        var source = """
            function f(x: string): number {
                x = "hello";
                return x.length;
            }
            console.log(f("world"));
            """;

        Assert.Equal("5\n", TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void Reassign_AcrossUnionMembers_ChecksAgainstDeclaredNotNarrowed()
    {
        // After `x = "hi"` narrows `x` to string, a later `x = 42` must still be allowed: the
        // assignment is checked against the DECLARED type, not the narrowed one.
        var source = """
            function f(): string | number {
                let x: string | number = "s";
                x = "hi";
                x = 42;
                return x;
            }
            console.log(f());
            """;

        Assert.Equal("42\n", TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void Reassign_NullValue_StaysNullableAndRejectsNonNullSlot()
    {
        // Writing `null` narrows the variable to bare `null` (#742). Returning it from a non-nullable
        // `string` function is therefore still rejected (`null` is not assignable to `string`).
        var source = """
            function f(x: string | null): string {
                x = null;
                return x;
            }
            """;

        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("null", ex.Message);
    }

    [Fact]
    public void Reassign_NullThenAccess_StillRejected()
    {
        // Soundness: `x = null` followed by a property access must still error. The variable narrows
        // to bare `null` (#742) and `x.length` is flagged directly as access on a possibly-null type.
        var source = """
            function f(x: string | null): number {
                x = null;
                return x.length;
            }
            """;

        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void Reassign_RhsSpansFullUnion_DoesNotNarrow()
    {
        // If the RHS can be the whole declared union, there is no narrowing benefit — the unguarded
        // read still errors (matching the property analog WriteOfNullableRhs_DoesNotNarrow).
        var source = """
            function getNullable(): string | null { return null; }
            function f(x: string | null): number {
                x = getNullable();
                return x.length;
            }
            """;

        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("null", ex.Message);
    }

    [Fact]
    public void Reassign_ConditionalEscapeToOtherMember_StaysSound()
    {
        // Soundness guard: a CONDITIONAL reassignment that escapes the guard narrowing to a different
        // member must not let the post-branch read assume the assigned member. After the `if`, `x` is
        // `string | number`, so `x.toFixed` (string has none) is correctly rejected — the narrowing
        // must not persist past the join.
        var source = """
            function f(x: string | undefined | number, cond: boolean): void {
                if (typeof x === "string") {
                    if (cond) { x = 42; }
                    x.toFixed(2);
                }
            }
            """;

        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void ModuleScope_GuardThenReassignToOtherMember_Allowed()
    {
        // #743 Bug 1: at module scope `g = 42` was checked against the guard-narrowed `string` instead
        // of the declared `string | number`, producing a spurious "Cannot assign" error. tsc accepts
        // this — the assignment is checked against the DECLARED type. The same code in a function
        // already worked because function locals were tracked in the declared-type stack.
        var source = """
            let g: string | number = "s";
            if (typeof g === "string") {
                g = 42;
            }
            console.log(g);
            """;

        Assert.Equal("42\n", TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void ModuleScope_WriteThenRead_NarrowsToRhsType()
    {
        // #743 Bug 2: post-write narrowing (#653) is gated on tracked variables, so module-level
        // `x = "hi"` previously left `x` at `string | null` and `x.length` reported a false
        // "possibly null". With the module declared-type frame, the write narrows `x` to string.
        var source = """
            let x: string | null = null;
            x = "hi";
            console.log(x.length);
            """;

        Assert.Equal("2\n", TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void ModuleScope_AssignOutsideDeclaredType_StillRejected()
    {
        // Regression guard: tracking module vars must not weaken assignment checking. An assignment
        // outside the declared type is still rejected (checked against the declared type, which is now
        // correctly recorded rather than read back from a narrowed binding).
        var source = """
            let n: number = 1;
            n = "s";
            """;

        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }
}

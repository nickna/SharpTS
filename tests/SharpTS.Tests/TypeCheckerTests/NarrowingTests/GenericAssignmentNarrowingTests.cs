using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests.NarrowingTests;

/// <summary>
/// Narrowing of a reference whose declared type is a bare type parameter, by assignment
/// (issue #337, item 3). tsc treats the constraint as the narrowing domain: after
/// <c>x = y</c> where <c>x: T</c> (T constrained) and <c>y</c> is a deferred conditional
/// like <c>NonNullable&lt;T&gt;</c>, <c>x</c> reads as that conditional's concrete base type,
/// so a subsequent <c>let s: string = x</c> type-checks. An <em>unconstrained</em> <c>T</c>
/// yields no base type and is left alone, and a bare-type-parameter RHS is never reduced to
/// its constraint (which would break in-chain assignments).
/// </summary>
public class GenericAssignmentNarrowingTests
{
    [Fact]
    public void ConstrainedTypeParam_AssignedNonNullable_NarrowsToBaseType()
    {
        // x: T (T extends string | undefined), y: NonNullable<T>.
        // After `x = y`, x reads as `string`, so `let s1: string = x` must NOT error.
        var source = """
            function f<T extends string | undefined>(x: T, y: NonNullable<T>) {
                x = y;
                let s1: string = x;
            }
            console.log("ok");
            """;

        Assert.Equal("ok\n", TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void UnconstrainedTypeParam_AssignedNonNullable_StillErrors()
    {
        // f1 from conditionalTypes1.ts: T is unconstrained, so NonNullable<T> has no concrete
        // base type. x stays `T` and `let s1: string = x` must STILL be a type error.
        var source = """
            function f<T>(x: T, y: NonNullable<T>) {
                x = y;
                let s1: string = x;
            }
            console.log("ok");
            """;

        Assert.ThrowsAny<SharpTS.TypeSystem.Exceptions.TypeCheckException>(
            () => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void ConstrainedTypeParam_AssignBackToConditional_StillErrors()
    {
        // f2's `y = x` line: even after `x = y` narrows x, assigning x back into the
        // NonNullable<T> slot is unsound (x's base type `string` isn't assignable to a bare
        // type parameter), so it must still error.
        var source = """
            function f<T extends string | undefined>(x: T, y: NonNullable<T>) {
                x = y;
                y = x;
            }
            console.log("ok");
            """;

        Assert.ThrowsAny<SharpTS.TypeSystem.Exceptions.TypeCheckException>(
            () => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void TypeParamChain_AssignedBareTypeParam_NotReducedToConstraint()
    {
        // Regression guard (typeParameterAssignability2/3): a bare-type-parameter RHS must not
        // be collapsed to its constraint. After `u = t`, a later `v = u` must still type-check
        // because u stays within the T extends U extends V chain (not flattened to Date).
        var source = """
            function foo<T extends U, U extends V, V extends Date>(t: T, u: U, v: V) {
                u = t;
                v = u;
                v = t;
            }
            console.log("ok");
            """;

        Assert.Equal("ok\n", TestHarness.RunInterpreted(source));
    }
}

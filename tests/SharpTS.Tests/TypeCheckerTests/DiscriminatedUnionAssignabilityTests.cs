using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Discriminated-union assignability (tsc's typeRelatedToDiscriminatedType, pinned by
/// assignmentCompatWithDiscriminatedUnion): a source whose discriminant properties are unions
/// of unit types relates to a union target when every discriminant combination lands on an
/// assignable constituent.
/// </summary>
public class DiscriminatedUnionAssignabilityTests
{
    [Fact]
    public void BooleanDiscriminant_CoversBothConstituents()
    {
        // IteratorResult shape: done: boolean splits across done: true / done: false.
        TestHarness.RunInterpreted("""
            type S = { done: boolean, value: number };
            type T = { done: true, value: number } | { done: false, value: number };
            declare let s: S;
            declare let t: T;
            t = s;
            """);
    }

    [Fact]
    public void LiteralUnionDiscriminant_MatchingCombinations_Assignable()
    {
        TestHarness.RunInterpreted("""
            type S = { a: 0 | 2, b: 4 };
            type T = { a: 0, b: 1 | 4 } | { a: 1, b: 2 } | { a: 2, b: 3 | 4 };
            declare let s: S;
            declare let t: T;
            t = s;
            """);
    }

    [Fact]
    public void UnmatchedCombination_Rejected()
    {
        // a=2 lands on { a: 2, b: 3 } whose b rejects 4 — the combination has no home.
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            type S = { a: 0 | 2, b: 4 };
            type T = { a: 0, b: 1 | 4 } | { a: 1, b: 2 | 4 } | { a: 2, b: 3 };
            declare let s: S;
            declare let t: T;
            t = s;
            """));
    }

    [Fact]
    public void MissingNonDiscriminantMember_Rejected()
    {
        // a=2 matches { a: 2, b: 3 | 4, c: string } on discriminants but S lacks 'c'.
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            type S = { a: 0 | 2, b: 4 };
            type T = { a: 0, b: 1 | 4 } | { a: 1, b: 2 } | { a: 2, b: 3 | 4, c: string };
            declare let s: S;
            declare let t: T;
            t = s;
            """));
    }

    [Fact]
    public void UnionPropertyWrite_AcceptsUnionOfMemberTypes()
    {
        TestHarness.RunInterpreted("""
            interface ILinearAxis { type: "linear"; }
            interface ICategoricalAxis { type: "categorical"; }
            type IAxis = ILinearAxis | ICategoricalAxis;
            function getAxisType(): "linear" | "categorical" { return "categorical"; }
            const good: IAxis = { type: "linear" };
            good.type = getAxisType();
            """);
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            interface ILinearAxis { type: "linear"; }
            interface ICategoricalAxis { type: "categorical"; }
            type IAxis = ILinearAxis | ICategoricalAxis;
            const good: IAxis = { type: "linear" };
            good.type = "diagonal";
            """));
    }

    [Fact]
    public void SameNamedAliases_InDifferentNamespaces_ExpandIndependently()
    {
        // The alias expansion cache must not serve one namespace's T for another's —
        // the second namespace's t = s is a genuine error.
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            namespace First {
                type S = { a: 0 | 2, b: 4 };
                type T = { a: 0, b: 1 | 4 } | { a: 1, b: 2 } | { a: 2, b: 3 | 4 };
                declare let s: S;
                declare let t: T;
                t = s;
            }
            namespace Second {
                type S = { a: 0 | 2, b: 4 };
                type T = { a: 0, b: 1 | 4 } | { a: 1, b: 2 | 4 } | { a: 2, b: 3 };
                declare let s: S;
                declare let t: T;
                t = s;
            }
            """));
    }
}

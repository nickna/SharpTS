using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests.NarrowingTests;

/// <summary>
/// Literal equality narrowing (tsc's narrowTypeByLiteralExpression): `x === "a"` narrows
/// x to "a" in the true branch, and `x === "a" || x === "b"` narrows to "a" | "b" — the
/// ||-merge applies when both disjuncts guard the same path. Pinned by the GH39357 case
/// in assignmentCompatWithDiscriminatedUnion.
/// </summary>
public class LiteralEqualityNarrowingTests
{
    [Fact]
    public void StringLiteralEquality_NarrowsInIfBranch()
    {
        TestHarness.RunInterpreted("""
            function f(b: "a" | "b" | "c") {
                if (b === "a") {
                    const x: "a" = b;
                }
            }
            f("a");
            """);
    }

    [Fact]
    public void StringLiteralEquality_NarrowsElseBranch()
    {
        TestHarness.RunInterpreted("""
            function f(b: "a" | "b" | "c") {
                if (b === "a") { } else {
                    const x: "b" | "c" = b;
                }
            }
            f("b");
            """);
    }

    [Fact]
    public void StringLiteralInequality_NarrowsTrueBranch()
    {
        TestHarness.RunInterpreted("""
            function f(b: "a" | "b" | "c") {
                if (b !== "a") {
                    const x: "b" | "c" = b;
                } else {
                    const y: "a" = b;
                }
            }
            f("c");
            """);
    }

    [Fact]
    public void OrChainedEquality_NarrowsToUnionInTrueBranch()
    {
        TestHarness.RunInterpreted("""
            function f(b: "a" | "b" | "c") {
                if (b === "a" || b === "b") {
                    const x: "a" | "b" = b;
                } else {
                    const y: "c" = b;
                }
            }
            f("a");
            """);
    }

    [Fact]
    public void OrChainedEquality_NarrowsTernaryBranches()
    {
        TestHarness.RunInterpreted("""
            function f(b: "a" | "b" | "c"): "a" | "b" {
                return b === "a" || b === "b" ? b : "a";
            }
            console.log(f("b"));
            """);
    }

    [Fact]
    public void OrOverDifferentVariables_DoesNotNarrowTrueBranch()
    {
        // Either disjunct may be the true one, so neither variable narrows.
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            function f(a: "x" | "y", b: "x" | "y") {
                if (a === "x" || b === "x") {
                    const t: "x" = a;
                }
            }
            f("x", "y");
            """));
    }

    [Fact]
    public void NumberLiteralEquality_Narrows()
    {
        TestHarness.RunInterpreted("""
            function f(n: 0 | 1 | 2) {
                if (n === 0 || n === 2) {
                    const x: 0 | 2 = n;
                } else {
                    const y: 1 = n;
                }
            }
            f(1);
            """);
    }

    [Fact]
    public void BooleanLiteralEquality_Narrows()
    {
        TestHarness.RunInterpreted("""
            function f(v: boolean | string) {
                if (v === true) {
                    const t: true = v;
                }
            }
            f("s");
            """);
    }

    [Fact]
    public void GeneralString_NarrowsToLiteralInTrueBranch()
    {
        TestHarness.RunInterpreted("""
            function f(s: string) {
                if (s === "hi") {
                    const t: "hi" = s;
                }
                const after: string = s;
            }
            f("x");
            """);
    }

    [Fact]
    public void EqualityAgainstAbsentLiteral_StillRejectsWrongAssignment()
    {
        // Narrowing must not loosen the unrelated branch: b stays "b" | "c" in else.
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            function f(b: "a" | "b" | "c") {
                if (b === "a") { } else {
                    const x: "a" = b;
                }
            }
            f("b");
            """));
    }

    [Fact]
    public void NarrowedValueFlowsAtRuntime()
    {
        var result = TestHarness.RunInterpreted("""
            function pick(b: "a" | "b" | "c"): string {
                return b === "a" || b === "b" ? b.toUpperCase() : b;
            }
            console.log(pick("a"));
            console.log(pick("c"));
            """);
        Assert.Equal("A\nc\n", result);
    }
}

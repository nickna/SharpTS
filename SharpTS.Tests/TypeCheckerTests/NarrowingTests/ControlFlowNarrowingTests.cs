using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests.NarrowingTests;

/// <summary>
/// Tests for CFG-based type narrowing across complex control flow.
/// These tests verify that type narrowings propagate correctly through:
/// - Early returns
/// - Logical operators (&&, ||)
/// - Ternary expressions
/// - Loop conditions
/// </summary>
public class ControlFlowNarrowingTests
{
    #region Early Return Narrowing

    [Fact]
    public void EarlyReturn_NarrowsAfterReturn()
    {
        // After early return, narrowing should apply
        var source = """
            function test(x: string | null): string {
                if (x === null) {
                    return "was null";
                }
                return x;  // x should be narrowed to string
            }
            console.log(test("hello"));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\n", result);
    }

    [Fact]
    public void EarlyThrow_NarrowsAfterThrow()
    {
        // After early throw, narrowing should apply
        var source = """
            function test(x: string | null): string {
                if (x === null) {
                    throw new Error("x is null");
                }
                return x;  // x should be narrowed to string
            }
            console.log(test("hello"));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\n", result);
    }

    [Fact]
    public void PropertyNarrowing_EarlyReturn()
    {
        // Property narrowing with early return
        var source = """
            type Obj = { prop: string | null };
            function test(obj: Obj): string {
                if (obj.prop === null) {
                    return "was null";
                }
                return obj.prop;  // obj.prop should be narrowed to string
            }
            console.log(test({ prop: "hello" }));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\n", result);
    }

    #endregion

    #region Logical AND Narrowing

    [Fact]
    public void LogicalAnd_NarrowsRightSide()
    {
        // In `x !== null && expr`, x is narrowed in expr
        var source = """
            function test(x: string | null): number {
                const len = x !== null && x.length;
                return len || 0;
            }
            console.log(test("hello"));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("5\n", result);
    }

    [Fact]
    public void LogicalAnd_PropertyNarrowing()
    {
        // In `obj.prop !== null && expr`, obj.prop is narrowed in expr
        var source = """
            type Obj = { prop: string | null };
            function test(obj: Obj): number {
                const len = obj.prop !== null && obj.prop.length;
                return len || 0;
            }
            console.log(test({ prop: "hello" }));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("5\n", result);
    }

    #endregion

    #region Logical OR Narrowing

    [Fact]
    public void LogicalOr_NarrowsRightSide()
    {
        // In `x === null || expr`, x is narrowed to non-null in expr
        var source = """
            function test(x: string | null): string {
                x === null || console.log(x.length);
                return "done";
            }
            console.log(test("hello"));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("5\ndone\n", result);
    }

    #endregion

    #region Ternary Expression Narrowing

    [Fact]
    public void Ternary_NarrowsInBranches()
    {
        // In `x !== null ? x.length : 0`, x is narrowed in the true branch
        var source = """
            function test(x: string | null): number {
                return x !== null ? x.length : 0;
            }
            console.log(test("hello"));
            console.log(test(null));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("5\n0\n", result);
    }

    [Fact]
    public void Ternary_PropertyNarrowing()
    {
        // Property narrowing in ternary branches
        var source = """
            type Obj = { prop: string | null };
            function test(obj: Obj): number {
                return obj.prop !== null ? obj.prop.length : 0;
            }
            console.log(test({ prop: "hello" }));
            console.log(test({ prop: null }));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("5\n0\n", result);
    }

    #endregion

    #region Nullish Coalescing

    [Fact]
    public void NullishCoalescing_ImplicitNarrowing()
    {
        // ?? implies the left side is checked for null/undefined
        var source = """
            function test(x: string | null): string {
                return x ?? "default";
            }
            console.log(test("hello"));
            console.log(test(null));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\ndefault\n", result);
    }

    #endregion

    #region Multiple Conditions

    [Fact]
    public void MultipleNullChecks_NarrowsBoth()
    {
        // Multiple null checks narrow both variables
        var source = """
            function test(x: string | null, y: string | null): string {
                if (x !== null && y !== null) {
                    return x + y;
                }
                return "one is null";
            }
            console.log(test("hello", " world"));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("hello world\n", result);
    }

    [Fact]
    public void NestedIf_NarrowingAccumulates()
    {
        // Nested if statements accumulate narrowings
        var source = """
            type Obj = { a: string | null; b: string | null };
            function test(obj: Obj): string {
                if (obj.a !== null) {
                    if (obj.b !== null) {
                        return obj.a + obj.b;
                    }
                    return obj.a;
                }
                return "a is null";
            }
            console.log(test({ a: "hello", b: " world" }));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("hello world\n", result);
    }

    #endregion

    #region Loop Narrowing

    [Fact]
    public void WhileCondition_NarrowsInBody()
    {
        // While condition narrows in the body
        var source = """
            function test(x: string | null): number {
                let count = 0;
                while (x !== null && count < 3) {
                    console.log(x.length);  // x is narrowed in body
                    count++;
                }
                return count;
            }
            console.log(test("hi"));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("2\n2\n2\n3\n", result);
    }

    #endregion
}

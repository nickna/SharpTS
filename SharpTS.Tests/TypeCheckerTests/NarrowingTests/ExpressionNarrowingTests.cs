using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests.NarrowingTests;

/// <summary>
/// Tests for expression-level type narrowing (Phase 4).
/// These tests verify that type narrowings are applied within:
/// - Logical AND (&&) expressions
/// - Logical OR (||) expressions
/// - Ternary (?:) expressions
/// - Nullish coalescing (??) expressions
/// </summary>
public class ExpressionNarrowingTests
{
    #region Logical AND (&&) Expression Narrowing

    [Fact]
    public void LogicalAnd_VariableNarrowing_AccessesProperty()
    {
        // x !== null && x.length should narrow x to string in the right operand
        var source = """
            function test(x: string | null): number | boolean {
                return x !== null && x.length;
            }
            console.log(test("hello"));
            console.log(test(null));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("5\nfalse\n", result);
    }

    [Fact]
    public void LogicalAnd_PropertyNarrowing_AccessesNestedProperty()
    {
        // obj.prop !== null && obj.prop.length
        var source = """
            type Obj = { prop: string | null };
            function test(obj: Obj): number | boolean {
                return obj.prop !== null && obj.prop.length;
            }
            console.log(test({ prop: "hello" }));
            console.log(test({ prop: null }));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("5\nfalse\n", result);
    }

    [Fact]
    public void LogicalAnd_MultipleNarrowings()
    {
        // x !== null && y !== null && (x + y) should narrow both
        var source = """
            function test(x: string | null, y: string | null): string | boolean {
                return x !== null && y !== null && (x + y);
            }
            console.log(test("hello", " world"));
            console.log(test(null, "world"));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("hello world\nfalse\n", result);
    }

    [Fact]
    public void LogicalAnd_ChainedAccess()
    {
        // Narrowing should allow chained property access
        var source = """
            type Node = { value: string; next: Node | null };
            function test(node: Node): string | boolean {
                return node.next !== null && node.next.value;
            }
            const node: Node = { value: "a", next: { value: "b", next: null } };
            console.log(test(node));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("b\n", result);
    }

    #endregion

    #region Logical OR (||) Expression Narrowing

    [Fact]
    public void LogicalOr_NarrowsToExcludedType()
    {
        // x === null || x.length should narrow x to string in the right operand
        var source = """
            function test(x: string | null): number | boolean {
                return x === null || x.length;
            }
            console.log(test("hello"));
            console.log(test(null));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("5\ntrue\n", result);
    }

    [Fact]
    public void LogicalOr_PropertyNarrowing()
    {
        // obj.prop === null || obj.prop.length
        var source = """
            type Obj = { prop: string | null };
            function test(obj: Obj): number | boolean {
                return obj.prop === null || obj.prop.length;
            }
            console.log(test({ prop: "hello" }));
            console.log(test({ prop: null }));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("5\ntrue\n", result);
    }

    [Fact]
    public void LogicalOr_DefaultValue()
    {
        // Common pattern: x === null || console.log(x)
        var source = """
            function test(x: string | null): void {
                x === null || console.log(x.toUpperCase());
            }
            test("hello");
            test(null);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("HELLO\n", result);
    }

    #endregion

    #region Ternary Expression Narrowing

    [Fact]
    public void Ternary_NarrowsThenBranch()
    {
        // x !== null ? x.length : 0 should narrow x to string in the then branch
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
    public void Ternary_NarrowsElseBranch()
    {
        // x === null ? "null" : x.toUpperCase() should narrow x in else branch
        var source = """
            function test(x: string | null): string {
                return x === null ? "null" : x.toUpperCase();
            }
            console.log(test("hello"));
            console.log(test(null));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("HELLO\nnull\n", result);
    }

    [Fact]
    public void Ternary_PropertyNarrowing()
    {
        // obj.prop !== null ? obj.prop.length : 0
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

    [Fact]
    public void Ternary_NestedNarrowing()
    {
        // Nested ternary with narrowing
        var source = """
            function test(x: string | null, y: string | null): string {
                return x !== null
                    ? (y !== null ? x + y : x)
                    : "both null";
            }
            console.log(test("hello", " world"));
            console.log(test("hello", null));
            console.log(test(null, "world"));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("hello world\nhello\nboth null\n", result);
    }

    #endregion

    #region Nullish Coalescing (??) Narrowing

    [Fact]
    public void NullishCoalescing_ProvidesDefault()
    {
        // x ?? "default" handles null
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

    [Fact]
    public void NullishCoalescing_PropertyAccess()
    {
        // obj.prop ?? "default"
        var source = """
            type Obj = { prop: string | null };
            function test(obj: Obj): string {
                return obj.prop ?? "default";
            }
            console.log(test({ prop: "hello" }));
            console.log(test({ prop: null }));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\ndefault\n", result);
    }

    #endregion

    #region Combined Expression Patterns

    [Fact]
    public void CombinedPatterns_AndWithTernary()
    {
        // Combining && with ternary
        var source = """
            function test(x: string | null, flag: boolean): string {
                return flag && x !== null ? x.toUpperCase() : "default";
            }
            console.log(test("hello", true));
            console.log(test("hello", false));
            console.log(test(null, true));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("HELLO\ndefault\ndefault\n", result);
    }

    [Fact]
    public void CombinedPatterns_OrWithNullishCoalescing()
    {
        // Combining || with ??
        var source = """
            function test(x: string | null): string {
                return (x ?? "").toUpperCase() || "empty";
            }
            console.log(test("hello"));
            console.log(test(null));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("HELLO\nempty\n", result);
    }

    #endregion
}

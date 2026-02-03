using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests.NarrowingTests;

/// <summary>
/// Tests for property-based type narrowing.
/// These tests verify that type guards like `if (obj.prop !== null)` correctly narrow
/// the type of the property within the guarded scope.
/// </summary>
public class PropertyNarrowingTests
{
    #region Simple Property Narrowing

    [Fact]
    public void SimplePropertyNullCheck_NarrowsInThenBranch()
    {
        var source = """
            type Obj = { prop: string | null };
            function test(obj: Obj): string {
                if (obj.prop !== null) {
                    return obj.prop;  // Should be narrowed to string
                }
                return "default";
            }
            console.log(test({ prop: "hello" }));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\n", result);
    }

    [Fact]
    public void SimplePropertyNullCheck_NarrowsInElseBranch()
    {
        var source = """
            type Obj = { prop: string | null };
            function test(obj: Obj): string {
                if (obj.prop === null) {
                    return "was null";
                }
                return obj.prop;  // Should be narrowed to string in else branch
            }
            console.log(test({ prop: "hello" }));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\n", result);
    }

    #endregion

    #region Nested Property Narrowing (Phase 1 feature)

    [Fact]
    public void NestedPropertyNullCheck_TwoLevels()
    {
        var source = """
            type Inner = { value: string | null };
            type Outer = { inner: Inner };

            function test(obj: Outer): string {
                if (obj.inner.value !== null) {
                    return obj.inner.value;  // Should be narrowed to string
                }
                return "default";
            }
            console.log(test({ inner: { value: "nested" } }));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("nested\n", result);
    }

    [Fact]
    public void NestedPropertyNullCheck_ThreeLevels()
    {
        var source = """
            type Deep = { a: { b: { c: string | null } } };

            function test(obj: Deep): string {
                if (obj.a.b.c !== null) {
                    return obj.a.b.c;  // Should be narrowed to string
                }
                return "default";
            }
            console.log(test({ a: { b: { c: "deep" } } }));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("deep\n", result);
    }

    #endregion

    #region Recursive Type Narrowing

    [Fact]
    public void RecursiveTypeProperty_NarrowsCorrectly()
    {
        var source = """
            type Node = { value: number; next: Node | null };

            function traverse(node: Node): void {
                console.log(node.value);
                if (node.next !== null) {
                    traverse(node.next);  // node.next is narrowed to Node
                }
            }

            const list: Node = { value: 1, next: { value: 2, next: { value: 3, next: null } } };
            traverse(list);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n2\n3\n", result);
    }

    [Fact]
    public void RecursiveTypeWithGeneric_NarrowsCorrectly()
    {
        var source = """
            type Node<T> = { value: T; next: Node<T> | null };

            function traverse<T>(node: Node<T>): void {
                console.log(node.value);
                if (node.next !== null) {
                    traverse(node.next);  // node.next is narrowed to Node<T>
                }
            }

            const list: Node<string> = { value: "a", next: { value: "b", next: null } };
            traverse(list);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("a\nb\n", result);
    }

    #endregion

    #region Union Type Narrowing

    [Fact]
    public void PropertyWithMultipleNullableTypes_NarrowsCorrectly()
    {
        var source = """
            type Obj = { prop: string | number | null };

            function test(obj: Obj): string {
                if (obj.prop !== null) {
                    // obj.prop is string | number (null removed)
                    return typeof obj.prop;
                }
                return "null";
            }
            console.log(test({ prop: 42 }));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("number\n", result);
    }

    #endregion

    #region Negative Tests - Narrowing Should Not Persist

    [Fact]
    public void NarrowingDoesNotPersistOutsideScope()
    {
        // This test verifies that narrowing is properly scoped
        var source = """
            type Obj = { prop: string | null };

            function test(obj: Obj): void {
                if (obj.prop !== null) {
                    console.log(obj.prop.length);  // OK - narrowed
                }
                // Outside the if, obj.prop is back to string | null
                console.log("done");
            }
            test({ prop: "hello" });
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("5\ndone\n", result);
    }

    #endregion
}

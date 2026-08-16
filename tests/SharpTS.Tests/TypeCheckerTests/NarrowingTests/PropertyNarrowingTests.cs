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

    #region Issue #43 regression tests

    [Fact]
    public void CompoundAndGuard_FlowsLhsNarrowingToRhs()
    {
        // Regression for issue #43: `a != null && a.b != null` spuriously
        // errored with "Property 'b' cannot be accessed on 'null'" because
        // the LHS narrowing wasn't applied when analyzing the RHS's path
        // guard.
        var source = """
            interface Obj { prop: string | null; }
            function test(o: Obj | null): string {
                if (o != null && o.prop != null) {
                    return o.prop;
                }
                return "default";
            }
            console.log(test({ prop: "hi" }));
            console.log(test(null));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("hi\ndefault\n", result);
    }

    [Fact]
    public void ThisRootedPath_NarrowsInAccessor()
    {
        // Regression for issue #43: narrowing of `this.x.y` property paths
        // didn't apply because the extractor didn't handle Expr.This.
        var source = """
            interface R { opaquePath: string | null; }
            class C {
                _record: R = { opaquePath: null };
                get pathname(): string {
                    if (this._record.opaquePath != null) return this._record.opaquePath;
                    return "";
                }
            }
            const c = new C();
            c._record.opaquePath = "p";
            console.log(c.pathname);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("p\n", result);
    }

    [Fact]
    public void PropertyNarrowing_DoesNotLeakAcrossFunctions()
    {
        // Regression for issue #43: narrowings added via AddNarrowing (used
        // for "if (x) return;" post-if narrowing) leaked across sibling
        // function bodies because function bodies didn't isolate the
        // narrowing-context stack.
        var source = """
            interface R { x: string | null; }
            function poison(r: R): void {
                if (r.x != null) return;  // adds r.x -> null to current scope
            }
            function use(r: R): string {
                if (r.x != null) return r.x;  // must see string, not null
                return "";
            }
            console.log(use({ x: "hi" }));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("hi\n", result);
    }

    [Fact]
    public void ThisNarrowing_DoesNotLeakBetweenAccessors()
    {
        // Regression for issue #43: narrowings on `this.x.y` leaked across
        // getter/setter boundaries because accessor bodies shared the
        // enclosing narrowing context.
        var source = """
            interface R { opaquePath: string | null; }
            class C {
                _record: R = { opaquePath: null };
                set host(_v: string) {
                    if (this._record.opaquePath != null) return;
                }
                get pathname(): string {
                    if (this._record.opaquePath != null) return this._record.opaquePath;
                    return "default";
                }
            }
            const c = new C();
            c._record.opaquePath = "p";
            console.log(c.pathname);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("p\n", result);
    }

    #endregion
}

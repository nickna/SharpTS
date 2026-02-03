using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests.NarrowingTests;

/// <summary>
/// Tests for narrowing invalidation when assignments occur.
/// These tests verify that type narrowings are properly invalidated when:
/// - The narrowed variable is reassigned
/// - The object containing a narrowed property is reassigned
/// - A property in the narrowing path is reassigned
/// </summary>
public class AssignmentInvalidationTests
{
    #region Direct Property Reassignment

    [Fact]
    public void PropertyReassignment_InvalidatesNarrowing()
    {
        // Reassigning the narrowed property should invalidate the narrowing
        var source = """
            type Obj = { prop: string | null };
            function test(obj: Obj): string {
                if (obj.prop !== null) {
                    obj.prop = null;  // Reassignment
                    return obj.prop;  // Should error - narrowing invalidated
                }
                return "default";
            }
            """;

        var ex = Assert.Throws<TypeMismatchException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("string | null", ex.Message);
        Assert.Contains("string", ex.Message);
    }

    [Fact]
    public void PropertyReassignment_ToSameType_StillInvalidates()
    {
        // Even reassigning to a compatible value should invalidate narrowing
        // because we're conservative about what the new value might be
        var source = """
            type Obj = { prop: string | null };
            function test(obj: Obj): string {
                if (obj.prop !== null) {
                    obj.prop = "hello";  // Reassigning to string
                    return obj.prop;  // Should error - narrowing still invalidated
                }
                return "default";
            }
            """;

        var ex = Assert.Throws<TypeMismatchException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("string | null", ex.Message);
    }

    #endregion

    #region Object Reassignment

    [Fact]
    public void ObjectReassignment_InvalidatesPropertyNarrowing()
    {
        // Reassigning the entire object should invalidate narrowings on its properties
        var source = """
            type Obj = { prop: string | null };
            function test(obj: Obj): string {
                if (obj.prop !== null) {
                    obj = { prop: null };  // Object reassignment
                    return obj.prop;  // Should error - narrowing invalidated
                }
                return "default";
            }
            """;

        var ex = Assert.Throws<TypeMismatchException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("string | null", ex.Message);
    }

    #endregion

    #region Nested Property Reassignment

    [Fact]
    public void NestedPropertyReassignment_InvalidatesDescendantNarrowings()
    {
        // Reassigning an intermediate property should invalidate nested narrowings
        var source = """
            type Inner = { value: string | null };
            type Outer = { inner: Inner };

            function test(obj: Outer): string {
                if (obj.inner.value !== null) {
                    obj.inner = { value: null };  // Reassign inner
                    return obj.inner.value;  // Should error - narrowing invalidated
                }
                return "default";
            }
            """;

        var ex = Assert.Throws<TypeMismatchException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("string | null", ex.Message);
    }

    #endregion

    #region Variable Reassignment

    [Fact(Skip = "Known limitation: Variable narrowing uses TypeEnvironment which replaces the type; assignments check narrowed type instead of declared type")]
    public void VariableReassignment_InvalidatesNarrowing()
    {
        // Note: This test is skipped because variable narrowing is implemented
        // by redefining the variable in TypeEnvironment with the narrowed type.
        // This means assignments check against the narrowed type, not the declared type.
        // TypeScript allows assigning the declared type (string | null) even when
        // the variable has been narrowed to (string).
        // Fixing this would require tracking declared types separately from narrowed types.
        var source = """
            function test(x: string | null): string {
                if (x !== null) {
                    x = getValue();  // Reassignment to string | null
                    return x;  // Should error - narrowing invalidated
                }
                return "default";
            }
            function getValue(): string | null { return "hello"; }
            """;

        var ex = Assert.Throws<TypeMismatchException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("string | null", ex.Message);
    }

    #endregion

    #region Narrowing Survives Unrelated Assignment

    [Fact]
    public void UnrelatedAssignment_DoesNotInvalidateNarrowing()
    {
        // Assignment to an unrelated property should not affect narrowing
        var source = """
            type Obj = { prop: string | null; other: number };
            function test(obj: Obj): string {
                if (obj.prop !== null) {
                    obj.other = 42;  // Unrelated assignment
                    return obj.prop;  // Should work - narrowing preserved
                }
                return "default";
            }
            console.log(test({ prop: "hello", other: 1 }));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\n", result);
    }

    [Fact]
    public void DifferentObject_AssignmentDoesNotInvalidate()
    {
        // Assignment to a different object's property should not affect narrowing
        var source = """
            type Obj = { prop: string | null };
            function test(obj1: Obj, obj2: Obj): string {
                if (obj1.prop !== null) {
                    obj2.prop = null;  // Different object
                    return obj1.prop;  // Should work - narrowing preserved
                }
                return "default";
            }
            console.log(test({ prop: "hello" }, { prop: "world" }));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\n", result);
    }

    #endregion

    #region Compound Assignment

    [Fact]
    public void CompoundAssignment_InvalidatesNarrowing()
    {
        // Compound assignments like += should also invalidate narrowing
        var source = """
            type Obj = { count: number | null };
            function test(obj: Obj): number {
                if (obj.count !== null) {
                    obj.count += 1;  // Compound assignment
                    return obj.count;  // Should error - narrowing invalidated
                }
                return 0;
            }
            """;

        var ex = Assert.Throws<TypeMismatchException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("number | null", ex.Message);
    }

    #endregion

    #region Index Assignment

    [Fact]
    public void TupleIndexAssignment_InvalidatesNarrowing()
    {
        // Assignment to a tuple element should invalidate its narrowing
        var source = """
            function test(tuple: [string | null, number]): string {
                if (tuple[0] !== null) {
                    tuple[0] = null;  // Index assignment
                    return tuple[0];  // Should error - narrowing invalidated
                }
                return "default";
            }
            """;

        var ex = Assert.Throws<TypeMismatchException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("string | null", ex.Message);
    }

    #endregion

    #region Loop Assignment

    [Fact(Skip = "Phase 3: CFG-based narrowing required for loop analysis")]
    public void AssignmentInLoop_InvalidatesNarrowing()
    {
        // Assignment inside a loop should invalidate narrowing
        // This requires CFG analysis to detect that the assignment in the loop
        // can affect the narrowing for subsequent iterations
        var source = """
            type Obj = { prop: string | null };
            function test(obj: Obj): void {
                if (obj.prop !== null) {
                    for (let i = 0; i < 3; i++) {
                        console.log(obj.prop.length);  // Should error on second iteration concept
                        obj.prop = null;
                    }
                }
            }
            """;

        var ex = Assert.Throws<TypeMismatchException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("string | null", ex.Message);
    }

    [Fact]
    public void AssignmentInLoopBody_InvalidatesAfterAssignment()
    {
        // Assignment in the loop body should at least invalidate for code after the assignment
        // Note: This test uses a simpler structure - the assignment and access are in sequence
        var source = """
            type Obj = { prop: string | null };
            function test(obj: Obj): string {
                if (obj.prop !== null) {
                    obj.prop = getValue();  // Reassignment invalidates narrowing
                    return obj.prop;  // Should error - narrowing invalidated
                }
                return "default";
            }
            function getValue(): string | null { return null; }
            """;

        var ex = Assert.Throws<TypeMismatchException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("string | null", ex.Message);
    }

    #endregion
}

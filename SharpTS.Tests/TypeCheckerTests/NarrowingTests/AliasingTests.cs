using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests.NarrowingTests;

/// <summary>
/// Tests for aliasing awareness in type narrowing (Phase 5).
/// These tests verify that type narrowings are properly invalidated when:
/// - Objects are passed to functions that might mutate them
/// - Objects are aliased to other variables
/// - Readonly properties are safely narrowed
/// </summary>
public class AliasingTests
{
    #region Readonly Property Narrowing

    [Fact(Skip = "Parser doesn't support readonly keyword in interfaces yet")]
    public void ReadonlyProperty_SafeToNarrow()
    {
        // Readonly properties can be safely narrowed since they can't be mutated
        var source = """
            interface Obj {
                readonly prop: string | null;
            }
            function test(obj: Obj): string {
                if (obj.prop !== null) {
                    someFunction(obj);  // Can't mutate readonly prop
                    return obj.prop;  // Should still be narrowed to string
                }
                return "default";
            }
            function someFunction(o: Obj): void { }
            console.log(test({ prop: "hello" }));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\n", result);
    }

    [Fact(Skip = "Parser doesn't support readonly keyword in interfaces yet")]
    public void ReadonlyNestedProperty_SafeToNarrow()
    {
        // Readonly nested properties should also be safe
        var source = """
            interface Inner {
                readonly value: string | null;
            }
            interface Outer {
                readonly inner: Inner;
            }
            function test(obj: Outer): string {
                if (obj.inner.value !== null) {
                    someFunction(obj);
                    return obj.inner.value;  // Should still be narrowed
                }
                return "default";
            }
            function someFunction(o: Outer): void { }
            console.log(test({ inner: { value: "hello" } }));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\n", result);
    }

    #endregion

    #region Function Call Invalidation (Conservative)

    [Fact]
    public void FunctionCall_InvalidatesMutablePropertyNarrowing()
    {
        // When object is passed to a function, mutable property narrowings should be invalidated
        var source = """
            type Obj = { prop: string | null };
            function test(obj: Obj): string {
                if (obj.prop !== null) {
                    mutate(obj);  // Might modify obj.prop
                    return obj.prop;  // Should error - narrowing invalidated
                }
                return "default";
            }
            function mutate(o: Obj): void {
                o.prop = null;
            }
            """;

        var ex = Assert.Throws<TypeMismatchException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("string | null", ex.Message);
    }

    [Fact]
    public void FunctionCall_InvalidatesOnlyPassedObject()
    {
        // Only the passed object's narrowings should be invalidated
        var source = """
            type Obj = { prop: string | null };
            function test(obj1: Obj, obj2: Obj): string {
                if (obj1.prop !== null && obj2.prop !== null) {
                    mutate(obj1);  // Only affects obj1
                    return obj2.prop;  // Should still be narrowed
                }
                return "default";
            }
            function mutate(o: Obj): void { }
            console.log(test({ prop: "a" }, { prop: "b" }));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("b\n", result);
    }

    [Fact]
    public void MethodCall_InvalidatesMutablePropertyNarrowing()
    {
        // Method calls on an object should invalidate its mutable property narrowings
        var source = """
            class Container {
                prop: string | null = "initial";
                mutate(): void {
                    this.prop = null;
                }
            }
            function test(obj: Container): string {
                if (obj.prop !== null) {
                    obj.mutate();  // Might modify obj.prop
                    return obj.prop;  // Should error - narrowing invalidated
                }
                return "default";
            }
            """;

        var ex = Assert.Throws<TypeMismatchException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("string | null", ex.Message);
    }

    #endregion

    #region Alias Tracking

    [Fact]
    public void AliasAssignment_InvalidatesNarrowing()
    {
        // When an alias is created, mutations through the alias should invalidate narrowings
        var source = """
            type Obj = { prop: string | null };
            function test(obj: Obj): string {
                if (obj.prop !== null) {
                    const alias = obj;
                    alias.prop = null;  // Mutation through alias
                    return obj.prop;  // Should error - narrowing invalidated
                }
                return "default";
            }
            """;

        var ex = Assert.Throws<TypeMismatchException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("string | null", ex.Message);
    }

    [Fact(Skip = "Complex alias tracking not yet implemented")]
    public void AliasInDifferentScope_InvalidatesNarrowing()
    {
        // This is a more complex case that requires escape analysis
        var source = """
            type Obj = { prop: string | null };
            let globalAlias: Obj | null = null;

            function setAlias(obj: Obj): void {
                globalAlias = obj;
            }

            function test(obj: Obj): string {
                if (obj.prop !== null) {
                    setAlias(obj);  // obj now has an alias
                    globalAlias!.prop = null;  // Mutation through alias
                    return obj.prop;  // Should error
                }
                return "default";
            }
            """;

        var ex = Assert.Throws<TypeMismatchException>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("string | null", ex.Message);
    }

    #endregion

    #region Safe Patterns (No Invalidation Needed)

    [Fact]
    public void LocalObjectLiteral_SafeToNarrow()
    {
        // Objects created locally and not aliased are safe to narrow
        var source = """
            function test(): string {
                const obj: { prop: string | null } = { prop: "hello" };
                if (obj.prop !== null) {
                    return obj.prop;  // Safe - obj hasn't escaped
                }
                return "default";
            }
            console.log(test());
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\n", result);
    }

    [Fact]
    public void ConstBinding_NoReassignment()
    {
        // Const bindings can't be reassigned, but properties can still be mutated
        var source = """
            type Obj = { prop: string | null };
            function test(): string {
                const obj: Obj = { prop: "hello" };
                if (obj.prop !== null) {
                    // obj can't be reassigned, but obj.prop can be mutated
                    return obj.prop;  // Safe within this scope
                }
                return "default";
            }
            console.log(test());
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\n", result);
    }

    #endregion

    #region Discriminated Union Narrowing

    [Fact]
    public void DiscriminatedUnion_NarrowsCorrectly()
    {
        // Discriminated unions should narrow the entire type
        var source = """
            type Circle = { kind: "circle"; radius: number };
            type Square = { kind: "square"; size: number };
            type Shape = Circle | Square;

            function area(shape: Shape): number {
                if (shape.kind === "circle") {
                    return 3.14 * shape.radius * shape.radius;
                } else {
                    return shape.size * shape.size;
                }
            }
            console.log(area({ kind: "circle", radius: 10 }));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("314\n", result);
    }

    [Fact]
    public void DiscriminatedUnion_FunctionCallDoesNotInvalidateKind()
    {
        // The discriminant property is typically readonly/immutable
        var source = """
            type Circle = { kind: "circle"; radius: number };
            type Square = { kind: "square"; size: number };
            type Shape = Circle | Square;

            function logShape(s: Shape): void {
                console.log(s.kind);
            }

            function area(shape: Shape): number {
                if (shape.kind === "circle") {
                    logShape(shape);  // Passing to function
                    return 3.14 * shape.radius * shape.radius;  // Should still be narrowed
                }
                return 0;
            }
            console.log(area({ kind: "circle", radius: 10 }));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("circle\n314\n", result);
    }

    #endregion
}

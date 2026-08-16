using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Tests for the 'satisfies' operator (TypeScript 4.9+).
/// The satisfies operator validates that an expression matches a type without widening the inferred type.
/// </summary>
public class SatisfiesTypeTests
{
    #region Basic Validation

    [Fact]
    public void Satisfies_BasicNumber_Passes()
    {
        var source = """
            const x = 42 satisfies number;
            console.log(x);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void Satisfies_BasicString_Passes()
    {
        var source = """
            const x = "hello" satisfies string;
            console.log(x);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\n", output);
    }

    [Fact]
    public void Satisfies_ObjectLiteral_Passes()
    {
        var source = """
            const obj = { x: 1, y: 2 } satisfies { x: number, y: number };
            console.log(obj.x);
            console.log(obj.y);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n2\n", output);
    }

    #endregion

    #region Type Validation Errors

    [Fact]
    public void Satisfies_WrongType_Fails()
    {
        var source = """
            const x = "hello" satisfies number;
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("does not satisfy constraint", ex.Message);
    }

    [Fact]
    public void Satisfies_MissingProperty_Fails()
    {
        var source = """
            const obj = { x: 1 } satisfies { x: number, y: number };
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("does not satisfy constraint", ex.Message);
    }

    [Fact]
    public void Satisfies_WrongPropertyType_Fails()
    {
        var source = """
            const obj = { x: "not a number" } satisfies { x: number };
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("does not satisfy constraint", ex.Message);
    }

    #endregion

    #region Union Constraints

    [Fact]
    public void Satisfies_UnionConstraint_StringMember_Passes()
    {
        var source = """
            const x = "hello" satisfies string | number;
            console.log(x);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\n", output);
    }

    [Fact]
    public void Satisfies_UnionConstraint_NumberMember_Passes()
    {
        var source = """
            const x = 42 satisfies string | number;
            console.log(x);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n", output);
    }

    #endregion

    #region Escape Hatches (any/unknown)

    [Fact]
    public void Satisfies_AnyConstraint_AlwaysPasses()
    {
        var source = """
            const x = { weird: true, stuff: "here" } satisfies any;
            console.log(x.weird);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Satisfies_UnknownConstraint_AlwaysPasses()
    {
        var source = """
            const x = [1, 2, 3] satisfies unknown;
            console.log(x[0]);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n", output);
    }

    [Fact]
    public void Satisfies_AnyValue_AlwaysPasses()
    {
        var source = """
            let anyVal: any = "hello";
            const x = anyVal satisfies number;
            console.log(x);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\n", output);
    }

    #endregion

    #region Chaining

    [Fact]
    public void Satisfies_Chained_Passes()
    {
        var source = """
            const x = 42 satisfies number satisfies number;
            console.log(x);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n", output);
    }

    #endregion

    #region Excess Properties OK (Unlike Assignment)

    [Fact]
    public void Satisfies_ExcessProperties_Passes()
    {
        var source = """
            const obj = { x: 1, y: 2, z: 3 } satisfies { x: number };
            console.log(obj.x);
            console.log(obj.y);
            console.log(obj.z);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n2\n3\n", output);
    }

    #endregion

    #region Array Constraints

    [Fact]
    public void Satisfies_ArrayConstraint_Passes()
    {
        var source = """
            const arr = [1, 2, 3] satisfies number[];
            console.log(arr[0]);
            console.log(arr.length);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n3\n", output);
    }

    [Fact]
    public void Satisfies_ArrayConstraint_WrongElementType_Fails()
    {
        var source = """
            const arr = ["a", "b"] satisfies number[];
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("does not satisfy constraint", ex.Message);
    }

    #endregion
}

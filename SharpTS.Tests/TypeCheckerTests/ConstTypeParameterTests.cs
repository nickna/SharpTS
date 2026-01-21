using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Tests for const type parameters (TypeScript 5.0+ feature).
/// Const type parameters preserve literal types during inference instead of widening.
/// </summary>
public class ConstTypeParameterTests
{
    #region Basic Parsing and Type Checking

    [Fact]
    public void ConstTypeParam_BasicFunction_Parses()
    {
        var source = """
            function identity<const T>(x: T): T {
                return x;
            }
            console.log(identity(42));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n", result);
    }

    [Fact]
    public void ConstTypeParam_WithConstraint_Parses()
    {
        var source = """
            function identity<const T extends string>(x: T): T {
                return x;
            }
            console.log(identity("hello"));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\n", result);
    }

    [Fact]
    public void ConstTypeParam_MultipleParams_Parses()
    {
        var source = """
            function pair<const K, V>(key: K, value: V): string {
                return key + ":" + value;
            }
            console.log(pair("name", 42));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("name:42\n", result);
    }

    [Fact]
    public void ConstTypeParam_MixedConstAndNonConst_Parses()
    {
        var source = """
            function wrap<const T, U>(value: T, converter: (x: T) => U): U {
                return converter(value);
            }
            console.log(wrap(42, x => x * 2));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("84\n", result);
    }

    #endregion

    #region Class and Interface Const Type Parameters

    [Fact]
    public void ConstTypeParam_InClass_Parses()
    {
        var source = """
            class Box<const T> {
                constructor(public value: T) {}
            }
            let box = new Box(42);
            console.log(box.value);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n", result);
    }

    [Fact]
    public void ConstTypeParam_InInterface_Parses()
    {
        var source = """
            interface Container<const T> {
                value: T;
            }
            function makeContainer<const T>(v: T): Container<T> {
                return { value: v };
            }
            let c = makeContainer(42);
            console.log(c.value);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n", result);
    }

    #endregion

    #region Constraint Validation

    [Fact]
    public void ConstTypeParam_ConstraintViolation_Fails()
    {
        var source = """
            function identity<const T extends string>(x: T): T {
                return x;
            }
            identity(42);
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void ConstTypeParam_ConstraintSatisfied_Passes()
    {
        var source = """
            function identity<const T extends number>(x: T): T {
                return x;
            }
            console.log(identity(42));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n", result);
    }

    #endregion

    #region Explicit Type Arguments Override Const

    [Fact]
    public void ConstTypeParam_ExplicitTypeArg_Accepted()
    {
        var source = """
            function identity<const T>(x: T): T {
                return x;
            }
            let result: number = identity<number>(42);
            console.log(result);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n", result);
    }

    #endregion

    #region Default Type Parameters with Const

    [Fact]
    public void ConstTypeParam_WithDefault_Parses()
    {
        var source = """
            function identity<const T = string>(x: T): T {
                return x;
            }
            console.log(identity("hello"));
            console.log(identity(42));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\n42\n", result);
    }

    #endregion

    #region Complex Type Inference

    [Fact]
    public void ConstTypeParam_WithObjectLiteral_Parses()
    {
        var source = """
            function wrap<const T>(obj: T): { value: T } {
                return { value: obj };
            }
            let wrapped = wrap({ x: 1, y: 2 });
            console.log(wrapped.value.x);
            console.log(wrapped.value.y);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n2\n", result);
    }

    [Fact]
    public void ConstTypeParam_WithArrayLiteral_Parses()
    {
        var source = """
            function first<const T>(arr: T[]): T {
                return arr[0];
            }
            console.log(first([1, 2, 3]));
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n", result);
    }

    #endregion
}

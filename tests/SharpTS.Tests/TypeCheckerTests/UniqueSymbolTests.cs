using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Tests for unique symbol type support in TypeScript.
/// Covers const declarations with unique symbol, type compatibility, and well-known symbols.
/// </summary>
public class UniqueSymbolTests
{
    #region Valid Declarations

    [Fact]
    public void UniqueSymbol_BasicDeclaration_TypeChecks()
    {
        var source = """
            const KEY: unique symbol = Symbol();
            console.log("ok");
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("ok\n", result);
    }

    [Fact]
    public void UniqueSymbol_WithDescription_TypeChecks()
    {
        var source = """
            const KEY: unique symbol = Symbol("myKey");
            console.log("ok");
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("ok\n", result);
    }

    [Fact]
    public void UniqueSymbol_MultipleDeclarations_TypeChecks()
    {
        var source = """
            const KEY1: unique symbol = Symbol("key1");
            const KEY2: unique symbol = Symbol("key2");
            console.log("ok");
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("ok\n", result);
    }

    #endregion

    #region Invalid Declarations

    [Fact]
    public void UniqueSymbol_OnLet_Fails()
    {
        var source = """
            let KEY: unique symbol = Symbol();
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("unique symbol", ex.Message.ToLower());
    }

    [Fact]
    public void UniqueSymbol_NotInitializedWithSymbol_Fails()
    {
        var source = """
            const KEY: unique symbol = 5;
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Symbol()", ex.Message);
    }

    [Fact]
    public void UniqueSymbol_SymbolDescriptionMustBeString_Fails()
    {
        var source = """
            const KEY: unique symbol = Symbol(42);
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("string", ex.Message.ToLower());
    }

    #endregion

    #region Type Compatibility

    [Fact]
    public void UniqueSymbol_AssignableToSymbol()
    {
        var source = """
            const KEY: unique symbol = Symbol();
            let s: symbol = KEY;
            console.log("ok");
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("ok\n", result);
    }

    [Fact]
    public void Symbol_NotAssignableToUniqueSymbol()
    {
        var source = """
            const KEY: unique symbol = Symbol();
            let s: symbol = Symbol();
            let u: typeof KEY = s;
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void DifferentUniqueSymbols_NotAssignable()
    {
        var source = """
            const KEY1: unique symbol = Symbol();
            const KEY2: unique symbol = Symbol();
            let u: typeof KEY1 = KEY2;
            """;

        var ex = Assert.ThrowsAny<Exception>(() => TestHarness.RunInterpreted(source));
        Assert.Contains("Type Error", ex.Message);
    }

    [Fact]
    public void SameUniqueSymbol_AssignableToItself()
    {
        var source = """
            const KEY: unique symbol = Symbol();
            let u: typeof KEY = KEY;
            console.log("ok");
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("ok\n", result);
    }

    #endregion

    #region Well-Known Symbols

    [Fact]
    public void WellKnownSymbol_AssignableToSymbol()
    {
        var source = """
            let s: symbol = Symbol.iterator;
            console.log("ok");
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("ok\n", result);
    }

    #endregion

    #region Const Declaration Basic Tests

    [Fact]
    public void ConstDeclaration_NumberType()
    {
        var source = """
            const x: number = 42;
            console.log(x);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("42\n", result);
    }

    [Fact]
    public void ConstDeclaration_StringType()
    {
        var source = """
            const s: string = "hello";
            console.log(s);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("hello\n", result);
    }

    [Fact]
    public void ConstDeclaration_InferredType()
    {
        var source = """
            const x = 42;
            const s = "hello";
            console.log(x, s);
            """;

        var result = TestHarness.RunInterpreted(source);
        Assert.Equal("42 hello\n", result);
    }

    #endregion
}

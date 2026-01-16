using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.ParserTests;

/// <summary>
/// Tests for parser error handling.
/// Verifies that the parser throws appropriate exceptions for invalid syntax.
/// </summary>
public class ErrorHandlingTests
{
    #region Helpers

    private static Exception ParseAndExpectError(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        return Assert.Throws<Exception>(() => parser.Parse());
    }

    private static List<Stmt> TryParse(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        return parser.Parse();
    }

    #endregion

    #region Missing Tokens

    [Fact]
    public void Error_MissingClosingParen()
    {
        var ex = ParseAndExpectError("(1 + 2");
        Assert.Contains(")", ex.Message);
    }

    [Fact]
    public void Error_MissingClosingBracket()
    {
        var ex = ParseAndExpectError("[1, 2, 3");
        Assert.Contains("]", ex.Message);
    }

    [Fact]
    public void Error_MissingClosingBrace()
    {
        var ex = ParseAndExpectError("{ let x = 1;");
        Assert.Contains("}", ex.Message);
    }

    [Fact]
    public void Error_MissingSemicolonInFor()
    {
        // Missing second semicolon in for loop
        var ex = ParseAndExpectError("for (let i = 0; i < 10) { }");
        Assert.NotNull(ex);
    }

    #endregion

    #region Invalid Assignment Targets

    [Fact]
    public void Error_AssignToLiteral()
    {
        var ex = ParseAndExpectError("5 = 10;");
        Assert.NotNull(ex);
    }

    [Fact]
    public void Error_AssignToExpression()
    {
        var ex = ParseAndExpectError("(a + b) = 10;");
        Assert.NotNull(ex);
    }

    [Fact]
    public void Error_IncrementLiteral()
    {
        var ex = ParseAndExpectError("5++;");
        Assert.NotNull(ex);
    }

    [Fact]
    public void Error_DecrementExpression()
    {
        var ex = ParseAndExpectError("--(a + b);");
        Assert.NotNull(ex);
    }

    #endregion

    #region Invalid Declarations

    [Fact]
    public void Error_ConstWithoutInitializer()
    {
        // const requires initializer
        var ex = ParseAndExpectError("const x;");
        Assert.NotNull(ex);
    }

    [Fact]
    public void Error_ClassWithoutName()
    {
        // Note: Anonymous class expressions are valid, but class declarations need names
        // This tests that "class { }" as a statement fails
        var ex = ParseAndExpectError("class { }");
        Assert.NotNull(ex);
    }

    [Fact]
    public void Error_FunctionWithoutName()
    {
        // Function declaration without name
        var ex = ParseAndExpectError("function() { }");
        Assert.NotNull(ex);
    }

    #endregion

    #region Invalid Control Flow

    [Fact]
    public void Error_IfWithoutCondition()
    {
        var ex = ParseAndExpectError("if { }");
        Assert.NotNull(ex);
    }

    [Fact]
    public void Error_WhileWithoutCondition()
    {
        var ex = ParseAndExpectError("while { }");
        Assert.NotNull(ex);
    }

    [Fact]
    public void Error_SwitchWithoutExpression()
    {
        var ex = ParseAndExpectError("switch { }");
        Assert.NotNull(ex);
    }

    #endregion

    #region Invalid Expressions

    [Fact]
    public void Error_UnterminatedTernary()
    {
        var ex = ParseAndExpectError("a ? b;");
        Assert.NotNull(ex);
    }

    [Fact]
    public void Error_BinaryWithoutRightOperand()
    {
        var ex = ParseAndExpectError("a +;");
        Assert.NotNull(ex);
    }

    [Fact]
    public void Error_UnaryWithoutOperand()
    {
        var ex = ParseAndExpectError("!;");
        Assert.NotNull(ex);
    }

    #endregion

    #region Valid Edge Cases (should NOT error)

    [Fact]
    public void Valid_EmptySource()
    {
        var statements = TryParse("");
        Assert.Empty(statements);
    }

    [Fact]
    public void Valid_OnlySemicolons()
    {
        var statements = TryParse(";;;");
        // Empty statements should be parsed successfully
        Assert.NotNull(statements);
    }

    [Fact]
    public void Valid_TrailingCommaInArray()
    {
        var statements = TryParse("[1, 2, 3,];");
        Assert.Single(statements);
    }

    [Fact]
    public void Valid_TrailingCommaInObjectLiteral()
    {
        var statements = TryParse("({ a: 1, b: 2, });");
        Assert.Single(statements);
    }

    [Fact]
    public void Valid_TrailingCommaInFunctionParams()
    {
        var statements = TryParse("function foo(a, b,) { }");
        Assert.Single(statements);
    }

    #endregion
}

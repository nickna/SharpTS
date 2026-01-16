using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.ParserTests;

/// <summary>
/// Tests for parser error recovery functionality.
/// Verifies that the parser can collect multiple errors and continue parsing.
/// </summary>
public class ParserRecoveryTests
{
    #region Helpers

    private static ParseResult Parse(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        return parser.Parse();
    }

    #endregion

    #region Basic Error Collection

    [Fact]
    public void Parse_NoErrors_ReturnsSuccess()
    {
        var result = Parse("let x = 5;");

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Errors);
        Assert.Single(result.Statements);
    }

    [Fact]
    public void Parse_SingleError_CollectsError()
    {
        var result = Parse("let x = ;");

        Assert.False(result.IsSuccess);
        Assert.Single(result.Errors);
        Assert.Equal(1, result.Errors[0].Line);
    }

    [Fact]
    public void Parse_MultipleErrors_CollectsAll()
    {
        var source = """
            let x = ;
            let y = 5;
            class { }
            """;
        var result = Parse(source);

        Assert.False(result.IsSuccess);
        Assert.True(result.Errors.Count >= 2, $"Expected at least 2 errors, got {result.Errors.Count}");
    }

    [Fact]
    public void Parse_ErrorsOnDifferentLines_TracksLineNumbers()
    {
        var source = """
            let a = ;
            let b = 5;
            let c = ;
            """;
        var result = Parse(source);

        Assert.False(result.IsSuccess);
        Assert.True(result.Errors.Count >= 2);

        // Errors should be on different lines
        var lines = result.Errors.Select(e => e.Line).Distinct().ToList();
        Assert.True(lines.Count >= 2, "Errors should be on different lines");
    }

    #endregion

    #region Error Limit

    [Fact]
    public void Parse_ErrorLimit_StopsAt10()
    {
        // Generate 15 statements with errors
        var source = string.Join("\n", Enumerable.Repeat("let x = ;", 15));
        var result = Parse(source);

        Assert.False(result.IsSuccess);
        Assert.True(result.HitErrorLimit);
        Assert.Equal(10, result.Errors.Count);
    }

    [Fact]
    public void Parse_UnderErrorLimit_DoesNotSetHitErrorLimit()
    {
        var source = """
            let a = ;
            let b = ;
            let c = ;
            """;
        var result = Parse(source);

        Assert.False(result.IsSuccess);
        Assert.False(result.HitErrorLimit);
        Assert.True(result.Errors.Count < 10);
    }

    [Fact]
    public void Parse_ExactlyAtLimit_SetsHitErrorLimit()
    {
        // Generate exactly 10 statements with errors
        var source = string.Join("\n", Enumerable.Repeat("let x = ;", 10));
        var result = Parse(source);

        Assert.False(result.IsSuccess);
        Assert.True(result.HitErrorLimit);
        Assert.Equal(10, result.Errors.Count);
    }

    #endregion

    #region Recovery and Continuation

    [Fact]
    public void Parse_ValidStatementAfterError_StillParsed()
    {
        var source = """
            let x = ;
            let y = 5;
            """;
        var result = Parse(source);

        Assert.False(result.IsSuccess);
        // The valid statement "let y = 5" should still be parsed
        Assert.True(result.Statements.Count >= 1, "Valid statements should be parsed");

        // Check that we have at least one Var statement with value 5
        var varStatements = result.Statements.OfType<Stmt.Var>().ToList();
        Assert.Contains(varStatements, v => v.Name.Lexeme == "y");
    }

    [Fact]
    public void Parse_ErrorBetweenValidStatements_ParsesAll()
    {
        var source = """
            let a = 1;
            let b = ;
            let c = 3;
            """;
        var result = Parse(source);

        Assert.False(result.IsSuccess);

        // Should have parsed the valid statements
        var varStatements = result.Statements.OfType<Stmt.Var>().ToList();
        Assert.Contains(varStatements, v => v.Name.Lexeme == "a");
        Assert.Contains(varStatements, v => v.Name.Lexeme == "c");
    }

    [Fact]
    public void Parse_MultipleValidAfterError_AllParsed()
    {
        var source = """
            let x = ;
            let a = 1;
            let b = 2;
            let c = 3;
            """;
        var result = Parse(source);

        Assert.False(result.IsSuccess);
        Assert.Single(result.Errors);

        // All three valid statements should be parsed
        var varStatements = result.Statements.OfType<Stmt.Var>().ToList();
        Assert.True(varStatements.Count >= 3, $"Expected at least 3 var statements, got {varStatements.Count}");
    }

    #endregion

    #region Synchronization Points

    [Fact]
    public void Parse_SynchronizesAtSemicolon()
    {
        var source = """
            let x = (;
            let y = 5;
            """;
        var result = Parse(source);

        // Should recover after the semicolon and parse "let y = 5"
        var varStatements = result.Statements.OfType<Stmt.Var>().ToList();
        Assert.Contains(varStatements, v => v.Name.Lexeme == "y");
    }

    [Fact]
    public void Parse_SynchronizesAtClassKeyword()
    {
        // Missing expression before semicolon - error at ;, then recover at 'class'
        // Note: We can't use "let x = (" because 'class' is a valid expression (class expression)
        // and would be parsed as part of the grouping expression.
        var source = "let x = ;\nclass Foo { }";
        var result = Parse(source);

        Assert.False(result.IsSuccess);
        Assert.True(result.Errors.Count >= 1);
        // Should recover at "class" and parse the class
        var classStatements = result.Statements.OfType<Stmt.Class>().ToList();
        Assert.Single(classStatements);
        Assert.Equal("Foo", classStatements[0].Name.Lexeme);
    }

    [Fact]
    public void Parse_SynchronizesAtFunctionKeyword()
    {
        // Note: "function" is also a valid expression, so we use the same pattern
        var source = "let x = ;\nfunction foo() { return 1; }";
        var result = Parse(source);

        Assert.False(result.IsSuccess);
        // Should recover at "function" and parse the function
        var funcStatements = result.Statements.OfType<Stmt.Function>().ToList();
        Assert.Single(funcStatements);
        Assert.Equal("foo", funcStatements[0].Name.Lexeme);
    }

    [Fact]
    public void Parse_SynchronizesAtIfKeyword()
    {
        // 'if' is not a valid expression, but let's use consistent pattern
        var source = "let x = ;\nif (true) { let y = 1; }";
        var result = Parse(source);

        Assert.False(result.IsSuccess);
        // Should recover at "if" and parse the if statement
        var ifStatements = result.Statements.OfType<Stmt.If>().ToList();
        Assert.Single(ifStatements);
    }

    [Fact]
    public void Parse_SynchronizesAtForKeyword()
    {
        var source = "let x = ;\nfor (let i = 0; i < 10; i++) { }";
        var result = Parse(source);

        Assert.False(result.IsSuccess);
        // Should recover at "for" and parse the for loop (desugared to block/while)
        Assert.True(result.Statements.Count >= 1);
    }

    [Fact]
    public void Parse_SynchronizesAtWhileKeyword()
    {
        var source = "let x = ;\nwhile (true) { break; }";
        var result = Parse(source);

        Assert.False(result.IsSuccess);
        // Should recover at "while" and parse the while loop
        var whileStatements = result.Statements.OfType<Stmt.While>().ToList();
        Assert.Single(whileStatements);
    }

    [Fact]
    public void Parse_SynchronizesAtReturnKeyword()
    {
        var source = "let x = ;\nreturn 5;";
        var result = Parse(source);

        Assert.False(result.IsSuccess);
        // Should recover at "return" and parse the return statement
        var returnStatements = result.Statements.OfType<Stmt.Return>().ToList();
        Assert.Single(returnStatements);
    }

    [Fact]
    public void Parse_SynchronizesAtInterfaceKeyword()
    {
        var source = "let x = ;\ninterface IFoo { x: number; }";
        var result = Parse(source);

        Assert.False(result.IsSuccess);
        // Should recover at "interface" and parse the interface
        var interfaceStatements = result.Statements.OfType<Stmt.Interface>().ToList();
        Assert.Single(interfaceStatements);
        Assert.Equal("IFoo", interfaceStatements[0].Name.Lexeme);
    }

    [Fact]
    public void Parse_SynchronizesAtEnumKeyword()
    {
        var source = "let x = ;\nenum Color { Red, Green, Blue }";
        var result = Parse(source);

        Assert.False(result.IsSuccess);
        // Should recover at "enum" and parse the enum
        var enumStatements = result.Statements.OfType<Stmt.Enum>().ToList();
        Assert.Single(enumStatements);
        Assert.Equal("Color", enumStatements[0].Name.Lexeme);
    }

    [Fact]
    public void Parse_ClassExpressionInGroupingWithNewlines_ParsesCorrectly()
    {
        // This tests that 'class' inside a grouping is correctly parsed as a class expression
        // even when there are newlines inside the grouping
        var source = "let x = (\nclass Foo { }\n);";
        var result = Parse(source);

        Assert.True(result.IsSuccess,
            $"Expected success but got errors: {string.Join(", ", result.Errors.Select(e => e.Message))}");
        // The class is parsed as a class EXPRESSION, not a class statement
        var varStatements = result.Statements.OfType<Stmt.Var>().ToList();
        Assert.Single(varStatements);
        // The initializer should be a class expression inside a grouping
        Assert.NotNull(varStatements[0].Initializer);
    }

    #endregion

    #region ParseOrThrow Backward Compatibility

    [Fact]
    public void ParseOrThrow_NoErrors_ReturnsStatements()
    {
        var lexer = new Lexer("let x = 5;");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);

        var statements = parser.ParseOrThrow();

        Assert.Single(statements);
        var varStmt = Assert.IsType<Stmt.Var>(statements[0]);
        Assert.Equal("x", varStmt.Name.Lexeme);
    }

    [Fact]
    public void ParseOrThrow_WithError_ThrowsException()
    {
        var lexer = new Lexer("let x = ;");
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);

        var exception = Assert.Throws<Exception>(() => parser.ParseOrThrow());
        Assert.Contains("Parse Error", exception.Message);
    }

    [Fact]
    public void ParseOrThrow_MultipleErrors_ThrowsOnFirst()
    {
        var source = """
            let a = ;
            let b = ;
            """;
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);

        var exception = Assert.Throws<Exception>(() => parser.ParseOrThrow());
        // Should throw on first error (line 1)
        Assert.Contains("line 1", exception.Message);
    }

    #endregion

    #region Error Message Quality

    [Fact]
    public void Parse_ErrorMessage_ContainsLineNumber()
    {
        var result = Parse("let x = ;");

        Assert.Single(result.Errors);
        var errorString = result.Errors[0].ToString();
        Assert.Contains("line", errorString.ToLower());
    }

    [Fact]
    public void Parse_ErrorMessage_ContainsRelevantInfo()
    {
        var result = Parse("let x = ;");

        Assert.Single(result.Errors);
        // Error message should indicate what was expected or what went wrong
        Assert.False(string.IsNullOrWhiteSpace(result.Errors[0].Message));
    }

    [Fact]
    public void Parse_Error_HasTokenLexeme()
    {
        var result = Parse("let x = ;");

        Assert.Single(result.Errors);
        // TokenLexeme should be captured for context
        Assert.NotNull(result.Errors[0].TokenLexeme);
    }

    #endregion

    #region Complex Scenarios

    [Fact]
    public void Parse_NestedBlockErrors_Recovers()
    {
        var source = """
            function foo() {
                let x = ;
                let y = 5;
            }
            let z = 10;
            """;
        var result = Parse(source);

        Assert.False(result.IsSuccess);
        // Should still parse the function and the outer variable
        Assert.True(result.Statements.Count >= 2);
    }

    [Fact]
    public void Parse_ClassWithErrors_Recovers()
    {
        var source = """
            class Foo {
                x: ;
            }
            let y = 5;
            """;
        var result = Parse(source);

        Assert.False(result.IsSuccess);
        // Should recover and parse "let y = 5"
        var varStatements = result.Statements.OfType<Stmt.Var>().ToList();
        Assert.Contains(varStatements, v => v.Name.Lexeme == "y");
    }

    [Fact]
    public void Parse_MixedValidAndInvalid_ParsesAllValid()
    {
        var source = """
            let a = 1;
            let b = ;
            function foo() { return 2; }
            let c = ;
            class Bar { }
            let d = ;
            let e = 5;
            """;
        var result = Parse(source);

        Assert.False(result.IsSuccess);
        Assert.Equal(3, result.Errors.Count);

        // Check that valid declarations were parsed
        var varStatements = result.Statements.OfType<Stmt.Var>().ToList();
        Assert.Contains(varStatements, v => v.Name.Lexeme == "a");
        Assert.Contains(varStatements, v => v.Name.Lexeme == "e");

        var funcStatements = result.Statements.OfType<Stmt.Function>().ToList();
        Assert.Contains(funcStatements, f => f.Name.Lexeme == "foo");

        var classStatements = result.Statements.OfType<Stmt.Class>().ToList();
        Assert.Contains(classStatements, c => c.Name.Lexeme == "Bar");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Parse_EmptySource_ReturnsEmptySuccess()
    {
        var result = Parse("");

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Statements);
    }

    [Fact]
    public void Parse_OnlySemicolons_ReturnsSuccess()
    {
        var result = Parse(";;;");

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Parse_ErrorAtEndOfFile_Collects()
    {
        var result = Parse("let x =");

        Assert.False(result.IsSuccess);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void Parse_UnmatchedBraces_Recovers()
    {
        var source = """
            {
                let x = 5;
            let y = 10;
            """;
        var result = Parse(source);

        Assert.False(result.IsSuccess);
        // Should still attempt to parse what it can
        Assert.True(result.Errors.Count >= 1);
    }

    #endregion
}

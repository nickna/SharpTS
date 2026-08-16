using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.ParsingTests;

/// <summary>
/// Tests for triple-slash directive parsing in the lexer.
/// </summary>
public class TripleSlashDirectiveTests
{
    #region Valid Directives

    [Fact]
    public void Parses_PathReference_DoubleQuotes()
    {
        var source = """
            /// <reference path="./utils.ts" />
            let x: number = 1;
            """;

        var lexer = new Lexer(source);
        lexer.ScanTokens();

        Assert.Single(lexer.TripleSlashDirectives);
        var directive = lexer.TripleSlashDirectives[0];
        Assert.Equal(TripleSlashReferenceType.Path, directive.Type);
        Assert.Equal("./utils.ts", directive.Value);
        Assert.Equal(1, directive.Line);
    }

    [Fact]
    public void Parses_PathReference_SingleQuotes()
    {
        var source = """
            /// <reference path='./utils.ts' />
            let x: number = 1;
            """;

        var lexer = new Lexer(source);
        lexer.ScanTokens();

        Assert.Single(lexer.TripleSlashDirectives);
        var directive = lexer.TripleSlashDirectives[0];
        Assert.Equal(TripleSlashReferenceType.Path, directive.Type);
        Assert.Equal("./utils.ts", directive.Value);
    }

    [Fact]
    public void Parses_TypesReference()
    {
        var source = """
            /// <reference types="node" />
            let x: number = 1;
            """;

        var lexer = new Lexer(source);
        lexer.ScanTokens();

        Assert.Single(lexer.TripleSlashDirectives);
        var directive = lexer.TripleSlashDirectives[0];
        Assert.Equal(TripleSlashReferenceType.Types, directive.Type);
        Assert.Equal("node", directive.Value);
    }

    [Fact]
    public void Parses_LibReference()
    {
        var source = """
            /// <reference lib="es2020" />
            let x: number = 1;
            """;

        var lexer = new Lexer(source);
        lexer.ScanTokens();

        Assert.Single(lexer.TripleSlashDirectives);
        var directive = lexer.TripleSlashDirectives[0];
        Assert.Equal(TripleSlashReferenceType.Lib, directive.Type);
        Assert.Equal("es2020", directive.Value);
    }

    [Fact]
    public void Parses_NoDefaultLibReference()
    {
        var source = """
            /// <reference no-default-lib="true" />
            let x: number = 1;
            """;

        var lexer = new Lexer(source);
        lexer.ScanTokens();

        Assert.Single(lexer.TripleSlashDirectives);
        var directive = lexer.TripleSlashDirectives[0];
        Assert.Equal(TripleSlashReferenceType.NoDefaultLib, directive.Type);
        Assert.Equal("true", directive.Value);
    }

    [Fact]
    public void Parses_MultipleDirectives()
    {
        var source = """
            /// <reference path="./a.ts" />
            /// <reference path="./b.ts" />
            /// <reference path="./c.ts" />
            let x: number = 1;
            """;

        var lexer = new Lexer(source);
        lexer.ScanTokens();

        Assert.Equal(3, lexer.TripleSlashDirectives.Count);
        Assert.Equal("./a.ts", lexer.TripleSlashDirectives[0].Value);
        Assert.Equal("./b.ts", lexer.TripleSlashDirectives[1].Value);
        Assert.Equal("./c.ts", lexer.TripleSlashDirectives[2].Value);
    }

    [Fact]
    public void Parses_WithExtraWhitespace()
    {
        var source = """
            ///   <reference   path="./utils.ts"   />
            let x: number = 1;
            """;

        var lexer = new Lexer(source);
        lexer.ScanTokens();

        Assert.Single(lexer.TripleSlashDirectives);
        Assert.Equal("./utils.ts", lexer.TripleSlashDirectives[0].Value);
    }

    [Fact]
    public void Parses_AfterRegularComment()
    {
        var source = """
            // This is a regular comment
            /// <reference path="./utils.ts" />
            let x: number = 1;
            """;

        var lexer = new Lexer(source);
        lexer.ScanTokens();

        Assert.Single(lexer.TripleSlashDirectives);
        Assert.Equal("./utils.ts", lexer.TripleSlashDirectives[0].Value);
    }

    #endregion

    #region Ignored After Code

    [Fact]
    public void Ignores_DirectiveAfterCode()
    {
        var source = """
            let x: number = 1;
            /// <reference path="./utils.ts" />
            """;

        var lexer = new Lexer(source);
        lexer.ScanTokens();

        // Directive after code should be ignored (treated as regular comment)
        Assert.Empty(lexer.TripleSlashDirectives);
    }

    [Fact]
    public void Ignores_DirectiveAfterCodeOnSameLine()
    {
        var source = """
            let x: number = 1; /// <reference path="./utils.ts" />
            """;

        var lexer = new Lexer(source);
        lexer.ScanTokens();

        Assert.Empty(lexer.TripleSlashDirectives);
    }

    #endregion

    #region Error Cases

    [Fact]
    public void Throws_OnMissingClosingSlash()
    {
        var source = """
            /// <reference path="./utils.ts" >
            let x: number = 1;
            """;

        var lexer = new Lexer(source);
        var ex = Assert.Throws<Exception>(() => lexer.ScanTokens());
        Assert.Contains("Invalid triple-slash directive", ex.Message);
    }

    [Fact]
    public void Throws_OnMissingClosingBracket()
    {
        var source = """
            /// <reference path="./utils.ts" /
            let x: number = 1;
            """;

        var lexer = new Lexer(source);
        var ex = Assert.Throws<Exception>(() => lexer.ScanTokens());
        Assert.Contains("Invalid triple-slash directive", ex.Message);
    }

    [Fact]
    public void Throws_OnUnterminatedString()
    {
        var source = """
            /// <reference path="./utils.ts />
            let x: number = 1;
            """;

        var lexer = new Lexer(source);
        var ex = Assert.Throws<Exception>(() => lexer.ScanTokens());
        Assert.Contains("Unterminated string", ex.Message);
    }

    [Fact]
    public void Throws_OnUnknownAttribute()
    {
        var source = """
            /// <reference unknown="value" />
            let x: number = 1;
            """;

        var lexer = new Lexer(source);
        var ex = Assert.Throws<Exception>(() => lexer.ScanTokens());
        Assert.Contains("Invalid triple-slash directive", ex.Message);
        Assert.Contains("path", ex.Message);
    }

    [Fact]
    public void Throws_OnMissingEquals()
    {
        var source = """
            /// <reference path "./utils.ts" />
            let x: number = 1;
            """;

        var lexer = new Lexer(source);
        var ex = Assert.Throws<Exception>(() => lexer.ScanTokens());
        Assert.Contains("Expected '='", ex.Message);
    }

    [Fact]
    public void Throws_OnMissingQuotedValue()
    {
        var source = """
            /// <reference path=./utils.ts />
            let x: number = 1;
            """;

        var lexer = new Lexer(source);
        var ex = Assert.Throws<Exception>(() => lexer.ScanTokens());
        Assert.Contains("Expected quoted value", ex.Message);
    }

    #endregion

    #region Regular Comments Not Affected

    [Fact]
    public void RegularComments_StillWork()
    {
        var source = """
            // Regular comment
            /* Block comment */
            let x: number = 1;
            """;

        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();

        // Should have tokens for the code, not the comments
        Assert.Contains(tokens, t => t.Type == TokenType.LET);
        Assert.Empty(lexer.TripleSlashDirectives);
    }

    [Fact]
    public void TripleSlashComment_WithoutReference_IsRegularComment()
    {
        var source = """
            /// This is just a doc comment
            let x: number = 1;
            """;

        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();

        // Should not parse as a directive
        Assert.Empty(lexer.TripleSlashDirectives);
    }

    #endregion
}

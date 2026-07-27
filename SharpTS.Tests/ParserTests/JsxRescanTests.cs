using SharpTS.Diagnostics;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.ParserTests;

/// <summary>
/// End-to-end tests for JSX token-stream repair: text runs and attribute strings that the
/// upfront TypeScript lexer corrupts (apostrophes, comments, escapes, fused operators) must
/// parse faithfully via source rescanning + token splicing.
/// </summary>
public class JsxRescanTests
{
    private static ParseDiagnosticResult ParseTsx(string source) =>
        new Parser(new Lexer(source) { JsxTolerant = true }.ScanTokens())
            .WithJsx(source, JsxParseOptions.Default)
            .Parse();

    /// <summary>Digs the lowered JSX factory call out of `let view = &lt;jsx&gt;;`.</summary>
    private static Expr.Call JsxCallOf(ParseDiagnosticResult parsed)
    {
        Assert.True(parsed.IsSuccess, string.Join(Environment.NewLine, parsed.Diagnostics));
        var initializer = parsed.Statements.OfType<Stmt.Var>().First(v => v.Name.Lexeme == "view").Initializer;
        var call = Assert.IsType<Expr.Call>(initializer);
        Assert.NotNull(call.JsxOrigin);
        return call;
    }

    private static IReadOnlyList<Expr> Children(ParseDiagnosticResult parsed) =>
        JsxCallOf(parsed).JsxOrigin!.ChildExprs;

    private static string TextChild(ParseDiagnosticResult parsed, int index = 0)
    {
        var literal = Assert.IsType<Expr.Literal>(Children(parsed)[index]);
        return Assert.IsType<string>(literal.Value);
    }

    private static Expr AttributeValue(ParseDiagnosticResult parsed, string name)
    {
        var props = Assert.IsType<Expr.ObjectLiteral>(JsxCallOf(parsed).JsxOrigin!.PropsExpr);
        return props.Properties.First(p => p.Key is Expr.IdentifierKey k && k.Name.Lexeme == name).Value;
    }

    #region Text fidelity — corrupted upfront streams

    [Fact]
    public void ApostropheInTextDoesNotStartAStringLiteral()
    {
        var parsed = ParseTsx("let view = <p>don't stop</p>;");

        Assert.Equal("don't stop", TextChild(parsed));
    }

    [Fact]
    public void DoubleQuoteInTextIsPreserved()
    {
        var parsed = ParseTsx("let view = <p>she said \"hi\" twice</p>;");

        Assert.Equal("she said \"hi\" twice", TextChild(parsed));
    }

    [Fact]
    public void LineCommentLookalikeInTextIsPreserved()
    {
        var parsed = ParseTsx("let view = <a>https://example.com/x</a>;");

        Assert.Equal("https://example.com/x", TextChild(parsed));
    }

    [Fact]
    public void BlockCommentLookalikeInTextIsPreserved()
    {
        var parsed = ParseTsx("let view = <p>a /* not a comment */ b</p>;");

        Assert.Equal("a /* not a comment */ b", TextChild(parsed));
    }

    [Fact]
    public void HashInTextDoesNotThrow()
    {
        var parsed = ParseTsx("let view = <p>#1 fan</p>;");

        Assert.Equal("#1 fan", TextChild(parsed));
    }

    [Fact]
    public void UnknownCharactersInTextArePreserved()
    {
        var parsed = ParseTsx("let view = <p>© 2026 — naïve café</p>;");

        Assert.Equal("© 2026 — naïve café", TextChild(parsed));
    }

    [Fact]
    public void EntitiesAreDecodedInText()
    {
        var parsed = ParseTsx("let view = <p>&lt;tag&gt; &amp; &#65;</p>;");

        Assert.Equal("<tag> & A", TextChild(parsed));
    }

    [Fact]
    public void BacktickInTextDoesNotStartATemplate()
    {
        var parsed = ParseTsx("let view = <p>use `code` here</p>;");

        Assert.Equal("use `code` here", TextChild(parsed));
    }

    #endregion

    #region Whitespace semantics

    [Fact]
    public void SameLineWhitespaceIsPreserved()
    {
        var parsed = ParseTsx("let view = <b>  hi  </b>;");

        Assert.Equal("  hi  ", TextChild(parsed));
    }

    [Fact]
    public void MultiLineTextIsTrimmedAndJoined()
    {
        var parsed = ParseTsx("let view = <div>\n  hello\n  world\n</div>;");

        Assert.Equal("hello world", TextChild(parsed));
    }

    [Fact]
    public void WhitespaceOnlyRunWithNewlineContributesNoChild()
    {
        var parsed = ParseTsx("let view = <div>\n  <br/>\n</div>;");

        var children = Children(parsed);
        Assert.Single(children);
        Assert.IsNotType<Expr.Literal>(children[0]);
    }

    [Fact]
    public void TextAroundExpressionContainersKeepsAdjacentSpaces()
    {
        var parsed = ParseTsx("let view = <div>Hi {1}!</div>;");

        var children = Children(parsed);
        Assert.Equal(3, children.Count);
        Assert.Equal("Hi ", ((Expr.Literal)children[0]).Value);
        Assert.Equal("!", ((Expr.Literal)children[2]).Value);
    }

    #endregion

    #region Attribute strings

    [Fact]
    public void AttributeBackslashIsLiteral()
    {
        var parsed = ParseTsx("let view = <img alt=\"C:\\\" />;");

        var value = Assert.IsType<Expr.Literal>(AttributeValue(parsed, "alt"));
        Assert.Equal("C:\\", value.Value);
    }

    [Fact]
    public void AttributeEntitiesAreDecoded()
    {
        var parsed = ParseTsx("let view = <p title=\"a &amp; b\" />;");

        var value = Assert.IsType<Expr.Literal>(AttributeValue(parsed, "title"));
        Assert.Equal("a & b", value.Value);
    }

    [Fact]
    public void SingleQuotedAttributeAllowsDoubleQuotes()
    {
        var parsed = ParseTsx("let view = <p title='don\"t' />;");

        var value = Assert.IsType<Expr.Literal>(AttributeValue(parsed, "title"));
        Assert.Equal("don\"t", value.Value);
    }

    [Fact]
    public void MultiLineAttributeStringParses()
    {
        var parsed = ParseTsx("let view = <p title=\"line one\nline two\" />;");

        var value = Assert.IsType<Expr.Literal>(AttributeValue(parsed, "title"));
        Assert.Equal("line one\nline two", value.Value);
    }

    [Fact]
    public void NamespacedAndNumericAttributeNamesParse()
    {
        var parsed = ParseTsx("let view = <rect xlink:href=\"#a\" data-1=\"x\" />;");

        Assert.IsType<Expr.Literal>(AttributeValue(parsed, "xlink:href"));
        Assert.IsType<Expr.Literal>(AttributeValue(parsed, "data-1"));
    }

    #endregion

    #region Structure

    [Fact]
    public void FusedGreaterEqualAtTagCloseParses()
    {
        var parsed = ParseTsx("let view = <div>=5</div>;");

        Assert.Equal("=5", TextChild(parsed));
    }

    [Fact]
    public void BareGreaterInTextIsRecoverableError()
    {
        var parsed = ParseTsx("let view = <div>a > b</div>;");

        // tsc recovers: text keeps the '>', but TS1382 is reported.
        Assert.Contains(parsed.Diagnostics, d => d.TsCode == "TS1382");
    }

    [Fact]
    public void BareRightBraceInTextIsRecoverableError()
    {
        var parsed = ParseTsx("let view = <div>a } b</div>;");

        Assert.Contains(parsed.Diagnostics, d => d.TsCode == "TS1381");
    }

    [Fact]
    public void NestedElementsWithApostrophesInConditionalParse()
    {
        var parsed = ParseTsx("let view = <div>{true ? <a>can't</a> : <b>won't</b>}</div>;");

        Assert.True(parsed.IsSuccess, string.Join(Environment.NewLine, parsed.Diagnostics));
    }

    [Fact]
    public void JsxInsideTemplateInterpolationKeepsTemplateTailIntact()
    {
        var parsed = ParseTsx("let s = `a${<p>don't</p>}z`;\nlet after = 1;");

        Assert.True(parsed.IsSuccess, string.Join(Environment.NewLine, parsed.Diagnostics));
        Assert.Equal(2, parsed.Statements.OfType<Stmt.Var>().Count());
    }

    [Fact]
    public void CustomElementTagWithDashParses()
    {
        var parsed = ParseTsx("let view = <foo-bar prop=\"1\">x</foo-bar>;");

        Assert.Equal("x", TextChild(parsed));
    }

    [Fact]
    public void CommentContainerContributesNoChild()
    {
        var parsed = ParseTsx("let view = <div>{/* note */}</div>;");

        Assert.Empty(Children(parsed));
    }

    [Fact]
    public void SpreadChildParses()
    {
        var parsed = ParseTsx("let xs = [1]; let view = <div>{...xs}</div>;");

        Assert.IsType<Expr.Spread>(Children(parsed)[0]);
    }

    [Fact]
    public void UnclosedElementReportsTs17008()
    {
        var parsed = ParseTsx("let view = <div>never closed;");

        Assert.False(parsed.IsSuccess);
        Assert.Contains(parsed.Diagnostics, d => d.TsCode == "TS17008");
    }

    [Fact]
    public void UnclosedFragmentReportsTs17014()
    {
        var parsed = ParseTsx("let view = <>never closed;");

        Assert.False(parsed.IsSuccess);
        Assert.Contains(parsed.Diagnostics, d => d.TsCode == "TS17014");
    }

    [Fact]
    public void MismatchedClosingTagReportsTs17002()
    {
        var parsed = ParseTsx("let view = <div>x</span>;");

        Assert.False(parsed.IsSuccess);
        Assert.Contains(parsed.Diagnostics, d => d.TsCode == "TS17002");
    }

    [Fact]
    public void CodeAfterCorruptedTextStillParses()
    {
        // The apostrophe corruption is repaired by splicing; the statements after the
        // element must survive untouched.
        var parsed = ParseTsx("let view = <p>it's fine</p>;\nlet a = 1;\nlet b = a + 1;");

        Assert.True(parsed.IsSuccess, string.Join(Environment.NewLine, parsed.Diagnostics));
        Assert.Equal(3, parsed.Statements.OfType<Stmt.Var>().Count());
    }

    #endregion
}

using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.ParserTests;

/// <summary>Unit tests for the source-driven JSX text/attribute scanner.</summary>
public class JsxTextTests
{
    #region ScanText

    [Fact]
    public void ScanText_StopsAtElementTerminator()
    {
        var scan = JsxText.ScanText("hello<br/>", 0, 1);

        Assert.Equal("hello", scan.Raw);
        Assert.Equal(5, scan.EndOffset);
        Assert.Equal('<', scan.Terminator);
        Assert.Null(scan.Errors);
    }

    [Fact]
    public void ScanText_StopsAtExpressionContainer()
    {
        var scan = JsxText.ScanText("Hi {name}", 0, 1);

        Assert.Equal("Hi ", scan.Raw);
        Assert.Equal('{', scan.Terminator);
    }

    [Fact]
    public void ScanText_ReportsEofWithNulTerminator()
    {
        var scan = JsxText.ScanText("dangling", 0, 1);

        Assert.Equal('\0', scan.Terminator);
        Assert.Equal(8, scan.EndOffset);
    }

    [Fact]
    public void ScanText_CollectsBareGreaterAndBraceErrors()
    {
        var scan = JsxText.ScanText("a > b } c<", 0, 3);

        Assert.NotNull(scan.Errors);
        Assert.Equal(2, scan.Errors!.Count);
        Assert.Equal('>', scan.Errors[0].Character);
        Assert.Equal('}', scan.Errors[1].Character);
        Assert.Equal(3, scan.Errors[0].Line);
    }

    [Fact]
    public void ScanText_TracksLinesAcrossNewlines()
    {
        var scan = JsxText.ScanText("a\nb\nc<", 0, 1);

        Assert.Equal(3, scan.EndLine);
    }

    #endregion

    #region CookChildText

    [Theory]
    [InlineData("hello", "hello")]
    [InlineData("  hi  ", "  hi  ")]                       // same-line whitespace preserved
    [InlineData(" ", " ")]                                 // whitespace-only, no newline: kept
    [InlineData("\n  hello\n  world\n", "hello world")]    // multi-line: trimmed + joined
    [InlineData("\n   \n", null)]                          // whitespace-only with newline: dropped
    [InlineData("\n", null)]
    [InlineData("Hi \n there", "Hi there")]
    [InlineData("one\n\n\ntwo", "one two")]                // blank interior lines dropped
    [InlineData("a\r\nb", "a b")]                          // CRLF normalized
    public void CookChildText_AppliesJsxTrimRules(string raw, string? expected)
    {
        Assert.Equal(expected, JsxText.CookChildText(raw));
    }

    [Fact]
    public void CookChildText_DecodesEntitiesAfterTrimming()
    {
        // &#32; is a space — decoding after trimming keeps it.
        Assert.Equal(" x ", JsxText.CookChildText("&#32;x&#32;"));
        Assert.Equal("\u00A0", JsxText.CookChildText("&nbsp;"));
    }

    #endregion

    #region DecodeEntities

    [Theory]
    [InlineData("a &amp; b", "a & b")]
    [InlineData("&lt;div&gt;", "<div>")]
    [InlineData("&quot;q&quot; &apos;a&apos;", "\"q\" 'a'")]
    [InlineData("&#65;&#x42;&#x63;", "ABc")]
    [InlineData("&copy; &rarr; &mdash;", "© → —")]
    [InlineData("&unknown; stays", "&unknown; stays")]     // unknown named: verbatim
    [InlineData("a & b", "a & b")]                         // bare ampersand: verbatim
    [InlineData("&;", "&;")]                               // empty reference: verbatim
    [InlineData("&#xZZ;", "&#xZZ;")]                       // malformed numeric: verbatim
    [InlineData("&#1114112;", "&#1114112;")]               // out of Unicode range: verbatim
    [InlineData("no entities", "no entities")]
    public void DecodeEntities_DecodesKnownAndPreservesUnknown(string input, string expected)
    {
        Assert.Equal(expected, JsxText.DecodeEntities(input));
    }

    #endregion

    #region CookAttributeValue

    [Fact]
    public void CookAttributeValue_ReadsDoubleQuotedValue()
    {
        const string source = "x=\"hello\" y";
        var scan = JsxText.CookAttributeValue(source, 2, 1);

        Assert.Equal("hello", scan.Value);
        Assert.Equal(8, scan.EndOffset);
        Assert.Equal('"', source[scan.EndOffset]);
    }

    [Fact]
    public void CookAttributeValue_BackslashIsLiteral()
    {
        // JSX strings have no escapes: "C:\" ends at the second quote.
        const string source = "\"C:\\\"";
        var scan = JsxText.CookAttributeValue(source, 0, 1);

        Assert.Equal("C:\\", scan.Value);
        Assert.Equal(4, scan.EndOffset);
    }

    [Fact]
    public void CookAttributeValue_SingleQuotesAllowEmbeddedDoubleQuotes()
    {
        const string source = "'don\"t'";
        var scan = JsxText.CookAttributeValue(source, 0, 1);

        Assert.Equal("don\"t", scan.Value);
    }

    [Fact]
    public void CookAttributeValue_SpansNewlinesAndCountsThem()
    {
        const string source = "\"a\nb\"";
        var scan = JsxText.CookAttributeValue(source, 0, 1);

        Assert.Equal("a\nb", scan.Value);
        Assert.Equal(2, scan.EndLine);
    }

    [Fact]
    public void CookAttributeValue_DecodesEntities()
    {
        var scan = JsxText.CookAttributeValue("\"&lt;3&amp;\"", 0, 1);

        Assert.Equal("<3&", scan.Value);
    }

    [Fact]
    public void CookAttributeValue_UnterminatedThrows()
    {
        var ex = Assert.Throws<ParseError>(() => JsxText.CookAttributeValue("\"never ends", 0, 1));
        Assert.Equal("TS1002", ex.TsCode);
    }

    #endregion
}

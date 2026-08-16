using Xunit;

namespace SharpTS.TypeScriptConformance;

public class ErrorsBaselineParserTests
{
    [Fact]
    public void ParsesSingleHeader()
    {
        var src = "foo.ts(3,5): error TS2322: Type 'string' is not assignable to type 'number'.\n";
        var diags = ErrorsBaselineParser.Parse(src);
        Assert.Single(diags);
        Assert.Equal(3, diags[0].Line);
        Assert.Equal("TS2322", diags[0].TsCode);
    }

    [Fact]
    public void IgnoresIndentedContinuationLines()
    {
        // The continuation lines explain the same diagnostic — they are not
        // separate diagnostics. Only headers get one entry.
        var src =
            "foo.ts(3,5): error TS2322: Type 'T' is not assignable to type 'NonNullable<T>'.\n" +
            "  Type 'T' is not assignable to type '{}'.\n" +
            "    Type 'string | undefined' is not assignable to type '{}'.\n" +
            "foo.ts(7,9): error TS2339: Property 'x' does not exist on type 'Foo'.\n";
        var diags = ErrorsBaselineParser.Parse(src);
        Assert.Equal(2, diags.Count);
        Assert.Equal(("TS2322", 3), (diags[0].TsCode, diags[0].Line));
        Assert.Equal(("TS2339", 7), (diags[1].TsCode, diags[1].Line));
    }

    [Fact]
    public void HandlesMultiFileTestFilePaths()
    {
        // Multi-file tests reference virtual filenames in the baseline.
        var src = "./a.ts(1,1): error TS2304: Cannot find name 'foo'.\n";
        var diags = ErrorsBaselineParser.Parse(src);
        Assert.Single(diags);
        Assert.Equal("TS2304", diags[0].TsCode);
    }

    [Fact]
    public void EmptyContent_ReturnsEmptyList()
    {
        Assert.Empty(ErrorsBaselineParser.Parse(""));
        Assert.Empty(ErrorsBaselineParser.Parse("\n\n"));
    }

    [Fact]
    public void NonHeaderContent_ReturnsEmptyList()
    {
        // A baseline with only commentary (no header lines) is technically
        // valid — yields zero diagnostics, not a parse failure.
        var src = "Some commentary that doesn't match the header pattern.\n";
        Assert.Empty(ErrorsBaselineParser.Parse(src));
    }
}

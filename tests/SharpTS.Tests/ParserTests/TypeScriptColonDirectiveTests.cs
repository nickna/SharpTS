using SharpTS.Compilation;
using SharpTS.Diagnostics;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.ParserTests;

public sealed class TypeScriptColonDirectiveTests
{
    // Diagnostic outcomes are pinned to TypeScript 6.0.3.
    [Theory]
    [InlineData("ts-expect-error: reason")]
    [InlineData("ts-expect-error:reason")]
    [InlineData("ts-ignore: reason")]
    [InlineData("ts-expect-error reason")]
    public void DescriptionSuppressesTheNextLineOnly(string directive)
    {
        Assert.Empty(Errors($"// @{directive}\nconst value:number='wrong';"));
        var error = Assert.Single(Errors($"// @{directive}\nconst value:number='wrong';\nconst after:number='wrong';"));
        Assert.Equal("TS2322", error.TsCode);
        Assert.Equal(3, error.Line);
    }

    [Fact]
    public void UnusedColonExpectationStillReportsTs2578()
    {
        var error = Assert.Single(Errors("// @ts-expect-error: reason\nconst value:number=1;"));
        Assert.Equal("TS2578", error.TsCode);
        Assert.Equal(1, error.Line);
    }

    [Theory]
    [InlineData("note @ts-expect-error: reason")]
    [InlineData("@ts-expect-err: reason")]
    [InlineData("@ts-check-ignore: reason")]
    [InlineData("@ts-nocheck-ignore: reason")]
    public void MisleadingPrefixesDoNotSuppressDiagnostics(string comment)
    {
        string source = $"// {comment}\nconst value:number='wrong';";
        var error = Assert.Single(Errors(source));
        Assert.Equal("TS2322", error.TsCode);
        Assert.Equal(2, error.Line);
        var lexer = new Lexer(source);
        lexer.ScanTokens();
        Assert.False(lexer.Pragmas.HasTsNoCheck);
        Assert.Empty(lexer.Pragmas.IgnoreLines);
        Assert.Empty(lexer.Pragmas.ExpectErrorLines);
    }

    private static Diagnostic[] Errors(string source) =>
        SingleSourceAnalyzer.Analyze(source, new(DecoratorMode.None, "directive.ts"))
            .Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
}

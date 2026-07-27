using SharpTS.Diagnostics;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.ParserTests;

public class JsxParserTests
{
    /// <summary>Parses in the TSX dialect, the way ModuleResolver does for .tsx files.</summary>
    private static ParseDiagnosticResult ParseTsx(string source, JsxParseOptions? options = null) =>
        new Parser(new Lexer(source) { JsxTolerant = true }.ScanTokens())
            .WithJsx(source, options ?? JsxParseOptions.Default)
            .Parse();

    [Fact]
    public void ParsesNestedElementsFragmentsAttributesAndExpressions()
    {
        const string source = """
            const name = "world";
            function Greeting(props: any) { return props; }
            const view = <>
                <section className="hero" data-id={1}>
                    <Greeting enabled>{name}</Greeting>
                    <br />
                </section>
            </>;
            """;

        var parsed = ParseTsx(source);

        Assert.True(parsed.IsSuccess, string.Join(Environment.NewLine, parsed.Diagnostics));
        Assert.Empty(new TypeChecker(maxErrors: 50)
            .CheckWithRecovery(parsed.Statements)
            .Diagnostics);
    }

    [Fact]
    public void AngleBracketTypeAssertionsStillParseInTsDialect()
    {
        var parsed = new Parser(new Lexer("const n = <number>1;").ScanTokens()).Parse();

        Assert.True(parsed.IsSuccess, string.Join(Environment.NewLine, parsed.Diagnostics));
    }

    [Fact]
    public void JsxIsASyntaxErrorInTsDialect()
    {
        var parsed = new Parser(new Lexer("const view = <div>hi</div>;").ScanTokens()).Parse();

        Assert.False(parsed.IsSuccess);
    }

    [Fact]
    public void JsxModeNoneReportsTs17004()
    {
        var parsed = ParseTsx("const view = <div />;",
            JsxParseOptions.Default with { Mode = JsxMode.None });

        Assert.False(parsed.IsSuccess);
        Assert.Contains(parsed.Diagnostics, d => d.TsCode == "TS17004");
    }

    [Fact]
    public void MismatchedClosingTagIsAParseError()
    {
        var parsed = ParseTsx("const view = <div></span>;");

        Assert.False(parsed.IsSuccess);
    }

    [Fact]
    public void IntrinsicAttributesUseJsxNamespaceWhenAvailable()
    {
        const string source = """
            declare namespace JSX {
                interface IntrinsicElements {
                    button: { disabled?: boolean };
                }
            }
            const view = <button disabled="wrong" />;
            """;
        var parsed = ParseTsx(source);
        Assert.True(parsed.IsSuccess, string.Join(Environment.NewLine, parsed.Diagnostics));

        var diagnostics = new TypeChecker(maxErrors: 50)
            .CheckWithRecovery(parsed.Statements)
            .Diagnostics;

        Assert.Contains(diagnostics, d => d.TsCode == "TS1360");
    }
}

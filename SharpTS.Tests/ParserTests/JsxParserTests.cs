using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.ParserTests;

public class JsxParserTests
{
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

        var parsed = new Parser(new Lexer(source).ScanTokens()).Parse();

        Assert.True(parsed.IsSuccess, string.Join(Environment.NewLine, parsed.Diagnostics));
        Assert.Empty(new TypeChecker(maxErrors: 50)
            .CheckWithRecovery(parsed.Statements)
            .Diagnostics);
    }

    [Fact]
    public void AngleBracketTypeAssertionsStillParse()
    {
        var parsed = new Parser(new Lexer("const n = <number>1;").ScanTokens()).Parse();

        Assert.True(parsed.IsSuccess, string.Join(Environment.NewLine, parsed.Diagnostics));
    }

    [Fact]
    public void MismatchedClosingTagIsAParseError()
    {
        var parsed = new Parser(new Lexer("const view = <div></span>;").ScanTokens()).Parse();

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
        var parsed = new Parser(new Lexer(source).ScanTokens()).Parse();
        Assert.True(parsed.IsSuccess, string.Join(Environment.NewLine, parsed.Diagnostics));

        var diagnostics = new TypeChecker(maxErrors: 50)
            .CheckWithRecovery(parsed.Statements)
            .Diagnostics;

        Assert.Contains(diagnostics, d => d.TsCode == "TS1360");
    }
}

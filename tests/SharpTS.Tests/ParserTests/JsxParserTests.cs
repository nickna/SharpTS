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
            .WithMaxErrors(1000)
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
    public void PreserveModeDoesNotResolveSyntheticFactory()
    {
        var parsed = ParseTsx("""
            declare namespace JSX { interface IntrinsicElements { div: {}; } }
            <div />;
            """, new JsxParseOptions(JsxMode.Preserve));
        Assert.True(parsed.IsSuccess, string.Join(Environment.NewLine, parsed.Diagnostics));
        var statement = Assert.IsType<Stmt.Expression>(parsed.Statements.Last());
        var call = Assert.IsType<Expr.Call>(statement.Expr);
        Assert.NotNull(call.JsxOrigin);
        Assert.Equal(JsxMode.Preserve, call.JsxOrigin!.Mode);

        var diagnostics = new TypeChecker(maxErrors: 50)
            .CheckWithRecovery(parsed.Statements)
            .Diagnostics;

        Assert.True(
            diagnostics.All(d => d.TsCode != "TS2304"),
            string.Join(Environment.NewLine, diagnostics));
    }

    [Fact]
    public void CommaAttributeExpressionReportsExactTsCodes()
    {
        var parsed = ParseTsx("const view = <div value={left, right} />;",
            new JsxParseOptions(JsxMode.Preserve));

        Assert.Contains(parsed.Diagnostics, d => d.Line == 1 && d.TsCode == "TS2695");
        Assert.Contains(parsed.Diagnostics, d => d.Line == 1 && d.TsCode == "TS18007");
    }

    [Fact]
    public void ImmediateSpreadAttributeExpressionReportsExactTsCodes()
    {
        var parsed = ParseTsx("""
            declare const React: any
            declare namespace JSX {
                interface IntrinsicElements { [key: string]: any }
            }
            const Widget: any
            const source: any
            <Widget value={...source} />
            """,
            new JsxParseOptions(JsxMode.Preserve));

        Assert.Contains(parsed.Diagnostics, d => d.Line == 7 && d.TsCode == "TS1109");
        Assert.Contains(parsed.Diagnostics, d => d.Line == 7 && d.TsCode == "TS1003");
    }

    [Fact]
    public void EmptyAttributeInitializerBeforeTagEndRecoversWithoutDiagnostic()
    {
        var parsed = ParseTsx("const view = <div value= />;",
            new JsxParseOptions(JsxMode.Preserve));

        Assert.True(parsed.IsSuccess, string.Join(Environment.NewLine, parsed.Diagnostics));
    }

    [Fact]
    public void ClosingTagWhereChildExpressionExpectedReportsTs1109AndRetainsElement()
    {
        var parsed = ParseTsx("const view = <div>{ </div>;",
            new JsxParseOptions(JsxMode.Preserve));

        Assert.Contains(parsed.Diagnostics, d => d.Line == 1 && d.TsCode == "TS1109");
        Assert.Contains(parsed.Statements, statement => statement is Stmt.Const);
    }

    [Fact]
    public void AdjacentJsxRootsReportTs2657AndRetainBothElements()
    {
        var parsed = ParseTsx("const view = <div></div><span></span>;",
            new JsxParseOptions(JsxMode.Preserve));

        Assert.Contains(parsed.Diagnostics, d => d.Line == 1 && d.TsCode == "TS2657");
        var declaration = Assert.IsType<Stmt.Const>(parsed.Statements.Single());
        Assert.IsType<Expr.Comma>(declaration.Initializer);
    }

    [Fact]
    public void AdjacentJsxRootStatementsRetainDiagnosticsForBothLines()
    {
        var parsed = ParseTsx("""
            declare namespace JSX { interface Element { } }

            <div></div>
            <span></span>
            """, new JsxParseOptions(JsxMode.Preserve));

        Assert.Contains(parsed.Diagnostics, d => d.Line == 3 && d.TsCode == "TS2657");
        var statement = Assert.IsType<Stmt.Expression>(parsed.Statements.Last());
        var roots = Assert.IsType<Expr.Comma>(statement.Expr);
        Assert.NotNull(Assert.IsType<Expr.Call>(roots.Left).JsxOrigin);
        Assert.NotNull(Assert.IsType<Expr.Call>(roots.Right).JsxOrigin);

        var diagnostics = new TypeChecker(new TypeCheckerOptions
            {
                NoImplicitAny = true,
                MaxErrors = 50,
            })
            .CheckWithRecovery(parsed.Statements)
            .Diagnostics;

        Assert.Contains(diagnostics, d => d.Line == 3 && d.TsCode == "TS7026");
        Assert.Contains(diagnostics, d => d.Line == 4 && d.TsCode == "TS7026");
    }

    [Fact]
    public void ClassicModeMissingFactoryReportsTs2874()
    {
        var parsed = ParseTsx("<div />;", new JsxParseOptions(JsxMode.React));
        Assert.True(parsed.IsSuccess, string.Join(Environment.NewLine, parsed.Diagnostics));

        var diagnostics = new TypeChecker(new TypeCheckerOptions
            {
                NoImplicitAny = false,
                MaxErrors = 50,
            })
            .CheckWithRecovery(parsed.Statements)
            .Diagnostics;

        Assert.Contains(diagnostics, d => d.Line == 1 && d.TsCode == "TS2874");
        Assert.DoesNotContain(diagnostics, d => d.TsCode == "TS2304");
    }

    [Fact]
    public void AmbiguousSingleTypeParameterArrowsCommitToJsx()
    {
        var parsed = ParseTsx("""
            var T: any;
            var x1 = <T>() => {}</T>;
            var x2 = <T extends={true}>() => {}</T>;
            var x3 = <T extends>() => {}</T>;
            """, new JsxParseOptions(JsxMode.Preserve));

        Assert.True(
            new[] { 2, 3, 4 }.All(line =>
                parsed.Diagnostics.Any(d => d.Line == line && d.TsCode == "TS1382")),
            string.Join(Environment.NewLine, parsed.Diagnostics));
    }

    [Fact]
    public void LessThanComparisonInsideTsxMethodDoesNotCommitToJsx()
    {
        var parsed = ParseTsx("""
            class View {
                props: any;
                render() {
                    if (this.props.id < 1) {
                        return <div />;
                    }
                }
            }
            """, new JsxParseOptions(JsxMode.Preserve));

        Assert.True(parsed.IsSuccess, string.Join(Environment.NewLine, parsed.Diagnostics));
    }

    [Fact]
    public void UnicodeEscapesInJsxNamesReportEachOpeningTagOrAttribute()
    {
        var parsed = ParseTsx("""
            ; <\u0061></a>
            ; <x.\u0076ideo />
            ; <\u{0061}></a>
            ; <\u{0061}-b></a-b>
            ; <a-\u{0063}></a-c>
            ; <Comp\u{0061} x={12} />
            ; <video data-\u0076ideo />
            ; <video \u0073rc="" />
            """, new JsxParseOptions(JsxMode.Preserve));

        Assert.True(
            Enumerable.Range(1, 8).All(line =>
                parsed.Diagnostics.Any(d => d.Line == line && d.TsCode == "TS17021")),
            string.Join(Environment.NewLine, parsed.Diagnostics));
    }

    [Theory]
    [InlineData("// comment\nlet x = <div><span></div>;\n", "2:TS17008")]
    [InlineData("let x = <div></span>;\n", "1:TS17002")]
    [InlineData("let x = <div><div></span>;\n", "1:TS17002,1:TS17008,2:TS1005")]
    [InlineData("let x = <div>;\n\n", "1:TS17008,3:TS1005")]
    [InlineData("let x = <div><span>\n\n", "1:TS17008,3:TS1005")]
    public void MismatchedClosingTagsRecoverAtTheOwningElement(
        string source,
        string expected)
    {
        var parsed = ParseTsx(source, new JsxParseOptions(JsxMode.Preserve));
        string actual = string.Join(',', parsed.Diagnostics
            .Where(d => d.TsCode is not null)
            .Select(d => $"{d.Line}:{d.TsCode}")
            .Distinct()
            .OrderBy(value => value, StringComparer.Ordinal));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void MalformedTypeAssertionLikeJsxRetainsTheRecoveredRoot()
    {
        var parsed = ParseTsx("""
            declare var createElement: any;
            class foo {}
            var x: any;
            x = <any> { test: <any></any> };
            x = <any><any></any>;
            x = <foo>hello {<foo>{}} </foo>;
            x = <foo test={<foo>{}}>hello</foo>;
            x = <foo test={<foo>{}}>hello{<foo>{}}</foo>;
            x = <foo>x</foo>, x = <foo/>;
            <foo>{<foo><foo>{/foo/.test(x) ? <foo><foo></foo> : <foo><foo></foo>}</foo>}</foo>
            """, new JsxParseOptions(JsxMode.Preserve));

        Assert.True(parsed.Statements.Count >= 4,
            $"statements={parsed.Statements.Count}: " +
            string.Join(", ", parsed.Statements.Select(statement => statement.GetType().Name)) +
            Environment.NewLine + string.Join(Environment.NewLine, parsed.Diagnostics));
    }

    [Fact]
    public void MismatchedClosingTagIsAParseError()
    {
        var parsed = ParseTsx("const view = <div></span>;");

        Assert.False(parsed.IsSuccess);
    }

    [Fact]
    public void NamespacedClosingTagsAndNumericEntitiesSurviveUpfrontLexing()
    {
        var parsed = ParseTsx("""
            const first = <svg:path>&#0123;</svg:path>;
            const second = <svg : path></svg : path>;
            """);

        Assert.True(parsed.IsSuccess, string.Join(Environment.NewLine, parsed.Diagnostics));
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

        // tsc's code for a bad intrinsic attribute type (was TS1360 under the old
        // satisfies-based interim lowering).
        Assert.Contains(diagnostics, d => d.TsCode == "TS2322");
    }
}

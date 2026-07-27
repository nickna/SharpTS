using SharpTS.Diagnostics;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.ParserTests;

/// <summary>
/// Per-file JSX pragmas: lexer scanning (@jsx/@jsxFrag/@jsxImportSource/@jsxRuntime),
/// merge semantics over project options, and the end-to-end custom-factory path.
/// </summary>
public class JsxPragmaTests
{
    private static TypeScriptPragmas Lex(string source)
    {
        var lexer = new Lexer(source) { JsxTolerant = true };
        lexer.ScanTokens();
        return lexer.Pragmas;
    }

    [Fact]
    public void BlockCommentPragmasAreScanned()
    {
        var pragmas = Lex("""
            /** @jsx h @jsxFrag HFragment */
            let a = 1;
            """);

        Assert.Equal("h", pragmas.JsxFactory);
        Assert.Equal("HFragment", pragmas.JsxFragmentFactory);
    }

    [Fact]
    public void LineCommentPragmasAreScanned()
    {
        var pragmas = Lex("""
            // @jsxImportSource preact
            // @jsxRuntime automatic
            let a = 1;
            """);

        Assert.Equal("preact", pragmas.JsxImportSource);
        Assert.Equal("automatic", pragmas.JsxRuntime);
    }

    [Fact]
    public void PragmasAfterFirstCodeTokenAreIgnored()
    {
        var pragmas = Lex("""
            let a = 1;
            /** @jsx h */
            """);

        Assert.Null(pragmas.JsxFactory);
    }

    [Fact]
    public void JsxNamePrefixesDoNotCrossMatch()
    {
        var pragmas = Lex("/** @jsxes nothing */\nlet a = 1;");

        Assert.Null(pragmas.JsxFactory);
    }

    [Fact]
    public void ApplyPragmas_JsxForcesClassicWithFactory()
    {
        var options = JsxParseOptions.Default.ApplyPragmas(
            Lex("/** @jsx h */\nlet a = 1;"));

        Assert.Equal(JsxMode.React, options.Mode);
        Assert.Equal("h", options.Factory);
        Assert.True(options.FactoryFromPragma);
    }

    [Fact]
    public void ApplyPragmas_RuntimeSwitchesMode()
    {
        var classic = JsxParseOptions.Default.ApplyPragmas(
            Lex("/** @jsxRuntime classic */\nlet a = 1;"));
        Assert.Equal(JsxMode.React, classic.Mode);

        var automatic = new JsxParseOptions(JsxMode.React).ApplyPragmas(
            Lex("/** @jsxRuntime automatic */\nlet a = 1;"));
        Assert.Equal(JsxMode.ReactJsx, automatic.Mode);
    }

    [Fact]
    public void ApplyPragmas_ImportSourceOverrides()
    {
        var options = JsxParseOptions.Default.ApplyPragmas(
            Lex("/** @jsxImportSource preact */\nlet a = 1;"));

        Assert.Equal("preact", options.ImportSource);
        Assert.Equal(JsxMode.ReactJsx, options.Mode);
    }

    [Fact]
    public void ApplyPragmas_NoneModeStaysNone()
    {
        var options = new JsxParseOptions(JsxMode.None).ApplyPragmas(
            Lex("/** @jsx h */\nlet a = 1;"));

        Assert.Equal(JsxMode.None, options.Mode);
        Assert.Equal("h", options.Factory);
    }

    [Fact]
    public void FragmentWithInlineFactoryPragmaIsTs17017()
    {
        const string source = """
            /** @jsx h */
            declare function h(...args: any[]): any;
            let view = <>x</>;
            """;
        var lexer = new Lexer(source) { JsxTolerant = true };
        var tokens = lexer.ScanTokens();
        var parsed = new Parser(tokens)
            .WithJsx(source, JsxParseOptions.Default.ApplyPragmas(lexer.Pragmas))
            .Parse();

        Assert.False(parsed.IsSuccess);
        Assert.Contains(parsed.Diagnostics, d => d.TsCode == "TS17017");
    }

    [Fact]
    public void FragmentWithBothPragmasIsLegal()
    {
        const string source = """
            /** @jsx h @jsxFrag HFrag */
            declare function h(...args: any[]): any;
            declare const HFrag: any;
            let view = <>x</>;
            """;
        var lexer = new Lexer(source) { JsxTolerant = true };
        var tokens = lexer.ScanTokens();
        var parsed = new Parser(tokens)
            .WithJsx(source, JsxParseOptions.Default.ApplyPragmas(lexer.Pragmas))
            .Parse();

        Assert.True(parsed.IsSuccess, string.Join(Environment.NewLine, parsed.Diagnostics));
        var view = parsed.Statements.OfType<Stmt.Var>().First(v => v.Name.Lexeme == "view");
        var call = Assert.IsType<Expr.Call>(view.Initializer);
        Assert.Equal("h", ((Expr.Variable)call.Callee).Name.Lexeme);
        Assert.Equal("HFrag", ((Expr.Variable)call.Arguments[0]).Name.Lexeme);
    }

    [Theory, ModeData]
    public void CustomFactoryPragmaRunsEndToEnd(ExecutionMode mode)
    {
        var output = TestHarness.RunModules(new Dictionary<string, string>
        {
            ["main.tsx"] = """
                /** @jsx h */
                function h(type: any, props: any, ...children: any[]): any {
                    return { tag: type, props: props, children: children };
                }
                const el = <div id="a">hi</div>;
                console.log(el.tag + "|" + el.props.id + "|" + el.children[0]);
                """,
        }, "main.tsx", mode);

        Assert.Contains("div|a|hi", output);
    }
}

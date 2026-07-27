using SharpTS.Diagnostics;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.ParserTests;

/// <summary>
/// Asserts the exact desugared AST shapes the JSX transform produces per jsx mode:
/// classic factory calls, automatic jsx/jsxs with children folding and key extraction,
/// the dev-runtime signature, and the synthesized runtime import.
/// </summary>
public class JsxTransformTests
{
    private static readonly JsxParseOptions Classic = new(JsxMode.React);

    private static ParseDiagnosticResult ParseTsx(string source, JsxParseOptions? options = null) =>
        new Parser(new Lexer(source) { JsxTolerant = true }.ScanTokens())
            .WithJsx(source, options ?? JsxParseOptions.Default)
            .Parse();

    private static Expr.Call FirstJsxCall(ParseDiagnosticResult parsed)
    {
        Assert.True(parsed.IsSuccess, string.Join(Environment.NewLine, parsed.Diagnostics));
        var initializer = parsed.Statements.OfType<Stmt.Var>().First(v => v.Name.Lexeme == "view").Initializer;
        var call = Assert.IsType<Expr.Call>(initializer);
        Assert.NotNull(call.JsxOrigin);
        return call;
    }

    private static string VariableName(Expr expr) => Assert.IsType<Expr.Variable>(expr).Name.Lexeme;

    private static Expr.ObjectLiteral Props(Expr.Call call) =>
        Assert.IsType<Expr.ObjectLiteral>(call.JsxOrigin!.PropsExpr);

    private static Expr? PropValue(Expr.ObjectLiteral props, string name) =>
        props.Properties.FirstOrDefault(p => p.Key is Expr.IdentifierKey k && k.Name.Lexeme == name)?.Value;

    #region Classic mode

    [Fact]
    public void Classic_IntrinsicLowersToCreateElementCall()
    {
        var call = FirstJsxCall(ParseTsx("let view = <div id=\"a\">x</div>;", Classic));

        var callee = Assert.IsType<Expr.Get>(call.Callee);
        Assert.Equal("React", VariableName(callee.Object));
        Assert.Equal("createElement", callee.Name.Lexeme);

        Assert.Equal(3, call.Arguments.Count);
        Assert.Equal("div", Assert.IsType<Expr.Literal>(call.Arguments[0]).Value);
        Assert.Same(call.JsxOrigin!.PropsExpr, call.Arguments[1]);
        Assert.Equal("x", Assert.IsType<Expr.Literal>(call.Arguments[2]).Value);
        Assert.Equal(JsxElementKind.Intrinsic, call.JsxOrigin.Kind);
        Assert.Equal(JsxMode.React, call.JsxOrigin.Mode);
    }

    [Fact]
    public void Classic_CustomFactoryIsHonored()
    {
        var options = Classic with { Factory = "h", FragmentFactory = "HFragment" };
        var call = FirstJsxCall(ParseTsx("let view = <p/>;", options));

        Assert.Equal("h", VariableName(call.Callee));
    }

    [Fact]
    public void Classic_ComponentWithoutAttributesPassesNullProps()
    {
        var call = FirstJsxCall(ParseTsx("declare function Foo(p?: any): any;\nlet view = <Foo/>;", Classic));

        Assert.Null(Assert.IsType<Expr.Literal>(call.Arguments[1]).Value);
        Assert.Null(call.JsxOrigin!.PropsExpr);
        Assert.Equal(JsxElementKind.Component, call.JsxOrigin.Kind);
    }

    [Fact]
    public void Classic_IntrinsicWithoutAttributesKeepsEmptyPropsObject()
    {
        // Deliberate deviation from tsc's `null` emit: an empty object literal keeps
        // required-prop checking alive; runtime-equivalent for createElement.
        var call = FirstJsxCall(ParseTsx("let view = <br/>;", Classic));

        Assert.Empty(Props(call).Properties);
    }

    [Fact]
    public void Classic_FragmentUsesFragmentFactory()
    {
        var call = FirstJsxCall(ParseTsx("let view = <>text</>;", Classic));

        var fragment = Assert.IsType<Expr.Get>(call.Arguments[0]);
        Assert.Equal("React", VariableName(fragment.Object));
        Assert.Equal("Fragment", fragment.Name.Lexeme);
        Assert.Equal(JsxElementKind.Fragment, call.JsxOrigin!.Kind);
    }

    [Fact]
    public void Classic_KeyStaysInProps()
    {
        var call = FirstJsxCall(ParseTsx("let k = 1;\nlet view = <div key={k}/>;", Classic));

        Assert.Equal(2, call.Arguments.Count);
        Assert.NotNull(PropValue(Props(call), "key"));
        Assert.Null(call.JsxOrigin!.KeyExpr);
    }

    [Fact]
    public void Classic_DoesNotInjectRuntimeImport()
    {
        var parsed = ParseTsx("let view = <div/>;", Classic);

        Assert.DoesNotContain(parsed.Statements, s => s is Stmt.Import);
    }

    #endregion

    #region Automatic mode

    [Fact]
    public void Automatic_SingleChildUsesJsxWithChildrenProp()
    {
        var call = FirstJsxCall(ParseTsx("let view = <p>only</p>;"));

        Assert.Equal("__sharpts_jsx", VariableName(call.Callee));
        Assert.Equal(2, call.Arguments.Count);
        var children = Assert.IsType<Expr.Literal>(PropValue(Props(call), "children"));
        Assert.Equal("only", children.Value);
    }

    [Fact]
    public void Automatic_MultipleChildrenUseJsxsWithArray()
    {
        var call = FirstJsxCall(ParseTsx("let view = <p>one<br/>two</p>;"));

        Assert.Equal("__sharpts_jsxs", VariableName(call.Callee));
        var children = Assert.IsType<Expr.ArrayLiteral>(PropValue(Props(call), "children"));
        Assert.Equal(3, children.Elements.Count);
        // JsxOrigin children are aliases of the folded expressions.
        Assert.Same(children.Elements[0], call.JsxOrigin!.ChildExprs[0]);
    }

    [Fact]
    public void Automatic_SpreadChildForcesJsxs()
    {
        var call = FirstJsxCall(ParseTsx("let xs = [1];\nlet view = <ul>{...xs}</ul>;"));

        Assert.Equal("__sharpts_jsxs", VariableName(call.Callee));
        var children = Assert.IsType<Expr.ArrayLiteral>(PropValue(Props(call), "children"));
        Assert.IsType<Expr.Spread>(Assert.Single(children.Elements));
    }

    [Fact]
    public void Automatic_KeyIsExtractedToThirdArgument()
    {
        var call = FirstJsxCall(ParseTsx("let k = \"id\";\nlet view = <li key={k} value={1}/>;"));

        Assert.Equal(3, call.Arguments.Count);
        Assert.Equal("k", VariableName(call.Arguments[2]));
        Assert.Same(call.Arguments[2], call.JsxOrigin!.KeyExpr);
        Assert.Null(PropValue(Props(call), "key"));
        Assert.NotNull(PropValue(Props(call), "value"));
    }

    [Fact]
    public void Automatic_FragmentUsesImportedFragment()
    {
        var parsed = ParseTsx("let view = <>a</>;");
        var call = FirstJsxCall(parsed);

        Assert.Equal("__sharpts_Fragment", VariableName(call.Arguments[0]));

        var import = Assert.IsType<Stmt.Import>(parsed.Statements.First(s => s is Stmt.Import));
        Assert.Contains(import.NamedImports!, s => s.Imported.Lexeme == "Fragment");
    }

    [Fact]
    public void Automatic_InjectsRuntimeImportWithOnlyUsedNames()
    {
        var parsed = ParseTsx("let view = <p>only</p>;");

        var import = Assert.IsType<Stmt.Import>(parsed.Statements.First(s => s is Stmt.Import));
        Assert.True(import.IsSynthesizedJsxRuntime);
        Assert.Equal("react/jsx-runtime", import.ModulePath);
        var specifier = Assert.Single(import.NamedImports!);
        Assert.Equal("jsx", specifier.Imported.Lexeme);
        Assert.Equal("__sharpts_jsx", specifier.LocalName!.Lexeme);
    }

    [Fact]
    public void Automatic_CustomImportSourceIsHonored()
    {
        var parsed = ParseTsx("let view = <p/>;",
            JsxParseOptions.Default with { ImportSource = "preact" });

        var import = Assert.IsType<Stmt.Import>(parsed.Statements.First(s => s is Stmt.Import));
        Assert.Equal("preact/jsx-runtime", import.ModulePath);
    }

    [Fact]
    public void Automatic_NoJsxMeansNoImport()
    {
        var parsed = ParseTsx("let a = 1;");

        Assert.DoesNotContain(parsed.Statements, s => s is Stmt.Import);
    }

    #endregion

    #region Dev mode

    [Fact]
    public void Dev_UsesJsxDevSignatureAndDevRuntimeImport()
    {
        var parsed = ParseTsx("let view = <p>x</p>;",
            JsxParseOptions.Default with { Mode = JsxMode.ReactJsxDev });
        var call = FirstJsxCall(parsed);

        Assert.Equal("__sharpts_jsxDEV", VariableName(call.Callee));
        // jsxDEV(type, props, key, isStaticChildren, source, this)
        Assert.Equal(6, call.Arguments.Count);
        Assert.IsType<Expr.ObjectLiteral>(call.Arguments[4]);

        var import = Assert.IsType<Stmt.Import>(parsed.Statements.First(s => s is Stmt.Import));
        Assert.Equal("react/jsx-dev-runtime", import.ModulePath);
        Assert.Contains(import.NamedImports!, s => s.Imported.Lexeme == "jsxDEV");
    }

    #endregion
}

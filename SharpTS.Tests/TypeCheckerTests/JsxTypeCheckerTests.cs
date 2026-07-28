using SharpTS.Diagnostics;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Unit tests for the checker's JSX pipeline (TypeChecker.Jsx.cs). JSX calls are hand-built
/// with <see cref="Expr.Call.JsxOrigin"/> — the shape the parser's factory-call desugaring
/// produces — so the pipeline is testable independently of the transform.
/// </summary>
public class JsxTypeCheckerTests
{
    private const string JsxPrelude = """
        declare const _jsx: any;
        declare function Greeting(props: { name: string; excited?: boolean }): JSX.Element;
        declare namespace JSX {
            interface Element { __jsxElementBrand: string }
            interface IntrinsicElements {
                button: { disabled?: boolean; label: string };
                div: { id?: string };
            }
            interface IntrinsicAttributes { key?: string | number }
        }
        """;

    private static Token Identifier(string name, int line = 1) =>
        new(TokenType.IDENTIFIER, name, null, line);

    private static Expr.Property Attribute(string name, Expr value, int line = 1) =>
        new(new Expr.IdentifierKey(Identifier(name, line)), value);

    private static Expr.Call JsxCall(
        JsxElementKind kind,
        string? tagName,
        Expr tagExpression,
        Expr.ObjectLiteral? props,
        params Expr[] children)
    {
        var arguments = new List<Expr> { tagExpression };
        arguments.Add(props ?? (Expr)new Expr.Literal(null));
        arguments.AddRange(children);
        return new Expr.Call(
            new Expr.Variable(Identifier("_jsx")),
            Identifier("_jsx"),
            null,
            arguments)
        {
            JsxOrigin = new JsxCallInfo(kind, tagName, props, children, null, JsxMode.ReactJsx, 1),
        };
    }

    private static TypeCheckDiagnosticResult Check(
        Expr.Call call, string prelude = JsxPrelude, TypeCheckerOptions? options = null)
    {
        var parsed = new Parser(new Lexer(prelude).ScanTokens()).Parse();
        Assert.True(parsed.IsSuccess, string.Join(Environment.NewLine, parsed.Diagnostics));
        var statements = new List<Stmt>(parsed.Statements) { new Stmt.Expression(call) };
        var checker = options is null ? new TypeChecker(maxErrors: 50) : new TypeChecker(options);
        return checker.CheckWithRecovery(statements);
    }

    [Fact]
    public void ValidIntrinsic_NoDiagnostics_TypesAsJsxElement()
    {
        var call = JsxCall(JsxElementKind.Intrinsic, "button", new Expr.Literal("button"),
            new Expr.ObjectLiteral([Attribute("label", new Expr.Literal("go"))]));

        var result = Check(call);

        Assert.Empty(result.Diagnostics);
        var callType = result.TypeMap.Get(call);
        Assert.IsType<TypeInfo.Interface>(callType);
        Assert.Equal("Element", ((TypeInfo.Interface)callType!).Name);
    }

    [Fact]
    public void UnknownIntrinsicTag_ReportsTs2339()
    {
        var call = JsxCall(JsxElementKind.Intrinsic, "dvi", new Expr.Literal("dvi"),
            new Expr.ObjectLiteral([]));

        var result = Check(call);

        Assert.Contains(result.Diagnostics, d => d.TsCode == "TS2339" && d.Message.Contains("'dvi'"));
    }

    [Fact]
    public void IntrinsicAttributeTypeMismatch_ReportsTs2322()
    {
        var call = JsxCall(JsxElementKind.Intrinsic, "button", new Expr.Literal("button"),
            new Expr.ObjectLiteral([
                Attribute("label", new Expr.Literal("ok")),
                Attribute("disabled", new Expr.Literal("wrong")),
            ]));

        var result = Check(call);

        Assert.Contains(result.Diagnostics, d => d.TsCode == "TS2322");
    }

    [Fact]
    public void UnknownIntrinsicAttribute_ReportsTs2322DoesNotExist()
    {
        // `id` overlaps the props type, so the weak-type rule (TS2559) does not preempt the
        // specific unknown-attribute elaboration.
        var call = JsxCall(JsxElementKind.Intrinsic, "div", new Expr.Literal("div"),
            new Expr.ObjectLiteral([
                Attribute("id", new Expr.Literal("x")),
                Attribute("unknownAttr", new Expr.Literal(1)),
            ]));

        var result = Check(call);

        Assert.Contains(result.Diagnostics,
            d => d.TsCode == "TS2322" && d.Message.Contains("'unknownAttr' does not exist"));
    }

    [Fact]
    public void WeakTypeNoOverlap_ReportsTs2559()
    {
        var call = JsxCall(JsxElementKind.Intrinsic, "div", new Expr.Literal("div"),
            new Expr.ObjectLiteral([Attribute("unknownAttr", new Expr.Literal(1))]));

        var result = Check(call);

        Assert.Contains(result.Diagnostics, d => d.TsCode == "TS2559");
    }

    [Fact]
    public void HyphenatedAndNamespacedAttributesAreExempt()
    {
        var call = JsxCall(JsxElementKind.Intrinsic, "div", new Expr.Literal("div"),
            new Expr.ObjectLiteral([
                Attribute("data-test", new Expr.Literal("x")),
                Attribute("aria-hidden", new Expr.Literal(true)),
                Attribute("xlink:href", new Expr.Literal("#a")),
            ]));

        var result = Check(call);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void KeyAttributeIsAlwaysLegal()
    {
        var call = JsxCall(JsxElementKind.Intrinsic, "div", new Expr.Literal("div"),
            new Expr.ObjectLiteral([Attribute("key", new Expr.Literal(1))]));

        Assert.Empty(Check(call).Diagnostics);
    }

    [Fact]
    public void OneMissingRequiredProp_ReportsTs2741()
    {
        var call = JsxCall(JsxElementKind.Intrinsic, "button", new Expr.Literal("button"),
            new Expr.ObjectLiteral([]));

        var result = Check(call);

        Assert.Contains(result.Diagnostics,
            d => d.TsCode == "TS2741" && d.Message.Contains("'label'"));
    }

    [Fact]
    public void SeveralMissingRequiredProps_ReportTs2739()
    {
        const string prelude = """
            declare const _jsx: any;
            declare namespace JSX {
                interface Element { __jsxElementBrand: string }
                interface IntrinsicElements {
                    widget: { alpha: string; beta: number };
                }
            }
            """;
        var call = JsxCall(JsxElementKind.Intrinsic, "widget", new Expr.Literal("widget"),
            new Expr.ObjectLiteral([]));

        var result = Check(call, prelude);

        Assert.Contains(result.Diagnostics,
            d => d.TsCode == "TS2739" && d.Message.Contains("alpha") && d.Message.Contains("beta"));
    }

    [Fact]
    public void NoIntrinsicElementsInterface_Ts7026_OnlyUnderNoImplicitAny()
    {
        const string prelude = """
            declare const _jsx: any;
            declare namespace JSX {
                interface Element { __jsxElementBrand: string }
            }
            """;
        var call = JsxCall(JsxElementKind.Intrinsic, "div", new Expr.Literal("div"),
            new Expr.ObjectLiteral([]));

        var strict = Check(call, prelude,
            new TypeCheckerOptions { NoImplicitAny = true, MaxErrors = 50 });
        Assert.Contains(strict.Diagnostics, d => d.TsCode == "TS7026");

        var lax = Check(call, prelude);
        Assert.Empty(lax.Diagnostics);
    }

    [Fact]
    public void NoJsxNamespaceAtAll_Ts2602_UnderNoImplicitAny()
    {
        const string prelude = "declare const _jsx: any;";
        var call = JsxCall(JsxElementKind.Intrinsic, "div", new Expr.Literal("div"),
            new Expr.ObjectLiteral([]));

        var strict = Check(call, prelude,
            new TypeCheckerOptions { NoImplicitAny = true, MaxErrors = 50 });

        Assert.Contains(strict.Diagnostics, d => d.TsCode == "TS2602");
        Assert.Contains(strict.Diagnostics, d => d.TsCode == "TS7026");
    }

    [Fact]
    public void FunctionComponent_ValidProps_NoDiagnostics()
    {
        var call = JsxCall(JsxElementKind.Component, "Greeting",
            new Expr.Variable(Identifier("Greeting")),
            new Expr.ObjectLiteral([Attribute("name", new Expr.Literal("world"))]));

        Assert.Empty(Check(call).Diagnostics);
    }

    [Fact]
    public void FunctionComponent_PropMismatch_ReportsTs2322()
    {
        var call = JsxCall(JsxElementKind.Component, "Greeting",
            new Expr.Variable(Identifier("Greeting")),
            new Expr.ObjectLiteral([Attribute("name", new Expr.Literal(42))]));

        var result = Check(call);

        Assert.Contains(result.Diagnostics, d => d.TsCode == "TS2322");
    }

    [Fact]
    public void FunctionComponent_MissingRequiredProp_ReportsTs2741()
    {
        var call = JsxCall(JsxElementKind.Component, "Greeting",
            new Expr.Variable(Identifier("Greeting")),
            new Expr.ObjectLiteral([]));

        var result = Check(call);

        Assert.Contains(result.Diagnostics,
            d => d.TsCode == "TS2741" && d.Message.Contains("'name'"));
    }

    [Fact]
    public void FunctionComponent_BadReturnType_ReportsTs2786()
    {
        const string prelude = JsxPrelude + """

            declare function NotAComponent(props: { a?: string }): number;
            """;
        var call = JsxCall(JsxElementKind.Component, "NotAComponent",
            new Expr.Variable(Identifier("NotAComponent")),
            new Expr.ObjectLiteral([]));

        var result = Check(call, prelude);

        Assert.Contains(result.Diagnostics, d => d.TsCode == "TS2786");
    }

    [Fact]
    public void NonCallableTag_ReportsTs2604()
    {
        const string prelude = JsxPrelude + """

            declare const JustAString: string;
            """;
        var call = JsxCall(JsxElementKind.Component, "JustAString",
            new Expr.Variable(Identifier("JustAString")),
            new Expr.ObjectLiteral([]));

        var result = Check(call, prelude);

        Assert.Contains(result.Diagnostics, d => d.TsCode == "TS2604");
    }

    [Fact]
    public void UnresolvedComponentIdentifier_ReportsTs2304()
    {
        var call = JsxCall(JsxElementKind.Component, "Missing",
            new Expr.Variable(Identifier("Missing")),
            new Expr.ObjectLiteral([]));

        var result = Check(call);

        Assert.Contains(result.Diagnostics, d => d.TsCode == "TS2304");
    }

    [Fact]
    public void Fragment_ChecksChildrenAndTypesAsElement()
    {
        var call = JsxCall(JsxElementKind.Fragment, null,
            new Expr.Variable(Identifier("_jsx")),  // any stand-in fragment expr that resolves
            new Expr.ObjectLiteral([]));

        var result = Check(call);

        Assert.Empty(result.Diagnostics);
        Assert.IsType<TypeInfo.Interface>(result.TypeMap.Get(call));
    }

    [Fact]
    public void NoFactorySignatureErrors_EvenWithManyArguments()
    {
        // The general call path (TS2554 arity) must never fire for JSX-origin calls:
        // _jsx is declared as taking any args, but even a mismatched arg list is the JSX
        // pipeline's business, not overload resolution's.
        var call = JsxCall(JsxElementKind.Intrinsic, "div", new Expr.Literal("div"),
            new Expr.ObjectLiteral([]),
            new Expr.Literal("child1"), new Expr.Literal("child2"), new Expr.Literal("child3"));

        var result = Check(call);

        Assert.DoesNotContain(result.Diagnostics, d => d.TsCode == "TS2554");
        Assert.DoesNotContain(result.Diagnostics, d => d.TsCode == "TS2345");
    }
}

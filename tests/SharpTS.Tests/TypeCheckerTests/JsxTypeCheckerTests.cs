using SharpTS.Diagnostics;
using SharpTS.Modules;
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

    private static Expr.Property Spread(Expr value) => new(null, value, IsSpread: true);

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
    public void DirectAttributeNoOverlap_ReportsTs2322()
    {
        var call = JsxCall(JsxElementKind.Intrinsic, "div", new Expr.Literal("div"),
            new Expr.ObjectLiteral([Attribute("unknownAttr", new Expr.Literal(1))]));

        var result = Check(call);

        Assert.Contains(result.Diagnostics, d => d.TsCode == "TS2322");
        Assert.DoesNotContain(result.Diagnostics, d => d.TsCode == "TS2559");
    }

    [Fact]
    public void SpreadOnlyWeakTypeNoOverlap_ReportsTs2559()
    {
        const string prelude = JsxPrelude + """

            declare const source: { unknownAttr: number };
            """;
        var call = JsxCall(JsxElementKind.Intrinsic, "div", new Expr.Literal("div"),
            new Expr.ObjectLiteral([Spread(new Expr.Variable(Identifier("source")))]));

        var result = Check(call, prelude);

        Assert.Contains(result.Diagnostics, d => d.TsCode == "TS2559");
    }

    [Fact]
    public void AnySpreadSuppressesRequiredAndExcessAttributeDiagnostics()
    {
        const string prelude = JsxPrelude + """

            declare const source: any;
            """;
        var call = JsxCall(JsxElementKind.Intrinsic, "button", new Expr.Literal("button"),
            new Expr.ObjectLiteral([Spread(new Expr.Variable(Identifier("source")))]));

        Assert.Empty(Check(call, prelude).Diagnostics);
    }

    [Fact]
    public void LaterRequiredSpread_ReportsOverwrittenDirectAttribute()
    {
        const string prelude = JsxPrelude + """

            declare const source: { label: string };
            """;
        var call = JsxCall(JsxElementKind.Intrinsic, "button", new Expr.Literal("button"),
            new Expr.ObjectLiteral([
                Attribute("label", new Expr.Literal("first"), line: 4),
                Spread(new Expr.Variable(Identifier("source"))),
            ]));

        var diagnostic = Assert.Single(Check(call, prelude).Diagnostics,
            item => item.TsCode == "TS2783");
        Assert.Equal(4, diagnostic.Location?.Line);
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
    public void DeclaredHyphenatedAttributeStillUsesItsDeclaredType()
    {
        const string prelude = """
            declare const _jsx: any;
            declare namespace JSX {
                interface Element {}
                interface IntrinsicElements { widget: { "data-count"?: number } }
            }
            """;
        var call = JsxCall(JsxElementKind.Intrinsic, "widget", new Expr.Literal("widget"),
            new Expr.ObjectLiteral([Attribute("data-count", new Expr.Literal("wrong"))]));

        Assert.Contains(Check(call, prelude).Diagnostics, diagnostic => diagnostic.TsCode == "TS2322");
    }

    [Fact]
    public void KeyAttributeIsAlwaysLegal()
    {
        var call = JsxCall(JsxElementKind.Intrinsic, "div", new Expr.Literal("div"),
            new Expr.ObjectLiteral([Attribute("key", new Expr.Literal(1))]));

        Assert.Empty(Check(call).Diagnostics);
    }

    [Fact]
    public void KeyAttributeUsesIntrinsicAttributesType()
    {
        var call = JsxCall(JsxElementKind.Intrinsic, "div", new Expr.Literal("div"),
            new Expr.ObjectLiteral([Attribute("key", new Expr.Literal(true))]));

        Assert.Contains(Check(call).Diagnostics, d => d.TsCode == "TS2322");
    }

    [Fact]
    public void ChildrenAndRefUseDeclaredPropTypes()
    {
        const string prelude = JsxPrelude + """

            declare function RefComponent(props: { ref?: { id: string }; children: string }): JSX.Element;
            """;
        var call = JsxCall(JsxElementKind.Component, "RefComponent",
            new Expr.Variable(Identifier("RefComponent")),
            new Expr.ObjectLiteral([
                Attribute("ref", new Expr.Literal(42)),
                Attribute("children", new Expr.Literal(42)),
            ]));

        TypeCheckDiagnosticResult result = Check(call, prelude);
        Assert.True(result.Diagnostics.Count(d => d.TsCode == "TS2322") >= 2);
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
    public void NoJsxNamespaceAtAll_DoesNotReportLegacyTs2602()
    {
        const string prelude = "declare const _jsx: any;";
        var call = JsxCall(JsxElementKind.Intrinsic, "div", new Expr.Literal("div"),
            new Expr.ObjectLiteral([]));

        var strict = Check(call, prelude,
            new TypeCheckerOptions { NoImplicitAny = true, MaxErrors = 50 });

        Assert.DoesNotContain(strict.Diagnostics, d => d.TsCode == "TS2602");
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
    public void ClassicFactory_RequiresRootButNotCreateElementMember()
    {
        const string prelude = "declare namespace React {}";
        var call = new Expr.Call(
            new Expr.Get(new Expr.Variable(Identifier("React")), Identifier("createElement")),
            Identifier("("),
            null,
            [new Expr.Literal("div"), new Expr.ObjectLiteral([])])
        {
            JsxOrigin = new JsxCallInfo(
                JsxElementKind.Intrinsic,
                "div",
                new Expr.ObjectLiteral([]),
                [],
                null,
                JsxMode.React,
                1),
        };

        Assert.DoesNotContain(Check(call, prelude).Diagnostics, d => d.TsCode == "TS2694");

        var missingRoot = call with
        {
            Callee = new Expr.Get(
                new Expr.Variable(Identifier("MissingReact")),
                Identifier("createElement")),
        };
        Assert.Contains(Check(missingRoot, "").Diagnostics, d => d.TsCode == "TS2874");
    }

    [Fact]
    public void ClassicFragment_MissingSharedFactoryRoot_ReportsBothJsxDiagnostics()
    {
        var react = new Expr.Variable(Identifier("React"));
        var call = new Expr.Call(
            new Expr.Get(react, Identifier("createElement")),
            Identifier("("),
            null,
            [
                new Expr.Get(new Expr.Variable(Identifier("React")), Identifier("Fragment")),
                new Expr.Literal(null),
            ])
        {
            JsxOrigin = new JsxCallInfo(
                JsxElementKind.Fragment,
                null,
                null,
                [],
                null,
                JsxMode.React,
                1),
        };

        var diagnostics = Check(call, "").Diagnostics;

        Assert.Contains(diagnostics, d => d.TsCode == "TS2874");
        Assert.Contains(diagnostics, d => d.TsCode == "TS2879");
    }

    [Fact]
    public void InlineFactoryFragmentFallback_ReportsBothJsxDiagnosticsEndToEnd()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "sharpts-jsx-fragment-fallback"));
        string renderer = Path.Combine(root, "renderer.d.ts");
        string entry = Path.Combine(root, "index.tsx");
        var files = new Dictionary<string, string>
        {
            [renderer] = "export function dom(): void;",
            [entry] = """
                /** @jsx dom */
                import { dom } from "./renderer";
                <><h></h></>;
                """,
        };
        var resolver = new ModuleResolver(entry, files, TypeScriptProgramOptions.Disabled)
        {
            JsxOptions = new JsxParseOptions(JsxMode.React),
            RecoverParseErrors = true,
        };
        ParsedModule program = resolver.LoadProgram(entry);
        var outerFragment = Assert.IsType<Expr.Call>(
            Assert.IsType<Stmt.Expression>(program.Statements.Last()).Expr);
        Assert.Equal(JsxElementKind.Fragment, outerFragment.JsxOrigin?.Kind);
        Assert.Equal(
            "React",
            Assert.IsType<Expr.Variable>(Assert.IsType<Expr.Get>(outerFragment.Callee).Object).Name.Lexeme);
        Assert.Equal(
            "React",
            Assert.IsType<Expr.Variable>(Assert.IsType<Expr.Get>(outerFragment.Arguments[0]).Object).Name.Lexeme);
        var checker = new TypeChecker(new TypeCheckerOptions { MaxErrors = 50 });

        checker.CheckModules(resolver.GetModulesInOrder(program), resolver);
        var diagnostics = program.ParseDiagnostics.Concat(checker.GetDiagnostics()).ToList();

        Assert.Contains(diagnostics, d => d.TsCode == "TS17017");
        Assert.Contains(diagnostics, d => d.TsCode == "TS2874");
        Assert.Contains(diagnostics, d => d.TsCode == "TS2879");
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
    public void DirectExcessAttributePreemptsMissingRequiredDiagnostic()
    {
        var call = JsxCall(JsxElementKind.Component, "Greeting",
            new Expr.Variable(Identifier("Greeting")),
            new Expr.ObjectLiteral([Attribute("naaame", new Expr.Literal("world"))]));

        TypeCheckDiagnosticResult result = Check(call);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.TsCode == "TS2322");
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.TsCode == "TS2741");
    }

    [Fact]
    public void MissingSpreadPropPreemptsSecondaryValueMismatch()
    {
        const string prelude = JsxPrelude + """

            declare const source: { excited: string };
            """;
        var call = JsxCall(JsxElementKind.Component, "Greeting",
            new Expr.Variable(Identifier("Greeting")),
            new Expr.ObjectLiteral([Spread(new Expr.Variable(Identifier("source")))]));

        TypeCheckDiagnosticResult result = Check(call, prelude);

        Assert.Single(result.Diagnostics, diagnostic => diagnostic.TsCode == "TS2741");
    }

    [Fact]
    public void ClassComponent_UsesPropsInheritedThroughGenericBase()
    {
        const string prelude = """
            declare const _jsx: any;
            declare namespace JSX {
                interface Element { __jsxElementBrand: string }
                interface ElementClass { render(): JSX.Element }
                interface ElementAttributesProperty { props: {} }
            }
            class Component<P> {
                props: P;
                constructor(props: P) { this.props = props; }
                render(): JSX.Element { return {} as JSX.Element; }
            }
            class Widget extends Component<{ label: string }> {}
            """;
        var valid = JsxCall(JsxElementKind.Component, "Widget",
            new Expr.Variable(Identifier("Widget")),
            new Expr.ObjectLiteral([Attribute("label", new Expr.Literal("ok"))]));
        var invalid = JsxCall(JsxElementKind.Component, "Widget",
            new Expr.Variable(Identifier("Widget")),
            new Expr.ObjectLiteral([Attribute("label", new Expr.Literal(42))]));

        Assert.Empty(Check(valid, prelude).Diagnostics);
        Assert.Contains(Check(invalid, prelude).Diagnostics, diagnostic => diagnostic.TsCode == "TS2322");
    }

    [Fact]
    public void ClassComponent_UsesPropsInheritedThroughAmbientGenericBase()
    {
        const string prelude = """
            declare const _jsx: any;
            declare namespace JSX {
                interface Element { __jsxElementBrand: string }
                interface ElementClass { render(): JSX.Element }
                interface ElementAttributesProperty { props: {} }
            }
            declare class Component<P> {
                props: P & { children?: any };
                render(): JSX.Element;
            }
            class Widget extends Component<{ label: string }> {}
            """;
        var valid = JsxCall(JsxElementKind.Component, "Widget",
            new Expr.Variable(Identifier("Widget")),
            new Expr.ObjectLiteral([Attribute("label", new Expr.Literal("ok"))]));
        var invalid = JsxCall(JsxElementKind.Component, "Widget",
            new Expr.Variable(Identifier("Widget")),
            new Expr.ObjectLiteral([Attribute("label", new Expr.Literal(42))]));

        Assert.Empty(Check(valid, prelude).Diagnostics);
        Assert.Contains(Check(invalid, prelude).Diagnostics, diagnostic => diagnostic.TsCode == "TS2322");
    }

    [Fact]
    public void ExportAssignmentSeesReferencedScriptNamespace()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "sharpts-jsx-export-assignment"));
        string declarations = Path.Combine(root, "globals.d.ts");
        string legacyModule = Path.Combine(root, "legacy.d.ts");
        string entry = Path.Combine(root, "index.tsx");
        var files = new Dictionary<string, string>
        {
            [declarations] = """
                namespace Legacy {
                    export const marker: { label: string };
                }
                """,
            [legacyModule] = """
                /// <reference path="./globals.d.ts" />
                export = Legacy;
                """,
            [entry] = """
                import Legacy = require("./legacy");
                const good: string = Legacy.marker.label;
                const bad: number = Legacy.marker.label;
                """,
        };
        var resolver = new ModuleResolver(entry, files, TypeScriptProgramOptions.Default with { Lib = [] })
        {
            JsxOptions = new JsxParseOptions(JsxMode.Preserve),
            RecoverParseErrors = true,
        };
        ParsedModule program = resolver.LoadProgram(entry);
        var modules = resolver.GetModulesInOrder(program);
        ParsedModule exportingModule = resolver.LoadModule(legacyModule);
        modules = modules
            .OrderBy(module => ReferenceEquals(module, exportingModule) ? 0 : 1)
            .ToList();
        var checker = new TypeChecker(new TypeCheckerOptions { MaxErrors = 50 });

        checker.CheckModules(modules, resolver);
        var diagnostics = program.ParseDiagnostics.Concat(checker.GetDiagnostics()).ToList();

        Assert.DoesNotContain(diagnostics,
            diagnostic => diagnostic.TsCode == "TS2339" && diagnostic.Message.Contains("marker"));
        Assert.Single(diagnostics, diagnostic => diagnostic.TsCode == "TS2322");
    }

    [Fact]
    public void FunctionComponent_IntersectionPropsAreChecked()
    {
        const string prelude = JsxPrelude + """

            declare function Intersected(props: { label: string } & { optional?: number }): JSX.Element;
            """;
        var valid = JsxCall(JsxElementKind.Component, "Intersected",
            new Expr.Variable(Identifier("Intersected")),
            new Expr.ObjectLiteral([Attribute("label", new Expr.Literal("ok"))]));
        var invalid = JsxCall(JsxElementKind.Component, "Intersected",
            new Expr.Variable(Identifier("Intersected")),
            new Expr.ObjectLiteral([Attribute("label", new Expr.Literal(42))]));

        Assert.Empty(Check(valid, prelude).Diagnostics);
        Assert.Contains(Check(invalid, prelude).Diagnostics, diagnostic => diagnostic.TsCode == "TS2322");
    }

    [Fact]
    public void FunctionComponent_UnionInsideIntersectionSelectsCompatiblePropsBranch()
    {
        const string prelude = JsxPrelude + """

            interface Canadian { street: string; postalCode: string }
            interface American { street: string; zipCode: string }
            declare function Address(props: (Canadian | American) & { children?: any }): JSX.Element;
            """;
        var canadian = JsxCall(JsxElementKind.Component, "Address",
            new Expr.Variable(Identifier("Address")), new Expr.ObjectLiteral([
                Attribute("street", new Expr.Literal("Main")),
                Attribute("postalCode", new Expr.Literal("A1A")),
            ]));
        var american = JsxCall(JsxElementKind.Component, "Address",
            new Expr.Variable(Identifier("Address")), new Expr.ObjectLiteral([
                Attribute("street", new Expr.Literal("Main")),
                Attribute("zipCode", new Expr.Literal("12345")),
            ]));

        Assert.Empty(Check(canadian, prelude).Diagnostics);
        Assert.Empty(Check(american, prelude).Diagnostics);
    }

    [Fact]
    public void DiscriminatedUnionProps_ReportMissingMemberFromSelectedBranch()
    {
        const string prelude = JsxPrelude + """

            type TextProps = { editable: false }
                           | { editable: true; onEdit: (value: string) => void };
            declare function TextComponent(props: TextProps & { children?: any }): JSX.Element;
            """;
        var call = JsxCall(JsxElementKind.Component, "TextComponent",
            new Expr.Variable(Identifier("TextComponent")),
            new Expr.ObjectLiteral([Attribute("editable", new Expr.Literal(true))]));

        Assert.Contains(Check(call, prelude).Diagnostics, diagnostic => diagnostic.TsCode == "TS2322");
    }

    [Fact]
    public void DiscriminatedUnionProps_AcceptNarrowedSpreadBranches()
    {
        const string prelude = JsxPrelude + """

            type TextProps = { editable: false }
                           | { editable: true; onEdit: (value: string) => void };
            declare function TextComponent(props: TextProps & { children?: any }): JSX.Element;
            declare const falseProps: { editable: false };
            declare const trueProps: { editable: true; onEdit: (value: string) => void };
            """;
        var falseCall = JsxCall(JsxElementKind.Component, "TextComponent",
            new Expr.Variable(Identifier("TextComponent")),
            new Expr.ObjectLiteral([Spread(new Expr.Variable(Identifier("falseProps")))]));
        var trueCall = JsxCall(JsxElementKind.Component, "TextComponent",
            new Expr.Variable(Identifier("TextComponent")),
            new Expr.ObjectLiteral([Spread(new Expr.Variable(Identifier("trueProps")))]));

        Assert.Empty(Check(falseCall, prelude).Diagnostics);
        Assert.Empty(Check(trueCall, prelude).Diagnostics);
    }

    [Fact]
    public void ConstructorSignatureComponent_UsesInstanceShapeForEmptyAttributesMarker()
    {
        const string prelude = """
            declare const _jsx: any;
            declare namespace JSX {
                interface Element {}
                interface ElementAttributesProperty {}
            }
            interface WidgetConstructor { new (name: string): { label?: string } }
            declare const Widget: WidgetConstructor;
            """;
        var valid = JsxCall(JsxElementKind.Component, "Widget",
            new Expr.Variable(Identifier("Widget")),
            new Expr.ObjectLiteral([Attribute("label", new Expr.Literal("ok"))]));
        var invalid = JsxCall(JsxElementKind.Component, "Widget",
            new Expr.Variable(Identifier("Widget")),
            new Expr.ObjectLiteral([Attribute("other", new Expr.Literal(1))]));

        Assert.Empty(Check(valid, prelude).Diagnostics);
        Assert.Contains(Check(invalid, prelude).Diagnostics, diagnostic => diagnostic.TsCode == "TS2322");
    }

    [Fact]
    public void ConstructorSignatureComponent_MissingNamedAttributesPropertyReportsTs2607()
    {
        const string prelude = """
            declare const _jsx: any;
            declare namespace JSX {
                interface Element {}
                interface ElementAttributesProperty { props: any }
            }
            interface WidgetConstructor { new (name: string): { label?: string } }
            declare const Widget: WidgetConstructor;
            """;
        var call = JsxCall(JsxElementKind.Component, "Widget",
            new Expr.Variable(Identifier("Widget")),
            new Expr.ObjectLiteral([Attribute("label", new Expr.Literal("ok"))]));

        Assert.Contains(Check(call, prelude).Diagnostics, diagnostic => diagnostic.TsCode == "TS2607");
    }

    [Fact]
    public void ClassComponent_MissingNamedAttributesPropertyReportsTs2607()
    {
        const string prelude = """
            declare const _jsx: any;
            declare namespace JSX {
                interface Element {}
                interface ElementAttributesProperty { props: any }
            }
            class Widget { render(): JSX.Element { return {} as JSX.Element; } }
            """;
        var call = JsxCall(JsxElementKind.Component, "Widget",
            new Expr.Variable(Identifier("Widget")), new Expr.ObjectLiteral([]));

        Assert.Contains(Check(call, prelude).Diagnostics, diagnostic => diagnostic.TsCode == "TS2607");
    }

    [Fact]
    public void MultipleChildrenPreserveTupleShape()
    {
        const string prelude = JsxPrelude + """

            declare const first: JSX.Element;
            declare const second: JSX.Element;
            declare const third: JSX.Element;
            declare function Pair(props: { children: [JSX.Element, JSX.Element] }): JSX.Element;
            """;
        var valid = JsxCall(JsxElementKind.Component, "Pair",
            new Expr.Variable(Identifier("Pair")), new Expr.ObjectLiteral([]),
            new Expr.Variable(Identifier("first")), new Expr.Variable(Identifier("second")));
        var invalid = JsxCall(JsxElementKind.Component, "Pair",
            new Expr.Variable(Identifier("Pair")), new Expr.ObjectLiteral([]),
            new Expr.Variable(Identifier("first")), new Expr.Variable(Identifier("second")),
            new Expr.Variable(Identifier("third")));

        Assert.Empty(Check(valid, prelude).Diagnostics);
        Assert.Contains(Check(invalid, prelude).Diagnostics, diagnostic => diagnostic.TsCode == "TS2322");
    }

    [Fact]
    public void FunctionComponent_BadReturnType_ReportsTs2786()
    {
        const string prelude = JsxPrelude + """

            declare function NotAComponent(props: { a?: string }): { value: string };
            """;
        var call = JsxCall(JsxElementKind.Component, "NotAComponent",
            new Expr.Variable(Identifier("NotAComponent")),
            new Expr.ObjectLiteral([]));

        var result = Check(call, prelude);

        Assert.Contains(result.Diagnostics, d => d.TsCode == "TS2786");
    }

    [Fact]
    public void GenericFunctionComponent_InfersPropsAndChecksAttributes()
    {
        const string prelude = JsxPrelude + """

            declare function GenericValue<T>(props: { value: T; same: T }): JSX.Element;
            """;
        var valid = JsxCall(JsxElementKind.Component, "GenericValue",
            new Expr.Variable(Identifier("GenericValue")),
            new Expr.ObjectLiteral([
                Attribute("value", new Expr.Literal("x")),
                Attribute("same", new Expr.Literal("x")),
            ]));
        var missing = JsxCall(JsxElementKind.Component, "GenericValue",
            new Expr.Variable(Identifier("GenericValue")),
            new Expr.ObjectLiteral([Attribute("value", new Expr.Literal("x"))]));

        Assert.Empty(Check(valid, prelude).Diagnostics);
        Assert.Contains(Check(missing, prelude).Diagnostics, d => d.TsCode == "TS2741");
    }

    [Fact]
    public void GenericFunctionComponent_CombinesRepeatedTypeParameterCandidates()
    {
        const string prelude = JsxPrelude + """

            declare function GenericValue<T>(props: { value: T; repeated: T }): JSX.Element;
            """;
        var call = JsxCall(JsxElementKind.Component, "GenericValue",
            new Expr.Variable(Identifier("GenericValue")),
            new Expr.ObjectLiteral([
                Attribute("value", new Expr.Literal("a")),
                Attribute("repeated", new Expr.Literal("b")),
            ]));

        Assert.Empty(Check(call, prelude).Diagnostics);
    }

    [Fact]
    public void CallableObjectComponent_ChecksItsCallSignatureProps()
    {
        const string prelude = JsxPrelude + """

            interface CallableComponent {
                (props: { count: number }): JSX.Element;
                label: string;
            }
            declare const Callable: CallableComponent;
            """;
        var call = JsxCall(JsxElementKind.Component, "Callable",
            new Expr.Variable(Identifier("Callable")),
            new Expr.ObjectLiteral([Attribute("count", new Expr.Literal("wrong"))]));

        Assert.Contains(Check(call, prelude).Diagnostics, d => d.TsCode == "TS2322");
    }

    [Fact]
    public void FunctionComponent_PrimitiveNullableAndArrayReturns_AreValid()
    {
        const string prelude = JsxPrelude + """

            declare function TextComponent(props: {}): string | null;
            declare function ListComponent(props: {}): JSX.Element[];
            """;
        var text = JsxCall(JsxElementKind.Component, "TextComponent",
            new Expr.Variable(Identifier("TextComponent")), new Expr.ObjectLiteral([]));
        var list = JsxCall(JsxElementKind.Component, "ListComponent",
            new Expr.Variable(Identifier("ListComponent")), new Expr.ObjectLiteral([]));

        Assert.Empty(Check(text, prelude).Diagnostics);
        Assert.Empty(Check(list, prelude).Diagnostics);
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

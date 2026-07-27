using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Behavior tests for the embedded react-family shim (stdlib/npm): the jsx runtime's element
/// objects, classic createElement, and react-dom/server's renderToString — exercised as plain
/// imported functions through both execution modes (JSX syntax lowering is tested separately).
/// </summary>
public class ReactShimModuleTests
{
    private static string Run(string mainSource, ExecutionMode mode) =>
        TestHarness.RunModules(
            new Dictionary<string, string> { ["main.ts"] = mainSource },
            "main.ts",
            mode);

    [Theory, ModeData]
    public void Jsx_ProducesElementShape(ExecutionMode mode)
    {
        var output = Run("""
            import { jsx, isValidElement } from "react/jsx-runtime";
            const el = jsx("div", { id: "a", children: "hi" }, 7);
            console.log(el.type);
            console.log(el.props.id);
            console.log(el.props.children);
            console.log(el.key);
            console.log(isValidElement(el));
            console.log(isValidElement({ type: "div" }));
            """, mode);

        Assert.Contains("div", output);
        Assert.Contains("a", output);
        Assert.Contains("hi", output);
        Assert.Contains("7", output);
        Assert.Contains("true", output);
        Assert.Contains("false", output);
    }

    [Theory, ModeData]
    public void Jsx_OmitsKeyAndRefFromProps(ExecutionMode mode)
    {
        var output = Run("""
            import { jsx } from "react/jsx-runtime";
            const el = jsx("div", { key: "k", ref: "r", ok: 1 });
            console.log("key" in el.props);
            console.log("ref" in el.props);
            console.log(el.props.ok);
            console.log(el.key);
            """, mode);

        Assert.Contains("false", output);
        Assert.Contains("1", output);
        Assert.Contains("null", output);
    }

    [Theory, ModeData]
    public void JsxDev_MatchesJsxBehavior(ExecutionMode mode)
    {
        var output = Run("""
            import { jsxDEV } from "react/jsx-dev-runtime";
            const el = jsxDEV("p", { children: "x" }, "k", false, { fileName: "f", lineNumber: 1, columnNumber: 1 }, null);
            console.log(el.type + "|" + el.key + "|" + el.props.children);
            """, mode);

        Assert.Contains("p|k|x", output);
    }

    [Theory, ModeData]
    public void CreateElement_FoldsChildrenAndExtractsKey(ExecutionMode mode)
    {
        var output = Run("""
            import React from "react";
            const one = React.createElement("span", { key: 5, id: "s" }, "only");
            console.log(one.key + "|" + one.props.children + "|" + ("key" in one.props));
            const many = React.createElement("ul", null, "a", "b", "c");
            console.log(Array.isArray(many.props.children) + "|" + many.props.children.length);
            const none = React.createElement("br", null);
            console.log("children" in none.props);
            console.log(React.version);
            """, mode);

        Assert.Contains("5|only|false", output);
        Assert.Contains("true|3", output);
        Assert.Contains("false", output);
        Assert.Contains("18.3.0-sharpts", output);
    }

    [Theory, ModeData]
    public void RenderToString_EscapesTextAndAttributes(ExecutionMode mode)
    {
        var output = Run("""
            import { jsx } from "react/jsx-runtime";
            import { renderToString } from "react-dom/server";
            const el = jsx("div", { title: "a\"b & c", children: "<script>alert('x')</script> & more" });
            console.log(renderToString(el));
            """, mode);

        Assert.Contains(
            "<div title=\"a&quot;b &amp; c\">&lt;script&gt;alert('x')&lt;/script&gt; &amp; more</div>",
            output);
    }

    [Theory, ModeData]
    public void RenderToString_VoidElementsAndBooleanAttributes(ExecutionMode mode)
    {
        var output = Run("""
            import { jsx, jsxs } from "react/jsx-runtime";
            import { renderToString } from "react-dom/server";
            const el = jsxs("div", { children: [
                jsx("br", {}),
                jsx("input", { type: "checkbox", checked: true, disabled: false }),
                jsx("img", { src: "x.png", alt: "" }),
            ]});
            console.log(renderToString(el));
            """, mode);

        Assert.Contains("<br/>", output);
        Assert.Contains("<input type=\"checkbox\" checked/>", output);
        Assert.Contains("<img src=\"x.png\" alt=\"\"/>", output);
    }

    [Theory, ModeData]
    public void RenderToString_ClassNameHtmlForAndStyleObject(ExecutionMode mode)
    {
        var output = Run("""
            import { jsx } from "react/jsx-runtime";
            import { renderToString } from "react-dom/server";
            const el = jsx("label", {
                className: "big",
                htmlFor: "field",
                style: { backgroundColor: "red", fontSize: 12, opacity: 0.5 },
                children: "Name",
            });
            console.log(renderToString(el));
            """, mode);

        Assert.Contains("class=\"big\"", output);
        Assert.Contains("for=\"field\"", output);
        Assert.Contains("background-color:red", output);
        Assert.Contains("font-size:12px", output);
        Assert.Contains("opacity:0.5", output);
    }

    [Theory, ModeData]
    public void RenderToString_FragmentsAndChildElision(ExecutionMode mode)
    {
        var output = Run("""
            import { jsx, jsxs, Fragment } from "react/jsx-runtime";
            import { renderToString } from "react-dom/server";
            const el = jsxs(Fragment, { children: [
                "a",
                null,
                undefined,
                true,
                false,
                42,
                jsx("b", { children: "bold" }),
            ]});
            console.log("[" + renderToString(el) + "]");
            """, mode);

        Assert.Contains("[a42<b>bold</b>]", output);
    }

    [Theory, ModeData]
    public void RenderToString_FunctionComponentsRecurse(ExecutionMode mode)
    {
        var output = Run("""
            import { jsx } from "react/jsx-runtime";
            import { renderToString } from "react-dom/server";
            function Item(props: any) {
                return jsx("li", { children: props.label });
            }
            function List(props: any) {
                return jsx("ul", { children: [
                    jsx(Item, { label: "one" }),
                    jsx(Item, { label: "two" }),
                ]});
            }
            console.log(renderToString(jsx(List, {})));
            """, mode);

        Assert.Contains("<ul><li>one</li><li>two</li></ul>", output);
    }

    [Theory, ModeData]
    public void RenderToString_DangerouslySetInnerHtml(ExecutionMode mode)
    {
        var output = Run("""
            import { jsx } from "react/jsx-runtime";
            import { renderToString } from "react-dom/server";
            const el = jsx("div", { dangerouslySetInnerHTML: { __html: "<b>raw</b>" } });
            console.log(renderToString(el));
            try {
                renderToString(jsx("div", {
                    dangerouslySetInnerHTML: { __html: "x" },
                    children: "conflict",
                }));
            } catch (e: any) {
                console.log("threw");
            }
            """, mode);

        Assert.Contains("<div><b>raw</b></div>", output);
        Assert.Contains("threw", output);
    }

    [Theory, ModeData]
    public void RenderToStaticMarkup_IsAnAlias(ExecutionMode mode)
    {
        var output = Run("""
            import { jsx } from "react/jsx-runtime";
            import { renderToString, renderToStaticMarkup } from "react-dom/server";
            const el = jsx("p", { children: "same" });
            console.log(renderToString(el) === renderToStaticMarkup(el));
            """, mode);

        Assert.Contains("true", output);
    }
}

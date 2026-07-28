using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.IntegrationTests;

/// <summary>
/// Full-pipeline JSX tests: real .tsx sources with JSX syntax, parsed in the TSX dialect,
/// lowered to the automatic runtime, resolved against the embedded react shim, type-checked
/// against its ambient JSX namespace, and executed — interpreted AND compiled.
/// </summary>
public class JsxEndToEndTests
{
    private static string RunTsx(string source, ExecutionMode mode) =>
        TestHarness.RunModules(
            new Dictionary<string, string> { ["main.tsx"] = source },
            "main.tsx",
            mode);

    [Theory, ModeData]
    public void BareTsxRunsWithZeroConfiguration(ExecutionMode mode)
    {
        var output = RunTsx("""
            import { renderToString } from "react-dom/server";

            function Greeting(props: { name: string; excited?: boolean }) {
                return <p className="greet">Hello {props.name}{props.excited ? "!" : "."}</p>;
            }

            console.log(renderToString(<Greeting name="world" excited />));
            """, mode);

        Assert.Contains("<p class=\"greet\">Hello world!</p>", output);
    }

    [Theory, ModeData]
    public void FragmentsListsAndNestedComponentsRender(ExecutionMode mode)
    {
        var output = RunTsx("""
            import { renderToString } from "react-dom/server";

            function Item(props: { label: string }) {
                return <li>{props.label}</li>;
            }

            const labels = ["one", "two", "three"];
            const view = (
                <>
                    <h1 id="title">List</h1>
                    <ul>
                        {labels.map(label => <Item key={label} label={label} />)}
                    </ul>
                </>
            );
            console.log(renderToString(view));
            """, mode);

        Assert.Contains(
            "<h1 id=\"title\">List</h1><ul><li>one</li><li>two</li><li>three</li></ul>",
            output);
    }

    [Theory, ModeData]
    public void JsxTextFidelitySurvivesTheWholePipeline(ExecutionMode mode)
    {
        var output = RunTsx("""
            import { renderToString } from "react-dom/server";

            const view = <p title="it's &amp; that">don't stop — © 2026 &lt;tags&gt;</p>;
            console.log(renderToString(view));
            """, mode);

        // Text entities decode at parse time, then renderToString re-escapes for HTML.
        Assert.Contains(
            "<p title=\"it's &amp; that\">don't stop — © 2026 &lt;tags&gt;</p>",
            output);
    }

    [Theory, ModeData]
    public void SpreadAttributesAndConditionalChildrenWork(ExecutionMode mode)
    {
        var output = RunTsx("""
            import { renderToString } from "react-dom/server";

            const shared: any = { className: "card", role: "note" };
            const show = false;
            const view = (
                <div {...shared} id="x">
                    {show && <span>hidden</span>}
                    visible
                </div>
            );
            console.log(renderToString(view));
            """, mode);

        Assert.Contains("class=\"card\"", output);
        Assert.Contains("role=\"note\"", output);
        Assert.Contains("id=\"x\"", output);
        Assert.Contains("visible", output);
        Assert.DoesNotContain("hidden", output);
    }

    [Theory, ModeData]
    public void ElementObjectsAreInspectableAtRuntime(ExecutionMode mode)
    {
        var output = RunTsx("""
            const el = <section data-kind="hero">content</section>;
            console.log(el.type);
            console.log(el.props["data-kind"]);
            console.log(el.props.children);
            console.log(el.key);
            """, mode);

        Assert.Contains("section", output);
        Assert.Contains("hero", output);
        Assert.Contains("content", output);
        Assert.Contains("null", output);
    }
}

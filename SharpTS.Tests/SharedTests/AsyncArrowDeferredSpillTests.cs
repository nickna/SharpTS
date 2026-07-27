using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regression tests for #414: the async-<em>arrow</em> counterpart of #400. A value spilled
/// before an <c>await</c> inside an async arrow and used after it must survive the suspension.
/// Each await is a genuinely deferred promise (settled from a later event-loop turn via
/// <c>setTimeout</c>), so the compiled arrow state machine actually re-enters MoveNext. Before
/// the fix the spilled operand lived in an IL local that the re-entry wiped, so the compiler
/// dropped the prefix (printing only the suffix). Two arrow forms are covered: nested inside an
/// async function, and a standalone top-level arrow. Runs against both interpreter and compiler.
///
/// The awaited expression is an inline <c>new Promise(...)</c> rather than a call to a helper:
/// awaiting a <em>function call</em> inside an async arrow currently emits separately-invalid
/// IL (a pre-existing gap, unrelated to spills), which would muddy these spill regressions.
/// </summary>
public class AsyncArrowDeferredSpillTests
{
    // An inline deferred promise (settles from a later event-loop turn).
    private static string Defer(object v, int ms)
        => $"new Promise<number>(r => setTimeout(() => r({v}), {ms}))";

    [Theory, ModeData]
    public void NestedArrow_BinaryConcat_PrefixSurvivesDeferredAwait(ExecutionMode mode)
    {
        // The exact shape from the issue.
        var source = $$"""
            async function m() {
              const f = async () => { console.log("z" + (await {{Defer(1, 5)}})); };
              await f();
            }
            m();
            """;
        Assert.Equal("z1\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void StandaloneArrow_BinaryConcat_PrefixSurvivesDeferredAwait(ExecutionMode mode)
    {
        var source = $$"""
            const f = async () => { console.log("z" + (await {{Defer(1, 5)}})); };
            f();
            """;
        Assert.Equal("z1\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void StandaloneArrow_TwoDeferredAwaits(ExecutionMode mode)
    {
        var source = $$"""
            const f = async () => {
              const a = "A" + (await {{Defer(1, 5)}});
              const b = a + "B" + (await {{Defer(2, 3)}});
              console.log(b);
            };
            f();
            """;
        Assert.Equal("A1B2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void StandaloneArrow_TemplateLiteral_DeferredAwaits(ExecutionMode mode)
    {
        var source = $$"""
            const f = async () => { console.log(`v=${"q"}-${await {{Defer(5, 5)}}}-${await {{Defer(6, 3)}}}`); };
            f();
            """;
        Assert.Equal("v=q-5-6\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void StandaloneArrow_ArrayAndObject_DeferredAwaits(ExecutionMode mode)
    {
        var source = $$"""
            const f = async () => {
              const a = ["x", await {{Defer(1, 5)}}, "y"];
              const o = { k: "z" + (await {{Defer(2, 3)}}) };
              console.log(a.join(","), o.k);
            };
            f();
            """;
        Assert.Equal("x,1,y z2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NestedArrow_CapturesOuterLocalAndSpillsAcrossAwait(ExecutionMode mode)
    {
        // Arrow captures an outer local (prefix) AND spills it across the await.
        var source = $$"""
            async function m() {
              const prefix = "PRE";
              const f = async () => { console.log(prefix + "-" + (await {{Defer(7, 5)}})); };
              await f();
            }
            m();
            """;
        Assert.Equal("PRE-7\n", TestHarness.Run(source, mode));
    }
}

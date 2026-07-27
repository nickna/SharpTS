using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regression tests for #414: a value spilled before a <c>yield</c> (or <c>yield*</c>) and
/// used after it must survive the generator's MoveNext re-entry. Before the fix the spilled
/// operand lived in an IL local that the re-entry wiped, so the compiler replaced the prefix
/// with a garbage value (e.g. <c>"a" + (yield "b")</c> resumed to <c>"0"</c> instead of
/// <c>"a…"</c>). The fix mirrors live spill locals to state-machine fields at the yield and
/// restores them on resume.
///
/// These now assert the <em>exact</em> output across both modes. They previously asserted only
/// that the spilled prefix was present, because a resumed <c>yield</c> evaluated to CLR
/// <c>null</c> in compiled mode versus JS <c>undefined</c> in the interpreter — that divergence
/// is fixed by #443 (compiled generators now load the <c>$Undefined</c> sentinel for a resumed
/// yield with no sent value), so the resume value is identical across modes.
/// </summary>
public class GeneratorYieldSpillTests
{
    [Theory, ModeData]
    public void BinaryConcat_PrefixSurvivesYield(ExecutionMode mode)
    {
        var source = """
            function* g() { console.log("PFX:" + (yield 1)); }
            for (const x of g()) {}
            """;
        // Pre-fix compiled output was "0" (prefix lost). The prefix now survives (#414) and the
        // resumed `yield 1` evaluates to `undefined` in both modes (#443).
        Assert.Equal("PFX:undefined\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void MultipleYields_PrefixSurvives(ExecutionMode mode)
    {
        var source = """
            function* g() { console.log("TAG:" + (yield 1) + "|" + (yield 2)); }
            for (const x of g()) {}
            """;
        Assert.Equal("TAG:undefined|undefined\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void TemplateLiteral_PrefixSurvivesYield(ExecutionMode mode)
    {
        var source = """
            function* g() { console.log(`[${"head"}-${yield 1}]`); }
            for (const x of g()) {}
            """;
        Assert.Equal("[head-undefined]\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void YieldStar_PrefixSurvivesDelegation(ExecutionMode mode)
    {
        var source = """
            function* inner() { yield 1; yield 2; }
            function* g() { console.log("PFX:" + (yield* inner())); }
            for (const x of g()) {}
            """;
        // `inner` runs off the end with no `return`, so the `yield*` completion value is
        // `undefined` in both modes (#443).
        Assert.Equal("PFX:undefined\n", TestHarness.Run(source, mode));
    }
}

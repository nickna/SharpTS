using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Guards the compiled-mode regex-literal hoisting optimization
/// (<c>RegexLiteralHoistAnalyzer</c> + <c>EmitRegexLiteral</c>). Hoisting shares
/// one <c>$RegExp</c> per literal site, so these tests pin the cases where that
/// must NOT change observable behavior. Each runs in both modes: the interpreter
/// (which never hoists) is the oracle, and compiled output must match it.
/// </summary>
public class RegExpLiteralHoistingTests
{
    // A global/sticky literal used as `.test` in a loop must NOT be hoisted: a
    // shared instance would advance lastIndex across evaluations. Fresh-per-eval
    // resets lastIndex each call, so every test of "aaa" matches → all true. A
    // shared instance would walk 0,1,2 then fail on the 4th.
    [Theory, ModeData]
    public void GlobalTestInLoop_NotHoisted_ResetsEachEvaluation(ExecutionMode mode)
    {
        var source = """
            function g(): boolean { return /a/g.test("aaa"); }
            console.log(g(), g(), g(), g());
            function y(): boolean { return /a/y.test("aaa"); }
            console.log(y(), y(), y(), y());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true true true true\ntrue true true true\n", output);
    }

    // A non-global, non-sticky literal as a `.test` receiver in a loop IS hoisted;
    // result must be unchanged (lastIndex is irrelevant without g/y).
    [Theory, ModeData]
    public void PlainTestInLoop_Hoisted_CountsCorrectly(ExecutionMode mode)
    {
        var source = """
            function count(n: number): number {
                let c = 0;
                for (let i = 0; i < n; i++) {
                    if (/^[a-z]+$/.test("abc")) c++;
                }
                return c;
            }
            console.log(count(5));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\n", output);
    }

    // Stateless String.prototype consumers (match/replace/search/split) scan from
    // 0 and never read instance lastIndex, so the literal arg is hoisted even with
    // g — repeated calls must give identical results.
    [Theory, ModeData]
    public void StatelessStringConsumersInLoop_Hoisted_StableResults(ExecutionMode mode)
    {
        var source = """
            for (let i = 0; i < 3; i++) {
                console.log("a1b2".replace(/\d/g, "#"));
                console.log(("u1@h.com u2@h.com".match(/\w+@\w+\.\w+/g) || []).length);
                console.log("x,y,z".split(/,/).length);
                console.log("hello world".search(/world/));
            }
            """;

        var output = TestHarness.Run(source, mode);
        var line = "a#b#\n2\n3\n6\n";
        Assert.Equal(line + line + line, output);
    }

    // An escaping literal that is value-equal to a hoisted one must stay a fresh,
    // independent instance (the analyzer keys by node identity, not value). Here
    // `/a/` is hoisted via `.test`; the value-equal `const r = /a/` is mutated and
    // must keep its own lastIndex.
    [Theory, ModeData]
    public void EscapingLiteral_ValueEqualToHoisted_StaysIndependent(ExecutionMode mode)
    {
        var source = """
            function uses(): boolean { return /a/.test("ba"); }
            const r = /a/;
            r.lastIndex = 7;
            console.log(uses());
            console.log(r.lastIndex);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n7\n", output);
    }

    // Two evaluations of the same source literal are distinct objects (===
    // false). The literals appear in === position (not a consuming position), so
    // they are never hoisted — identity is preserved.
    [Theory, ModeData]
    public void RegexLiteralIdentity_NotHoistedInComparison(ExecutionMode mode)
    {
        var source = """
            console.log(/a/ === /a/);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\n", output);
    }
}

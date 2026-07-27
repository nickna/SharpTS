using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Issue #607: in a call position, an in-scope local binding (parameter, <c>let</c>/<c>const</c>, or
/// a nested <c>function</c> declaration) must shadow a module top-level function of the same name —
/// matching JS lexical scoping and the interpreter. The compiler's direct-call dispatch consulted the
/// top-level function table before the local resolver, so <c>h()</c> silently called the top-level
/// <c>h</c> instead of the shadowing binding. The variable-READ path was already correct; only the
/// call path was wrong. The fix defers the direct top-level call when the resolver has a closer
/// binding (without disturbing a top-level function merely reached through a closure capture).
/// </summary>
public class FunctionShadowingCallTests
{
    [Theory, ModeData]
    public void Parameter_ShadowsTopLevelFunction_InCall(ExecutionMode mode)
    {
        var source = """
            function h(): number { return 1; }
            function b(h: () => number): number { return h(); }
            console.log(b(() => 20));
            """;
        Assert.Equal("20", TestHarness.Run(source, mode).Trim());
    }

    [Theory, ModeData]
    public void LocalConst_ShadowsTopLevelFunction_InCall(ExecutionMode mode)
    {
        var source = """
            function h(): number { return 1; }
            function b(): number { const h = () => 20; return h(); }
            console.log(b());
            """;
        Assert.Equal("20", TestHarness.Run(source, mode).Trim());
    }

    [Theory, ModeData]
    public void NestedFunction_ShadowsTopLevelFunction_InCall(ExecutionMode mode)
    {
        var source = """
            function h(): number { return 1; }
            function b(): number { function h(): number { return 20; } return h(); }
            console.log(b());
            """;
        Assert.Equal("20", TestHarness.Run(source, mode).Trim());
    }

    [Theory, ModeData]
    public void UnshadowedTopLevelFunction_StillCalledDirectly(ExecutionMode mode)
    {
        // Control: a function with no shadowing binding in scope must still reach the top-level h.
        var source = """
            function h(): number { return 1; }
            function b(): number { function h(): number { return 20; } return h(); }
            function callsTop(): number { return h(); }
            console.log(b() + "," + callsTop());
            """;
        Assert.Equal("20,1", TestHarness.Run(source, mode).Trim());
    }

    [Theory, ModeData]
    public void LetParameter_ShadowsTopLevelFunction_InCall(ExecutionMode mode)
    {
        // A reassigned `let` still shadows the top-level function for the rest of the body; the call
        // must observe the reassignment, not fall back to the top-level `compute`.
        var source = """
            function compute(): number { return 1; }
            function run(): number {
                let compute: () => number = () => 7;
                const first = compute();
                compute = () => 9;
                return first + compute();
            }
            console.log(run());
            """;
        Assert.Equal("16", TestHarness.Run(source, mode).Trim());
    }
}

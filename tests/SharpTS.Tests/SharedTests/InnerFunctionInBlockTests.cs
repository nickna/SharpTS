using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regression tests for #1230: a <c>function</c> declaration nested inside a block/loop/if
/// <b>inside a function</b> must be referenceable as a value in compiled mode, and — being a
/// block-scoped, per-iteration binding — each one created in a loop must close over that
/// iteration's captures.
///
/// <para>Before the fix the compiled inner-function pre-pass hoisted only a callable body's
/// TOP-LEVEL <c>function</c> declarations; one nested in a block/loop was collected (its method and
/// display class were emitted) but never materialized into a binding, so a reference threw
/// <c>ReferenceError: Undefined variable</c> at the reference site. The fix materializes such a
/// declaration in place at its textual position (via <c>EmitBlockScopedInnerFunctionDeclaration</c>),
/// so its captures snapshot the surrounding block's values for that iteration — matching the
/// interpreter and the per-iteration semantics #1223 established for loop-body <c>let</c>/<c>const</c>.</para>
///
/// <para>Per-iteration correctness depends on the #1223 machinery keeping loop-body/loop-variable
/// bindings out of the shared function display class; these tests pin both the referenceability fix
/// and that the captured values are per-iteration (0,1,… not the fused last value).</para>
/// </summary>
public class InnerFunctionInBlockTests
{
    // ---- The headline #1230 repro: inner function in a loop body capturing a loop-body const ----

    [Theory, ModeData]
    public void LoopBody_InnerFunction_CapturesBodyConst_PerIteration(ExecutionMode mode)
    {
        var source = """
            const fns: any[] = [];
            function go() {
                for (let i = 0; i < 2; i++) {
                    const x = { id: i };
                    function h() { return "x.id=" + x.id; }
                    fns.push(h);
                }
            }
            go();
            console.log(fns[0]() + "|" + fns[1]());
            """;
        Assert.Equal("x.id=0|x.id=1\n", TestHarness.Run(source, mode));
    }

    // ---- Capturing the loop VARIABLE directly (needs the value boxed into the closure field) ----

    [Theory, ModeData]
    public void LoopBody_InnerFunction_CapturesLoopVariable_PerIteration(ExecutionMode mode)
    {
        var source = """
            const fns: any[] = [];
            function go() {
                for (let i = 0; i < 3; i++) {
                    function h() { return "i=" + i; }
                    fns.push(h);
                }
            }
            go();
            console.log(fns.map((f: any) => f()).join(","));
            """;
        Assert.Equal("i=0,i=1,i=2\n", TestHarness.Run(source, mode));
    }

    // ---- Non-capturing inner function in a plain block (was also unreferenceable) ----

    [Theory, ModeData]
    public void PlainBlock_NonCapturingInnerFunction_IsReferenceable(ExecutionMode mode)
    {
        var source = """
            function f() {
                let out = "";
                {
                    function a() { return "a"; }
                    out += a();
                }
                return out;
            }
            console.log(f());
            """;
        Assert.Equal("a\n", TestHarness.Run(source, mode));
    }

    // ---- Inner function in an if-branch inside a function ----

    [Theory, ModeData]
    public void IfBranch_InnerFunction_IsReferenceable(ExecutionMode mode)
    {
        var source = """
            function f(flag: boolean) {
                if (flag) {
                    function b() { return "b"; }
                    return b();
                }
                return "none";
            }
            console.log(f(true) + "/" + f(false));
            """;
        Assert.Equal("b/none\n", TestHarness.Run(source, mode));
    }

    // ---- Two block-levels deep: function nested in an if inside a loop ----

    [Theory, ModeData]
    public void LoopThenIf_InnerFunction_CapturesLoopVariable_PerIteration(ExecutionMode mode)
    {
        var source = """
            function go() {
                const r: string[] = [];
                for (let i = 0; i < 2; i++) {
                    if (i >= 0) {
                        function h() { return "i=" + i; }
                        r.push(h());
                    }
                }
                return r.join(",");
            }
            console.log(go());
            """;
        Assert.Equal("i=0,i=1\n", TestHarness.Run(source, mode));
    }

    // ---- Inner function capturing an enclosing parameter ----

    [Theory, ModeData]
    public void Block_InnerFunction_CapturesParameter(ExecutionMode mode)
    {
        var source = """
            function paramCap(p: number) {
                {
                    function h() { return "p=" + p; }
                    return h();
                }
            }
            console.log(paramCap(42));
            """;
        Assert.Equal("p=42\n", TestHarness.Run(source, mode));
    }

    // ---- Recursive inner function declared in a block ----

    [Theory, ModeData]
    public void Block_RecursiveInnerFunction(ExecutionMode mode)
    {
        var source = """
            function rec() {
                {
                    function fact(n: number): number { return n <= 1 ? 1 : n * fact(n - 1); }
                    return fact(5);
                }
            }
            console.log(rec());
            """;
        Assert.Equal("120\n", TestHarness.Run(source, mode));
    }

    // ---- Sibling inner functions in a block: a later one calls an earlier one ----

    [Theory, ModeData]
    public void Block_SiblingInnerFunctions_CallEarlierSibling(ExecutionMode mode)
    {
        var source = """
            function sib() {
                {
                    function f() { return "f"; }
                    function e() { return f() + "e"; }
                    return e();
                }
            }
            console.log(sib());
            """;
        Assert.Equal("fe\n", TestHarness.Run(source, mode));
    }

    // ---- while-loop body ----

    [Theory, ModeData]
    public void WhileLoopBody_InnerFunction_PerIteration(ExecutionMode mode)
    {
        var source = """
            function wh() {
                const r: string[] = [];
                let i = 0;
                while (i < 2) {
                    const x = i;
                    function h() { return "x=" + x; }
                    r.push(h());
                    i++;
                }
                return r.join(",");
            }
            console.log(wh());
            """;
        Assert.Equal("x=0,x=1\n", TestHarness.Run(source, mode));
    }

    // ---- for-of loop body: inner function captures the loop variable ----

    [Theory, ModeData]
    public void ForOfLoopBody_InnerFunction_PerIteration(ExecutionMode mode)
    {
        var source = """
            function fof() {
                const r: any[] = [];
                for (const v of [10, 20]) {
                    function h() { return "v=" + v; }
                    r.push(h);
                }
                return r[0]() + "," + r[1]();
            }
            console.log(fof());
            """;
        Assert.Equal("v=10,v=20\n", TestHarness.Run(source, mode));
    }

    // ---- An arrow created in the same block captures the block-scoped inner function's value ----

    [Theory, ModeData]
    public void Block_ArrowCapturesInnerFunctionValue(ExecutionMode mode)
    {
        var source = """
            function mixed() {
                {
                    function h() { return "H"; }
                    const a = () => h() + "!";
                    return a();
                }
            }
            console.log(mixed());
            """;
        Assert.Equal("H!\n", TestHarness.Run(source, mode));
    }
}

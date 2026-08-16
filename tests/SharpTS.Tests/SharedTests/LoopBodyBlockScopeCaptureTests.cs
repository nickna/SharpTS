using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regression tests for #1223: a block-scoped <c>let</c>/<c>const</c> declared in a LOOP BODY
/// inside a function/arrow and captured by a closure must be a fresh binding per iteration
/// (ECMA-262 13.7.4 / 14.7.5.13), not a single shared slot.
///
/// <para>Before the fix, such names were hoisted onto the enclosing callable's shared display
/// class (one instance per call), so every iteration's closure read the LAST iteration's value.
/// The #649 per-iteration exclusion only covered <c>for (let/const …)</c> initializer bindings —
/// loop-BODY declarations (and <c>for-of</c>/<c>for-in</c> loop variables) were missed. The
/// original symptom was a compiled net program whose two loop clients shared one `client`
/// capture: client0's 'data' handler destroyed client1, dropping its pending data and hanging
/// the event loop (issue #1223's "intermittent no-output hang").</para>
///
/// <para>The exclusion is deliberately conservative for reassigned names: a binding the closure
/// itself mutates keeps its shared display-class slot, preserving within-iteration mutation
/// visibility.</para>
///
/// <para>#1231: a per-iteration binding captured through a chain of intermediate <em>sync</em>
/// arrows (<c>() =&gt; () =&gt; x</c>, where the intermediate arrow does not itself reference
/// <c>x</c>) is value-forwarded — each intermediate arrow snapshots the binding into its own
/// display class so the innermost closure reads the true per-iteration value. This holds at
/// module scope and inside functions/arrows. Before the fix the compiled output was <c>null</c>
/// (top-level / function-local for-initializer) or the fused last-iteration value (function-local
/// loop-body). Chains that cross a nested function-expression or async-arrow boundary are not
/// sync-forwardable and keep the shared-DC relay.</para>
/// </summary>
public class LoopBodyBlockScopeCaptureTests
{
    // ---- Headline #1223 shape: body const captured inside an arrow ----

    [Theory, ModeData]
    public void ArrowBody_ForLoopBodyConst_PerIteration(ExecutionMode mode)
    {
        var source = """
            const fns: any[] = [];
            const go = () => {
                for (let i = 0; i < 3; i++) {
                    const x = { id: i };
                    fns.push(() => x.id);
                }
            };
            go();
            console.log(fns.map((f: any) => f()).join(","));
            """;
        Assert.Equal("0,1,2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void FunctionBody_ForLoopBodyConst_PerIteration(ExecutionMode mode)
    {
        var source = """
            const fns: any[] = [];
            function go() {
                for (let i = 0; i < 3; i++) {
                    const x = { id: i };
                    fns.push(() => x.id);
                }
            }
            go();
            console.log(fns.map((f: any) => f()).join(","));
            """;
        Assert.Equal("0,1,2\n", TestHarness.Run(source, mode));
    }

    // ---- The #1223 net-repro shape: shared mutable state + per-iteration object capture ----
    // The closure captures BOTH a module-level mutable counter (stays on the entry-point DC)
    // and the per-iteration body const; each handler must destroy ITS OWN object.

    [Theory, ModeData]
    public void MixedCapture_ModuleCounterPlusBodyConst_DestroysOwnObject(ExecutionMode mode)
    {
        var source = """
            let responses = 0;
            const destroyed: string[] = [];
            const handlers: any[] = [];
            const listen = () => {
                for (let i = 0; i < 2; i++) {
                    const client = { name: "c" + i, destroy() { destroyed.push(this.name); } };
                    handlers.push(() => { responses++; client.destroy(); });
                }
            };
            listen();
            handlers[0]();
            handlers[1]();
            console.log(destroyed.join(",") + " resp=" + responses);
            """;
        Assert.Equal("c0,c1 resp=2\n", TestHarness.Run(source, mode));
    }

    // ---- Other loop kinds ----

    [Theory, ModeData]
    public void WhileLoopBodyConst_PerIteration(ExecutionMode mode)
    {
        var source = """
            const fns: any[] = [];
            function go() {
                let i = 0;
                while (i < 3) {
                    const x = { id: i };
                    fns.push(() => x.id);
                    i++;
                }
            }
            go();
            console.log(fns.map((f: any) => f()).join(","));
            """;
        Assert.Equal("0,1,2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void DoWhileLoopBodyConst_PerIteration(ExecutionMode mode)
    {
        var source = """
            const fns: any[] = [];
            function go() {
                let i = 0;
                do {
                    const x = { id: i };
                    fns.push(() => x.id);
                    i++;
                } while (i < 3);
            }
            go();
            console.log(fns.map((f: any) => f()).join(","));
            """;
        Assert.Equal("0,1,2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ForOfLoopVariable_CapturedInFunction_PerIteration(ExecutionMode mode)
    {
        var source = """
            const fns: any[] = [];
            const go = () => {
                for (const v of [10, 20, 30]) {
                    fns.push(() => v);
                }
            };
            go();
            console.log(fns.map((f: any) => f()).join(","));
            """;
        Assert.Equal("10,20,30\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ForInLoopVariable_CapturedInFunction_PerIteration(ExecutionMode mode)
    {
        var source = """
            const fns: any[] = [];
            const go = () => {
                for (const k in { a: 1, b: 2 }) {
                    fns.push(() => k);
                }
            };
            go();
            console.log(fns.map((f: any) => f()).join(","));
            """;
        Assert.Equal("a,b\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ForOfBodyConst_PerIteration(ExecutionMode mode)
    {
        var source = """
            const fns: any[] = [];
            function go() {
                for (const v of [1, 2]) {
                    const doubled = v * 2;
                    fns.push(() => doubled);
                }
            }
            go();
            console.log(fns.map((f: any) => f()).join(","));
            """;
        Assert.Equal("2,4\n", TestHarness.Run(source, mode));
    }

    // ---- State machines: async function and generator bodies ----

    [Theory, ModeData]
    public void AsyncFunctionLoopBodyConst_PerIteration(ExecutionMode mode)
    {
        var source = """
            const fns: any[] = [];
            async function go() {
                for (let i = 0; i < 3; i++) {
                    const x = { id: i };
                    fns.push(() => x.id);
                    await Promise.resolve();
                }
            }
            async function main() {
                await go();
                console.log(fns.map((f: any) => f()).join(","));
            }
            main();
            """;
        Assert.Equal("0,1,2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void GeneratorLoopBodyConst_PerIteration(ExecutionMode mode)
    {
        var source = """
            const fns: any[] = [];
            function* gen() {
                for (let i = 0; i < 3; i++) {
                    const x = { id: i };
                    fns.push(() => x.id);
                    yield i;
                }
            }
            for (const _ of gen()) {}
            console.log(fns.map((f: any) => f()).join(","));
            """;
        Assert.Equal("0,1,2\n", TestHarness.Run(source, mode));
    }

    // ---- Nested loops: body const of the inner loop sees both indices ----

    [Theory, ModeData]
    public void NestedLoopBodyConst_PerIteration(ExecutionMode mode)
    {
        var source = """
            const fns: any[] = [];
            function go() {
                for (let i = 0; i < 2; i++) {
                    for (let j = 0; j < 2; j++) {
                        const cell = "" + i + j;
                        fns.push(() => cell);
                    }
                }
            }
            go();
            console.log(fns.map((f: any) => f()).join(","));
            """;
        Assert.Equal("00,01,10,11\n", TestHarness.Run(source, mode));
    }

    // ---- Conservative declines: these shapes keep the shared DC slot ----

    // A `let` the closure itself mutates must keep shared mutation visibility
    // within the iteration (the closure's writes reach the outer read).
    [Theory, ModeData]
    public void WriteCapturedBodyLet_MutationVisibleWithinIteration(ExecutionMode mode)
    {
        var source = """
            const outs: string[] = [];
            function go() {
                for (let i = 0; i < 2; i++) {
                    let n = 0;
                    const inc = () => { n++; };
                    inc(); inc();
                    outs.push("n=" + n);
                }
            }
            go();
            console.log(outs.join(","));
            """;
        Assert.Equal("n=2,n=2\n", TestHarness.Run(source, mode));
    }

    // A body const shadow-adjacent to a function-level binding: the outer binding
    // keeps its shared slot; the body binding is still per-iteration.
    [Theory, ModeData]
    public void BodyConstNextToFunctionLevelCapture_BothCorrect(ExecutionMode mode)
    {
        var source = """
            const fns: any[] = [];
            const go = () => {
                let outer = { id: 99 };
                const touch = () => outer;
                touch();
                for (let i = 0; i < 2; i++) {
                    const y = { id: i };
                    fns.push(() => y.id + ":" + outer.id);
                }
            };
            go();
            console.log(fns.map((f: any) => f()).join(","));
            """;
        Assert.Equal("0:99,1:99\n", TestHarness.Run(source, mode));
    }

    // Same name declared BOTH as a function-level local and in a loop body of a
    // sibling function: per-function tracking keeps them independent.
    [Theory, ModeData]
    public void SameNameAcrossSiblingFunctions_Independent(ExecutionMode mode)
    {
        var source = """
            const fns: any[] = [];
            function a() {
                for (let i = 0; i < 2; i++) {
                    const x = { id: i };
                    fns.push(() => "a" + x.id);
                }
            }
            function b() {
                const x = { id: 7 };
                fns.push(() => "b" + x.id);
            }
            a();
            b();
            console.log(fns.map((f: any) => f()).join(","));
            """;
        Assert.Equal("a0,a1,b7\n", TestHarness.Run(source, mode));
    }

    // ---- #1231: per-iteration bindings captured through a nested sync-arrow chain ----
    // The intermediate arrow does not reference the binding, so it can only relay the value
    // by capturing-and-forwarding it (value-forwarding). Before the fix the compiled inner
    // closure read null (top-level / for-initializer) or the fused last value (function body).

    // Top-level loop-body const through a two-arrow chain (#1231 shape 1).
    [Theory, ModeData]
    public void TopLevel_LoopBodyConst_ChainedCapture_PerIteration(ExecutionMode mode)
    {
        var source = """
            const a: any[] = [];
            for (let i = 0; i < 3; i++) {
                const x = { id: i };
                a.push(() => () => x.id);
            }
            console.log(a.map((f: any) => f()()).join(","));
            """;
        Assert.Equal("0,1,2\n", TestHarness.Run(source, mode));
    }

    // Top-level for-initializer binding through a chain (#1231 shape 2 / #649 exclusion).
    [Theory, ModeData]
    public void TopLevel_ForInitializer_ChainedCapture_PerIteration(ExecutionMode mode)
    {
        var source = """
            const a: any[] = [];
            for (let j = 0; j < 3; j++) {
                a.push(() => () => j);
            }
            console.log(a.map((f: any) => f()()).join(","));
            """;
        Assert.Equal("0,1,2\n", TestHarness.Run(source, mode));
    }

    // Function-local loop-body const through a chain (was fused to the last iteration).
    [Theory, ModeData]
    public void FunctionBody_LoopBodyConst_ChainedCapture_PerIteration(ExecutionMode mode)
    {
        var source = """
            function go() {
                const a: any[] = [];
                for (let i = 0; i < 3; i++) {
                    const x = { id: i };
                    a.push(() => () => x.id);
                }
                return a;
            }
            console.log(go().map((f: any) => f()()).join(","));
            """;
        Assert.Equal("0,1,2\n", TestHarness.Run(source, mode));
    }

    // Function-local for-initializer binding through a chain (was null compiled).
    [Theory, ModeData]
    public void FunctionBody_ForInitializer_ChainedCapture_PerIteration(ExecutionMode mode)
    {
        var source = """
            function go() {
                const a: any[] = [];
                for (let j = 0; j < 3; j++) {
                    a.push(() => () => j);
                }
                return a;
            }
            console.log(go().map((f: any) => f()()).join(","));
            """;
        Assert.Equal("0,1,2\n", TestHarness.Run(source, mode));
    }

    // A binding captured BOTH directly and through a chain in the same loop: both closures
    // must observe the same per-iteration value (previously the chained capture forced even
    // the direct one onto the shared fused slot).
    [Theory, ModeData]
    public void DirectAndChainedCapture_SameLoop_BothPerIteration(ExecutionMode mode)
    {
        var source = """
            const direct: any[] = [];
            const chained: any[] = [];
            for (let i = 0; i < 3; i++) {
                const x = i;
                direct.push(() => x);
                chained.push(() => () => x);
            }
            const d = direct.map((f: any) => f()).join(",");
            const c = chained.map((f: any) => f()()).join(",");
            console.log(d + " | " + c);
            """;
        Assert.Equal("0,1,2 | 0,1,2\n", TestHarness.Run(source, mode));
    }

    // Deeper chain (four arrows) still forwards the per-iteration value all the way in.
    [Theory, ModeData]
    public void TopLevel_LoopBodyConst_DeepChain_PerIteration(ExecutionMode mode)
    {
        var source = """
            const a: any[] = [];
            for (let i = 0; i < 3; i++) {
                const x = i;
                a.push(() => () => () => () => x);
            }
            console.log(a.map((f: any) => f()()()()).join(","));
            """;
        Assert.Equal("0,1,2\n", TestHarness.Run(source, mode));
    }

    // Top-level for-of loop variable through a chain.
    [Theory, ModeData]
    public void TopLevel_ForOfVariable_ChainedCapture_PerIteration(ExecutionMode mode)
    {
        var source = """
            const a: any[] = [];
            for (const v of [10, 20, 30]) {
                a.push(() => () => v);
            }
            console.log(a.map((f: any) => f()()).join(","));
            """;
        Assert.Equal("10,20,30\n", TestHarness.Run(source, mode));
    }

    // A chained capture that also references the enclosing class `this`: the per-iteration
    // binding forwards by value while `this` still relays through the arrow chain.
    [Theory, ModeData]
    public void ChainedCapture_WithThis_PerIteration(ExecutionMode mode)
    {
        var source = """
            class C {
                tag = "t";
                build() {
                    const a: any[] = [];
                    for (let i = 0; i < 2; i++) {
                        const x = i;
                        a.push(() => () => this.tag + x);
                    }
                    return a;
                }
            }
            console.log(new C().build().map((f: any) => f()()).join(","));
            """;
        Assert.Equal("t0,t1\n", TestHarness.Run(source, mode));
    }
}

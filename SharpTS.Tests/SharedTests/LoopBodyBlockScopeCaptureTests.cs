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
/// <para>The exclusion is deliberately conservative: names that are reassigned after
/// initialization, or captured through an intermediate closure (<c>() =&gt; () =&gt; x</c>),
/// keep their shared display-class slot — preserving within-iteration mutation visibility and
/// the DC relay that a nested closure's populate path needs.</para>
/// </summary>
public class LoopBodyBlockScopeCaptureTests
{
    // ---- Headline #1223 shape: body const captured inside an arrow ----

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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
}

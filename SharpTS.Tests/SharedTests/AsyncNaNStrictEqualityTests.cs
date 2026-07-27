using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regression tests for #642: in the interpreter's async execution path, the number fast-path for
/// equality used <c>Double.Equals</c> (which treats <c>NaN.Equals(NaN)</c> as true) instead of
/// IEEE 754 <c>==</c>. So <c>NaN === NaN</c> inside an <c>async function</c> wrongly returned
/// <c>true</c> (and <c>NaN !== NaN</c> wrongly returned <c>false</c>), while top-level code, sync
/// functions, generators, and compiled mode were all correct (per ECMA-262 7.2.16 NaN is never
/// equal to anything, including itself).
///
/// <para>The fix mirrors the synchronous fast path (<c>EvaluateBinary</c> in
/// <c>Interpreter.Calls.cs</c>): <c>EvaluateBinaryAsync</c> now compares with <c>l == r</c>. These
/// tests assert the corrected NaN behavior in every async/generator state-machine context and pin
/// the already-correct contexts as cross-mode parity, plus guard that ordinary number equality is
/// unaffected by the change.</para>
///
/// <para>Also covers #648, the compiled-mode analog. There the equality machinery was already
/// NaN-aware; the real defect was narrower and broader at once: <c>AsyncArrowMoveNextEmitter</c>
/// reimplements variable resolution and never consulted the JS global constants, so a bare
/// <c>NaN</c>/<c>Infinity</c> inside a compiled <c>async</c> arrow compiled to a <c>null</c> load.
/// <c>NaN === NaN</c> therefore degraded to <c>null === null</c> → <c>true</c>, and any other use
/// (value, <c>typeof</c>, arithmetic) was equally wrong. The fix routes that emitter through the
/// shared <c>TryEmitJsGlobalConstant</c> helper, so the <see cref="AsyncArrow_NaNStrictEquality"/>
/// and <see cref="AsyncArrow_GlobalConstantsResolve"/> cases below now run cross-mode.</para>
/// </summary>
public class AsyncNaNStrictEqualityTests
{
    [Theory, ModeData]
    public void AsyncFunction_NaNStrictEquality(ExecutionMode mode)
    {
        // The exact repro from #642.
        var source = """
            async function main() {
                console.log(NaN === NaN);
                const x = NaN;
                console.log(x === x);
                console.log(NaN !== NaN);
            }
            main();
            """;
        Assert.Equal("false\nfalse\ntrue\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncFunction_ComputedNaN_NotEqual(ExecutionMode mode)
    {
        // A NaN produced at runtime (not the literal) — exercises the same fast path.
        var source = """
            async function main() {
                const n = Math.sqrt(-1);
                console.log(n === n);
                console.log(n !== n);
            }
            main();
            """;
        Assert.Equal("false\ntrue\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncFunction_AfterAwait_NaNStillNotEqual(ExecutionMode mode)
    {
        // Equality evaluated after a suspension point still runs through the async fast path.
        var source = """
            async function main() {
                await Promise.resolve(0);
                console.log(NaN === NaN);
            }
            main();
            """;
        Assert.Equal("false\n", TestHarness.Run(source, mode));
    }

    // #648: previously interpreted-only because a bare NaN/Infinity inside a compiled async arrow
    // resolved to null (NaN === NaN → null === null → true). Now cross-mode after the
    // AsyncArrowMoveNextEmitter global-constant fix.
    [Theory, ModeData]
    public void AsyncArrow_NaNStrictEquality(ExecutionMode mode)
    {
        // The exact repro from #648.
        var source = """
            const r = async () => {
                console.log(NaN === NaN);
                console.log(NaN !== NaN);
            };
            r();
            """;
        Assert.Equal("false\ntrue\n", TestHarness.Run(source, mode));
    }

    // #648 root cause (broader than the equality symptom): the JS global constants NaN/Infinity
    // must resolve to real values inside a compiled async arrow, not a null load. A param named
    // `NaN` must still shadow the global (resolver runs before the constant check), matching
    // ECMA-262 lexical lookup.
    [Theory, ModeData]
    public void AsyncArrow_GlobalConstantsResolve(ExecutionMode mode)
    {
        var source = """
            const r = async () => {
                console.log(NaN);
                console.log(typeof NaN);
                console.log(Infinity);
                console.log(-Infinity);
                console.log(NaN + 1);
                console.log(Number.isNaN(NaN));
                console.log(Infinity > 1e308);
            };
            r();
            const shadow = async (NaN: any) => NaN === NaN;
            shadow(5).then((v: boolean) => console.log(v));
            """;
        Assert.Equal("NaN\nnumber\nInfinity\n-Infinity\nNaN\ntrue\ntrue\ntrue\n", TestHarness.Run(source, mode));
    }

    // After an await the arrow body runs from a resumed state-machine label; the global constants
    // must still resolve there too (the suspend/resume path is where #648's null load was most
    // surprising).
    [Theory, ModeData]
    public void AsyncArrow_AfterAwait_NaNStillNotEqual(ExecutionMode mode)
    {
        var source = """
            const r = async () => {
                await Promise.resolve(0);
                console.log(NaN === NaN);
                console.log(NaN);
            };
            r();
            """;
        Assert.Equal("false\nNaN\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncGenerator_NaNStrictEquality(ExecutionMode mode)
    {
        var source = """
            async function* g() {
                yield (NaN === NaN);
                yield (NaN !== NaN);
            }
            async function main() {
                for await (const x of g()) console.log(x);
            }
            main();
            """;
        Assert.Equal("false\ntrue\n", TestHarness.Run(source, mode));
    }

    // ---- Already-correct contexts: assert cross-mode parity so they don't regress ----

    [Theory, ModeData]
    public void TopLevel_NaNStrictEquality(ExecutionMode mode)
    {
        var source = """
            console.log(NaN === NaN);
            console.log(NaN !== NaN);
            """;
        Assert.Equal("false\ntrue\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_NaNStrictEquality(ExecutionMode mode)
    {
        var source = """
            function* g() {
                yield (NaN === NaN);
                yield (NaN !== NaN);
            }
            for (const x of g()) console.log(x);
            """;
        Assert.Equal("false\ntrue\n", TestHarness.Run(source, mode));
    }

    // ---- Guard: ordinary number equality is unaffected by the Double.Equals -> == change ----

    [Theory, ModeData]
    public void AsyncFunction_OrdinaryNumberEquality_Unaffected(ExecutionMode mode)
    {
        var source = """
            async function main() {
                console.log(2 === 2);
                console.log(2 === 3);
                console.log(2 !== 3);
                console.log(0 === -0);
            }
            main();
            """;
        Assert.Equal("true\nfalse\ntrue\ntrue\n", TestHarness.Run(source, mode));
    }
}

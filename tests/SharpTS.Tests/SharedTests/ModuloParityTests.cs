using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Pins interpreter/compiled parity for the native-int modulo fast path (#928).
///
/// In compiled mode, <c>counter % integerLiteral</c> is emitted as a native int64 <c>rem</c>
/// followed by <c>conv.r8</c> (<see cref="SharpTS.Compilation.ILEmitter"/>
/// <c>TryEmitIntegerCounterModulo</c>) instead of an FP <c>fmod</c> on two doubles — the
/// <c>fmod</c> is the dominant per-iteration cost in numeric write kernels. C# <c>long %</c> and
/// JS <c>%</c> are both truncated (the result takes the dividend's sign), so the optimization is
/// bit-identical to the double computation for every <c>|dividend| ≤ 2^53</c> — the same range the
/// int-counter representation already accepts (<c>SHARPTS_INT_LOOP_COUNTER</c>). The interpreter
/// never takes the fast path, so each theory below asserts both modes agree, and every expected
/// value is ground-truthed against Node (= the JS spec).
/// </summary>
public class ModuloParityTests
{
    [Theory, ModeData]
    public void AscendingCounter_BasicDivisor(ExecutionMode mode)
    {
        var source = """
            let r: string = "";
            for (let i: number = 0; i < 10; i++) { r += (i % 3) + ","; }
            console.log(r);
            """;
        Assert.Equal("0,1,2,0,1,2,0,1,2,0,\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void CounterPlusMinusLiteral_Dividend(ExecutionMode mode)
    {
        // `(i + k) % m` and `(i - k) % m` — the counter±literal dividend shapes the fast path recognizes.
        var source = """
            let r: string = "";
            for (let i: number = 0; i < 8; i++) { r += ((i + 1) % 4) + ":" + ((i - 2) % 5) + " "; }
            console.log(r);
            """;
        Assert.Equal("1:-2 2:-1 3:0 0:1 1:2 2:3 3:4 0:0 \n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void DescendingCounter_NegativeDividend_AndNegativeDivisor(ExecutionMode mode)
    {
        // Truncated remainder: the result takes the dividend's sign; a negative divisor does not
        // change the sign. C# and JS agree on both.
        var source = """
            let r: string = "";
            for (let i: number = 3; i > -4; i--) { r += (i % 3) + "/" + (i % -3) + " "; }
            console.log(r);
            """;
        Assert.Equal("0/0 2/2 1/1 0/0 -1/-1 -2/-2 0/0 \n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void DivisorOne_AlwaysZero(ExecutionMode mode)
    {
        var source = """
            let r: string = "";
            for (let i: number = 0; i < 5; i++) { r += (i % 1) + ","; }
            console.log(r);
            """;
        Assert.Equal("0,0,0,0,0,\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void DivisorZero_FallsBackToNaN_DoesNotThrow(ExecutionMode mode)
    {
        // `i % 0` is NaN in JS (fmod(x, 0)). The fast path explicitly declines divisor 0 so it never
        // emits an int64 `rem` (which would be a DivideByZeroException) — it routes to the double path.
        var source = """
            let r: string = "";
            for (let i: number = 0; i < 3; i++) { r += (i % 0) + ","; }
            console.log(r);
            """;
        Assert.Equal("NaN,NaN,NaN,\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NonCounterDividend_FallsBackToDoublePath(ExecutionMode mode)
    {
        // `(i * 2) % 5` — the dividend is a multiply, not a recognized counter expression, so the
        // fast path declines and the double path computes it. Must still be correct.
        var source = """
            let r: string = "";
            for (let i: number = 0; i < 6; i++) { r += ((i * 2) % 5) + ","; }
            console.log(r);
            """;
        Assert.Equal("0,2,4,1,3,0,\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ModuloInFloat64WriteKernel(ExecutionMode mode)
    {
        // The real kernel shape: a modulo embedded in a mixed-double store expression into a
        // Float64Array, then summed. Exercises native-int modulo feeding a double store.
        var source = """
            const a: Float64Array = new Float64Array(20);
            for (let i: number = 0; i < 20; i++) { a[i] = i * 1.5 + (i % 7); }
            let s: number = 0;
            for (let i: number = 0; i < 20; i++) { s = s + a[i]; }
            console.log(s);
            """;
        Assert.Equal("342\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ModuloInInt32WriteKernel_ToInt32Truncation(ExecutionMode mode)
    {
        // Int32Array store path: the int64 modulo result feeds a store that ToInt32-truncates.
        var source = """
            const b: Int32Array = new Int32Array(15);
            for (let i: number = 0; i < 15; i++) { b[i] = i * 3 - (i % 7); }
            let r: string = "";
            for (let i: number = 0; i < 15; i++) { r += b[i] + ","; }
            console.log(r);
            """;
        Assert.Equal("0,2,4,6,8,10,12,21,23,25,27,29,31,33,42,\n", TestHarness.Run(source, mode));
    }
}

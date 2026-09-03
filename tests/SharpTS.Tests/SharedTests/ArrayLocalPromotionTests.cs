using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for the typed-array-local promotion optimization (#857/#860): a provably
/// non-escaping number[]/boolean[] local with an empty-array-literal initializer is
/// compiled to a concrete List&lt;double&gt;/List&lt;bool&gt; slot with unboxed element access.
///
/// These run against BOTH the interpreter and the compiler. The positive cases exercise
/// the promoted fast paths; the escape cases must NOT be promoted (they fall back to the
/// general $Array path) and must still produce correct results — i.e. interpreter/compiled
/// parity must hold even when the array is passed, returned, spread, iterated, compared,
/// or has holes. A wrong escape rule would surface here as a compiled-mode mismatch.
/// </summary>
public class ArrayLocalPromotionTests
{
    // ── Positive cases: promotable shapes ──────────────────────────────────

    [Theory, ModeData]
    public void Promoted_BoolSieve_CountsPrimes(ExecutionMode mode)
    {
        // The count-primes shape: const boolean[] built by push, then index read/write.
        var source = """
            function countPrimes(n: number): number {
                if (n <= 2) return 0;
                const isPrime: boolean[] = [];
                for (let i: number = 0; i < n; i++) { isPrime.push(true); }
                isPrime[0] = false;
                isPrime[1] = false;
                for (let i: number = 2; i * i < n; i++) {
                    if (isPrime[i]) {
                        for (let j: number = i * i; j < n; j = j + i) { isPrime[j] = false; }
                    }
                }
                let count: number = 0;
                for (let i: number = 0; i < n; i++) { if (isPrime[i]) count = count + 1; }
                return count;
            }
            console.log(countPrimes(20));
            """;

        Assert.Equal("8\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Promoted_NumberArray_PushIndexLength(ExecutionMode mode)
    {
        var source = """
            function build(): number {
                const xs: number[] = [];
                for (let i: number = 0; i < 5; i++) { xs.push(i * 2); }
                xs[0] = 100;
                let sum: number = 0;
                for (let i: number = 0; i < xs.length; i++) { sum = sum + xs[i]; }
                return sum;
            }
            console.log(build());
            """;

        // 100 + 2 + 4 + 6 + 8 = 120
        Assert.Equal("120\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Promoted_IndexWrite_ReturnsAssignedValue(ExecutionMode mode)
    {
        // `arr[i] = v` is an expression whose value is the assigned RHS.
        var source = """
            function f(): number {
                const xs: number[] = [];
                xs.push(0);
                const v: number = (xs[0] = 42);
                return v + xs[0];
            }
            console.log(f());
            """;

        Assert.Equal("84\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Promoted_BooleanCondition_OutOfRangeIsFalse(ExecutionMode mode)
    {
        var source = """
            function test(index: number): boolean {
                const xs: boolean[] = [];
                xs.push(true);
                if (xs[index]) return true;
                return false;
            }
            console.log(test(-1), test(1), test(0));
            """;

        Assert.Equal("false false true\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Promoted_BooleanTrueFill_PreservesFractionalAndFallbackBounds(ExecutionMode mode)
    {
        var source = """
            function fill(n: number): number {
                const xs: boolean[] = [];
                for (let i: number = 0; i < n; i++) xs.push(true);
                return xs.length;
            }
            console.log(fill(3.5), fill(0), fill(-2));
            """;

        Assert.Equal("4 0 0\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Promoted_IndexWrite_EvaluatesIndexBeforeValue(ExecutionMode mode)
    {
        var source = """
            let order: string = "";
            function index(): number { order = order + "i"; return 0; }
            function value(): boolean { order = order + "v"; return false; }
            function update(): boolean {
                const xs: boolean[] = [];
                xs.push(true);
                xs[index()] = value();
                return xs[0];
            }
            console.log(update(), order);
            """;

        Assert.Equal("false iv\n", TestHarness.Run(source, mode));
    }

    // ── Escape cases: must fall back, must stay correct ────────────────────

    [Theory, ModeData]
    public void Escape_PassedToFunctionThatPushes(ExecutionMode mode)
    {
        // Passing the array as an argument escapes — a bare List<T> can't be mutated
        // through the $Array-expecting callee, so this must NOT be promoted.
        var source = """
            function fill(a: number[]): void { a.push(1); a.push(2); a.push(3); }
            function go(): number {
                const xs: number[] = [];
                fill(xs);
                let sum: number = 0;
                for (let i: number = 0; i < xs.length; i++) { sum = sum + xs[i]; }
                return sum;
            }
            console.log(go());
            """;

        Assert.Equal("6\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Escape_Returned(ExecutionMode mode)
    {
        var source = """
            function make(): number[] {
                const xs: number[] = [];
                xs.push(7);
                xs.push(8);
                return xs;
            }
            const r: number[] = make();
            console.log(r.length);
            console.log(r[1]);
            """;

        Assert.Equal("2\n8\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Escape_Spread(ExecutionMode mode)
    {
        var source = """
            function f(): number {
                const xs: number[] = [];
                xs.push(1); xs.push(2); xs.push(3);
                const ys: number[] = [...xs, 4];
                let sum: number = 0;
                for (let i: number = 0; i < ys.length; i++) { sum = sum + ys[i]; }
                return sum;
            }
            console.log(f());
            """;

        Assert.Equal("10\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Escape_ForOf(ExecutionMode mode)
    {
        var source = """
            function f(): number {
                const xs: number[] = [];
                xs.push(10); xs.push(20); xs.push(30);
                let sum: number = 0;
                for (const x of xs) { sum = sum + x; }
                return sum;
            }
            console.log(f());
            """;

        Assert.Equal("60\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Escape_OtherMethod_Map(ExecutionMode mode)
    {
        // .map is not a permitted use → no promotion; result must still be correct.
        var source = """
            function f(): number {
                const xs: number[] = [];
                xs.push(1); xs.push(2); xs.push(3);
                const doubled: number[] = xs.map((x: number): number => x * 2);
                let sum: number = 0;
                for (let i: number = 0; i < doubled.length; i++) { sum = sum + doubled[i]; }
                return sum;
            }
            console.log(f());
            """;

        Assert.Equal("12\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Escape_OutOfRangeReadIsUndefined(ExecutionMode mode)
    {
        // A read past the end must yield undefined (JS semantics). Reading `xs[5]`
        // anywhere is fine for a promoted array only if it stays in range; here the
        // index expression escapes nothing, but an OOB read must match the interpreter.
        // Because the array is also logged (escape), it is not promoted — but this pins
        // the fallback semantics regardless.
        var source = """
            const xs: number[] = [];
            xs.push(1);
            console.log(xs);
            console.log(xs[5]);
            """;

        Assert.Equal("[1]\nundefined\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Escape_AnyTypedElementWrite_NotPromoted(ExecutionMode mode)
    {
        // An `any`-typed element write disqualifies promotion (the typed setter would
        // coerce a runtime undefined to NaN/false — the array analogue of the #367 taint
        // guard). We assert interpreter/compiled parity only on a NUMBER-typed any value
        // here, which both modes agree on; the guard's job is purely to keep codegen on
        // the pre-existing general path, which the count-primes IL inspection confirms.
        var source = """
            function f(): number {
                const xs: number[] = [];
                xs.push(1);
                const u: any = 41;
                xs[1] = u;
                return xs[0] + xs[1];
            }
            console.log(f());
            """;

        Assert.Equal("42\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Promoted_AutoExtendOnIndexWrite(ExecutionMode mode)
    {
        // Writing at index == length extends the array by one (auto-extend path).
        var source = """
            function f(): number {
                const xs: number[] = [];
                xs.push(1);
                xs[1] = 2;
                xs[2] = 3;
                let sum: number = 0;
                for (let i: number = 0; i < xs.length; i++) { sum = sum + xs[i]; }
                return sum;
            }
            console.log(f());
            """;

        Assert.Equal("6\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Promoted_PerScope_NameCollisionDoesNotPoison(ExecutionMode mode)
    {
        // `xs` in build() is a clean push/index/length array and must promote even though an
        // unrelated, escaping `xs` (returned) exists in leak() — candidacy is keyed per function
        // scope, so a common array name shared across functions/modules no longer poisons the
        // whole program. Both must produce correct results.
        var source = """
            function leak(): number[] {
                const xs: number[] = [];
                xs.push(9);
                return xs;
            }
            function build(): number {
                const xs: number[] = [];
                for (let i: number = 0; i < 5; i++) { xs.push(i); }
                let sum: number = 0;
                for (let i: number = 0; i < xs.length; i++) { sum = sum + xs[i]; }
                return sum;
            }
            console.log(build());
            console.log(leak()[0]);
            """;

        // 0+1+2+3+4 = 10 ; leak()[0] = 9
        Assert.Equal("10\n9\n", TestHarness.Run(source, mode));
    }

    // ── Typed-HOF pipeline (#861): typed reduce over a promoted number[] ────

    [Theory, ModeData]
    public void TypedReduce_PromotedNumberArray(ExecutionMode mode)
    {
        // arr used only via push + reduce(non-capturing typed numeric reducer) → promoted to
        // List<double>; reduce drives the typed ArrayReduceDouble fast path (no per-element boxing).
        var source = """
            function sumReduce(): number {
                const arr: number[] = [];
                for (let i: number = 0; i < 5; i++) { arr.push(i); }
                return arr.reduce((a: number, x: number): number => a + x, 0);
            }
            console.log(sumReduce());
            """;

        // 0+1+2+3+4 = 10
        Assert.Equal("10\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void TypedReduce_CapturingReducer_FallsBackCorrectly(ExecutionMode mode)
    {
        // The reducer captures `base`, so it cannot bind as a direct typed delegate — the analyzer
        // does NOT permit the reduce receiver, arr stays on the $Array path, and the result must
        // still be correct. Guards the analyzer/emitter typeability agreement.
        var source = """
            function f(): number {
                const base: number = 100;
                const arr: number[] = [];
                for (let i: number = 0; i < 3; i++) { arr.push(i); }
                return arr.reduce((a: number, x: number): number => a + x + base, 0);
            }
            console.log(f());
            """;

        // acc: 0 → 0+0+100=100 → 100+1+100=201 → 201+2+100=303
        Assert.Equal("303\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void TypedMap_ThenIndexLength(ExecutionMode mode)
    {
        // `doubled` = arr.map(typed mapper) is itself promoted to List<double> (its source arr is
        // promoted and the mapper is non-capturing number→number), then read via index/length.
        var source = """
            function f(): number {
                const arr: number[] = [];
                for (let i: number = 0; i < 5; i++) { arr.push(i); }
                const doubled = arr.map((x: number): number => x * 2);
                let s: number = 0;
                for (let i: number = 0; i < doubled.length; i++) { s = s + doubled[i]; }
                return s;
            }
            console.log(f());
            """;

        // [0,2,4,6,8] → 20
        Assert.Equal("20\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void TypedMap_ThenReduce_Chain(ExecutionMode mode)
    {
        // Full typed chain: arr (List<double>) → map → doubled (List<double>) → reduce → double.
        var source = """
            function f(): number {
                const arr: number[] = [];
                for (let i: number = 0; i < 5; i++) { arr.push(i); }
                const doubled = arr.map((x: number): number => x * 2);
                return doubled.reduce((a: number, x: number): number => a + x, 0);
            }
            console.log(f());
            """;

        // [0,2,4,6,8] → 20
        Assert.Equal("20\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void TypedMap_ResultReturned_FallsBackCorrectly(ExecutionMode mode)
    {
        // The map result escapes (returned), so it must NOT be a bare List<double> — falls back to
        // the $Array path and stays correct.
        var source = """
            function build(): number[] {
                const arr: number[] = [];
                for (let i: number = 0; i < 3; i++) { arr.push(i); }
                return arr.map((x: number): number => x + 10);
            }
            const r: number[] = build();
            console.log(r.length);
            console.log(r[0]);
            console.log(r[2]);
            """;

        Assert.Equal("3\n10\n12\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void TypedFilter_ThenLength(ExecutionMode mode)
    {
        var source = """
            function f(): number {
                const arr: number[] = [];
                for (let i: number = 0; i < 10; i++) { arr.push(i); }
                const evens = arr.filter((x: number): boolean => x % 2 === 0);
                let s: number = 0;
                for (let i: number = 0; i < evens.length; i++) { s = s + evens[i]; }
                return s;
            }
            console.log(f());
            """;

        // evens [0,2,4,6,8] → 20
        Assert.Equal("20\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void TypedPipeline_MapFilterReduce(ExecutionMode mode)
    {
        // The full array-methods benchmark shape: build → map → filter → reduce, every stage typed.
        var source = """
            function arrayMethodWork(n: number): number {
                const arr: number[] = [];
                for (let i: number = 0; i < n; i++) { arr.push(i); }
                const doubled = arr.map((x: number): number => x * 2);
                const evens = doubled.filter((x: number): boolean => x % 4 === 0);
                return evens.reduce((acc: number, x: number): number => acc + x, 0);
            }
            console.log(arrayMethodWork(10));
            """;

        // arr 0..9 → doubled [0,2,..,18] → evens (x%4===0) [0,4,8,12,16] → 40
        Assert.Equal("40\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void TypedPipeline_FusedBuildPreservesLoopBounds(ExecutionMode mode)
    {
        var source = """
            function work(n: number): number {
                const arr: number[] = [];
                for (let i: number = 0; i < n; i++) { arr.push(i); }
                const doubled = arr.map((x: number): number => x * 2);
                const evens = doubled.filter((x: number): boolean => x % 4 === 0);
                return evens.reduce((acc: number, x: number): number => acc + x, 0);
            }
            console.log(work(3.5), work(-1), work(NaN));
            """;

        // 3.5 executes four build iterations (0..3); negative and NaN execute none.
        Assert.Equal("4 0 0\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void TypedPipeline_ObservableCallbacksRetainPhaseOrder(ExecutionMode mode)
    {
        var source = """
            let order: string = "";
            function work(): number {
                const arr: number[] = [];
                for (let i: number = 0; i < 2; i++) { arr.push(i); }
                const doubled = arr.map((x: number): number => { order += "m"; return x * 2; });
                const kept = doubled.filter((x: number): boolean => { order += "f"; return true; });
                return kept.reduce((acc: number, x: number): number => { order += "r"; return acc + x; }, 0);
            }
            console.log(work(), order);
            """;

        Assert.Equal("2 mmffrr\n", TestHarness.Run(source, mode));
    }
}

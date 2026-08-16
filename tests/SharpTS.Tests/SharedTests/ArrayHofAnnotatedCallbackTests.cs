using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for #861: array higher-order-function callbacks (map/filter/reduce/forEach/
/// find/some/every) whose arrow has ANNOTATED parameters are de-virtualized in compiled
/// mode. An annotated callback like <c>(x: number): number =&gt; x*2</c> compiles to a typed
/// static method (<c>double(double)</c>) that cannot bind to the <c>Func&lt;object,object&gt;</c>
/// the <c>Array*Direct</c> helpers expect; a per-arrow boxed adapter bridges it, so the
/// per-element reflective <c>$TSFunction</c>/<c>MethodInvoker</c> dispatch is removed.
///
/// These run against BOTH the interpreter and the compiler. The point is interpreter/compiled
/// parity: the adapter's unbox/box must match the reflective path exactly, and the cases that
/// fall back (capturing arrows, multi-arg callbacks) must still produce correct results. A
/// wrong adapter coercion or a mis-fired fast path surfaces here as a compiled-mode mismatch.
/// </summary>
public class ArrayHofAnnotatedCallbackTests
{
    // ── Adapter path: annotated callbacks inside a function (the benchmark shape) ──

    [Theory, ModeData]
    public void AnnotatedMap_Number(ExecutionMode mode)
    {
        var source = """
            function f(): string {
                const a: number[] = [1, 2, 3, 4, 5];
                return a.map((x: number): number => x * 2).join(",");
            }
            console.log(f());
            """;
        Assert.Equal("2,4,6,8,10\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AnnotatedFilter_BooleanPredicate(ExecutionMode mode)
    {
        var source = """
            function f(): string {
                const a: number[] = [1, 2, 3, 4, 5, 6];
                return a.filter((x: number): boolean => x % 2 === 0).join(",");
            }
            console.log(f());
            """;
        Assert.Equal("2,4,6\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AnnotatedReduce_TwoTypedParams(ExecutionMode mode)
    {
        var source = """
            function f(): number {
                const a: number[] = [1, 2, 3, 4, 5];
                return a.reduce((acc: number, x: number): number => acc + x, 0);
            }
            console.log(f());
            """;
        Assert.Equal("15\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AnnotatedChain_MapFilterReduce(ExecutionMode mode)
    {
        // The array-methods benchmark shape: every callback param is annotated.
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
        Assert.Equal("40\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AnnotatedPredicates_FindSomeEveryFindIndex(ExecutionMode mode)
    {
        var source = """
            function f(): string {
                const a: number[] = [1, 2, 3, 4, 5];
                const found = a.find((x: number): boolean => x > 3);
                const hasBig = a.some((x: number): boolean => x > 4);
                const allPos = a.every((x: number): boolean => x > 0);
                const idx = a.findIndex((x: number): boolean => x === 3);
                return found + "," + hasBig + "," + allPos + "," + idx;
            }
            console.log(f());
            """;
        Assert.Equal("4,true,true,2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AnnotatedForEach_Void(ExecutionMode mode)
    {
        var source = """
            function f(): number {
                const a: number[] = [1, 2, 3, 4];
                let sum: number = 0;
                a.forEach((x: number): void => { sum = sum + x; });
                return sum;
            }
            console.log(f());
            """;
        Assert.Equal("10\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AnnotatedStringParam_MapsToLength(ExecutionMode mode)
    {
        var source = """
            function f(): string {
                const s: string[] = ["ab", "c", "defg"];
                return s.map((w: string): number => w.length).join(",");
            }
            console.log(f());
            """;
        Assert.Equal("2,1,4\n", TestHarness.Run(source, mode));
    }

    // ── Coercion parity: non-integer / NaN elements must marshal identically ──

    [Theory, ModeData]
    public void AnnotatedMap_FloatAndNaN(ExecutionMode mode)
    {
        var source = """
            function f(): string {
                const b: number[] = [1.5, NaN, 3];
                return b.map((x: number): number => x + 1).join(",");
            }
            console.log(f());
            """;
        Assert.Equal("2.5,NaN,4\n", TestHarness.Run(source, mode));
    }

    // ── Fallback cases: must NOT take the 1-arg adapter, must stay correct ──

    [Theory, ModeData]
    public void MultiArgCallback_UsesIndex_FallsBack(ExecutionMode mode)
    {
        // 2-param map callback uses the index → arity guard keeps it on the reflective
        // path; result must still be correct.
        var source = """
            function f(): string {
                const a: number[] = [10, 20, 30];
                return a.map((x: number, i: number): number => x + i).join(",");
            }
            console.log(f());
            """;
        Assert.Equal("10,21,32\n", TestHarness.Run(source, mode));
    }

    // ── #861 L3: capturing annotated arrows (instance adapter on the display class) ──

    [Theory, ModeData]
    public void CapturingAnnotatedArrow_Map(ExecutionMode mode)
    {
        // Capturing arrow → instance adapter on the display class (L3); the captured value must
        // marshal correctly through the adapter.
        var source = """
            function f(): string {
                const a: number[] = [1, 2, 3];
                const k: number = 10;
                return a.map((x: number): number => x + k).join(",");
            }
            console.log(f());
            """;
        Assert.Equal("11,12,13\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void CapturingAnnotatedArrow_Reduce(ExecutionMode mode)
    {
        var source = """
            function f(): number {
                const a: number[] = [1, 2, 3, 4, 5];
                const base: number = 100;
                return a.reduce((acc: number, x: number): number => acc + x + base, 0);
            }
            console.log(f());
            """;
        Assert.Equal("515\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void CapturingAnnotatedArrow_PerIterationFreshCapture(ExecutionMode mode)
    {
        // The display instance must be built FRESH at each call so each closure captures the
        // current loop variable, not a shared one.
        var source = """
            function f(): string {
                const fns: number[] = [];
                for (let i: number = 0; i < 3; i++) {
                    const arr: number[] = [10, 20];
                    fns.push(arr.map((x: number): number => x + i)[0]);
                }
                return fns.join(",");
            }
            console.log(f());
            """;
        Assert.Equal("10,11,12\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void CapturingBooleanPredicate_Filter(ExecutionMode mode)
    {
        // L3 (capturing instance adapter) + L4 (bool-returning adapter → *DirectBool): a captured
        // threshold in a typed boolean predicate must still filter correctly.
        var source = """
            function f(): string {
                const a: number[] = [1, 2, 3, 4, 5, 6];
                const t: number = 3;
                return a.filter((x: number): boolean => x > t).join(",");
            }
            console.log(f());
            """;
        Assert.Equal("4,5,6\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void BooleanPredicate_SomeEvery_ShortCircuit(ExecutionMode mode)
    {
        // L4 bool adapter under the short-circuiting some/every must agree with the reflective path.
        var source = """
            function f(): string {
                const a: number[] = [2, 4, 6, 7, 8];
                const someOdd = a.some((x: number): boolean => x % 2 === 1);
                const allEven = a.every((x: number): boolean => x % 2 === 0);
                return someOdd + "," + allEven;
            }
            console.log(f());
            """;
        Assert.Equal("true,false\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void CapturingAnnotatedArrow_Chained(ExecutionMode mode)
    {
        // Capturing arrows across a chained map().filter().reduce() (L1 capturing adapter + L2 round-trip).
        var source = """
            function f(): number {
                const a: number[] = [1, 2, 3, 4, 5];
                const m: number = 3;
                return a.map((x: number): number => x * m)
                        .filter((x: number): boolean => x > m)
                        .reduce((s: number, x: number): number => s + x, 0);
            }
            console.log(f());
            """;
        Assert.Equal("42\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void UntypedCallback_StillWorks(ExecutionMode mode)
    {
        // Unannotated callback keeps the pre-existing untyped direct/reflective path.
        var source = """
            function f(): string {
                const a: number[] = [1, 2, 3, 4, 5];
                return a.map(x => x + 1).join(",");
            }
            console.log(f());
            """;
        Assert.Equal("2,3,4,5,6\n", TestHarness.Run(source, mode));
    }

    // ── #861 L2: chained-stage round-trip elimination ──

    [Theory, ModeData]
    public void ChainedMapFilter_AssignedToArrayVariable(ExecutionMode mode)
    {
        // The final stage's result is consumed as an array value, so its $Array wrap must be
        // preserved (only the intermediate map result drops it). `.join` then works on it.
        var source = """
            function f(): string {
                const a: number[] = [1, 2, 3, 4, 5];
                const r = a.map((x: number): number => x * 2).filter((x: number): boolean => x > 4);
                return r.join(",");
            }
            console.log(f());
            """;
        Assert.Equal("6,8,10\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ChainedMapFilter_LengthOnResult(ExecutionMode mode)
    {
        // .length on the chain result requires the final $Array wrap to survive.
        var source = """
            function f(): number {
                const a: number[] = [1, 2, 3, 4, 5];
                return a.map((x: number): number => x * 2).filter((x: number): boolean => x > 5).length;
            }
            console.log(f());
            """;
        Assert.Equal("3\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ChainedMapSlice_MixedCallbackAndPlainArgs(ExecutionMode mode)
    {
        // Inner map (callback) feeds outer slice (plain args) — the bare List must flow across the
        // boundary and slice must consume it correctly.
        var source = """
            function f(): string {
                const a: number[] = [1, 2, 3, 4, 5];
                return a.map((x: number): number => x * 2).slice(1, 3).join(",");
            }
            console.log(f());
            """;
        Assert.Equal("4,6\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ChainedFilterMap_UntypedAndAnnotatedMixed(ExecutionMode mode)
    {
        // Inner filter is annotated, outer map is untyped — both stages chain through a bare List.
        var source = """
            function f(): string {
                const a: number[] = [1, 2, 3, 4, 5, 6];
                return a.filter((x: number): boolean => x % 2 === 0).map(x => x * 10).join(",");
            }
            console.log(f());
            """;
        Assert.Equal("20,40,60\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ConstBoundAnnotatedCallback_Resolves(ExecutionMode mode)
    {
        // const-bound arrow callback resolves through ConstArrowBindings and takes the
        // adapter path when annotated.
        var source = """
            const dbl = (x: number): number => x * 2;
            const out = [3, 4, 5].map(dbl).join(",");
            console.log(out);
            """;
        Assert.Equal("6,8,10\n", TestHarness.Run(source, mode));
    }
}

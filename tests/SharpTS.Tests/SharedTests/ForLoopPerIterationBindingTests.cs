using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regression tests for #633: each iteration of a <c>for (let/const …)</c> loop must get its own
/// binding for the loop variable(s) (ECMA-262 13.7.4 CreatePerIterationEnvironment), so closures
/// (and generators) created in different iterations capture distinct values.
///
/// <para>Before the fix the tree-walking interpreter reused a single environment slot for the loop
/// variable across iterations, so a closure capturing the loop variable read its <i>final</i> value
/// (e.g. <c>3,3,3</c> instead of <c>0,1,2</c>). Compiled mode (after the #431 box fix) already
/// snapshots per iteration, so this was an interpreter-vs-compiler divergence as well as a
/// conformance bug. The fix copies the loop variables into a fresh sibling environment per
/// iteration in <c>VisitFor</c> / <c>ExecuteForAsyncVT</c>.</para>
///
/// <para><c>var</c> declarations and bare-expression initializers share a single binding (no
/// per-iteration copy) — the negative cases below assert closures over those still read the final
/// value, matching JS.</para>
/// </summary>
public class ForLoopPerIterationBindingTests
{
    // ---- Headline repro: arrow capturing the loop `let` ----

    [Theory, ModeData]
    public void ArrowCapturesLetLoopVar_PerIteration(ExecutionMode mode)
    {
        var source = """
            const fns: any[] = [];
            for (let k = 0; k < 3; k++) { fns.push(() => k); }
            console.log(fns.map((f: any) => f()).join(","));
            """;
        Assert.Equal("0,1,2\n", TestHarness.Run(source, mode));
    }

    // The #622 headline repro: a generator created per iteration capturing the loop variable.
    [Theory, ModeData]
    public void GeneratorCapturesLetLoopVar_PerIteration(ExecutionMode mode)
    {
        var source = """
            const gens: any[] = [];
            for (let k = 0; k < 3; k++) { function* g() { yield k; } gens.push(g()); }
            console.log(gens.map((it: any) => it.next().value).join(","));
            """;
        Assert.Equal("0,1,2\n", TestHarness.Run(source, mode));
    }

    // ---- Multi-declarator: every declared name is per-iteration ----

    [Theory, ModeData]
    public void MultiDeclarator_AllNamesPerIteration(ExecutionMode mode)
    {
        var source = """
            const a: any[] = [];
            for (let i = 0, j = 10; i < 3; i++, j++) { a.push(() => i + ":" + j); }
            console.log(a.map((f: any) => f()).join(","));
            """;
        Assert.Equal("0:10,1:11,2:12\n", TestHarness.Run(source, mode));
    }

    // ---- continue keeps per-iteration semantics ----

    [Theory, ModeData]
    public void ContinuePath_StillPerIteration(ExecutionMode mode)
    {
        var source = """
            const c: any[] = [];
            for (let i = 0; i < 4; i++) { if (i === 2) continue; c.push(() => i); }
            console.log(c.map((f: any) => f()).join(","));
            """;
        Assert.Equal("0,1,3\n", TestHarness.Run(source, mode));
    }

    // ---- Nested loops: inner closure captures both per-iteration bindings ----

    [Theory, ModeData]
    public void NestedLoops_BothBindingsPerIteration(ExecutionMode mode)
    {
        var source = """
            const d: any[] = [];
            for (let i = 0; i < 2; i++) { for (let j = 0; j < 2; j++) { d.push(() => "" + i + j); } }
            console.log(d.map((f: any) => f()).join(","));
            """;
        Assert.Equal("00,01,10,11\n", TestHarness.Run(source, mode));
    }

    // ---- Single-statement body (no block braces) ----

    [Theory, ModeData]
    public void NoBraceBody_PerIteration(ExecutionMode mode)
    {
        var source = """
            const fns: any[] = [];
            for (let i = 0; i < 3; i++) fns.push(() => i);
            console.log(fns.map((f: any) => f()).join(","));
            """;
        Assert.Equal("0,1,2\n", TestHarness.Run(source, mode));
    }

    // ---- Mutating the loop var inside the body is reflected in that iteration's capture ----
    // i becomes i+10 at capture time, then i-10 before the iteration ends, so the per-iteration
    // binding settles back to the loop value; the per-iteration copy then carries that forward.
    // Compiled mode (#650) now allocates a per-iteration StrongBox cell for a loop binding that
    // the body BOTH mutates AND a closure captures, so closures reference-capture the live cell
    // (end-of-body value 0/1/2) instead of snapshotting the mid-body value (10/11/12). The cheap
    // value-snapshot path (#649) still serves the common non-mutating case.
    [Theory, ModeData]
    public void BodyMutatesLoopVar_CapturesLiveSlot(ExecutionMode mode)
    {
        var source = """
            const g: any[] = [];
            for (let i = 0; i < 3; i++) { i = i + 10; g.push(() => i); i = i - 10; }
            console.log(g.map((f: any) => f()).join(","));
            """;
        Assert.Equal("0,1,2\n", TestHarness.Run(source, mode));
    }

    // Same mutate-and-restore pattern inside a SYNC function body (cell lives as a local
    // in the function frame). Exercises the function-context EmitFor path.
    [Theory, ModeData]
    public void BodyMutatesLoopVar_SyncFunctionBody(ExecutionMode mode)
    {
        var source = """
            function main(): string {
                const g: any[] = [];
                for (let i = 0; i < 3; i++) { i = i + 10; g.push(() => i); i = i - 10; }
                return g.map((f: any) => f()).join(",");
            }
            console.log(main());
            """;
        Assert.Equal("0,1,2\n", TestHarness.Run(source, mode));
    }

    // The cell is shared by reference, so a closure that WRITES the loop binding mutates that
    // iteration's binding and a sibling reader observes it. Each iteration k: writer increments
    // binding k (k → k+1), reader returns k+1. node: 1,2,3.
    [Theory, ModeData]
    public void BodyMutatesLoopVar_ClosureWritesThrough(ExecutionMode mode)
    {
        var source = """
            const readers: any[] = [];
            const writers: any[] = [];
            for (let i = 0; i < 3; i++) { readers.push(() => i); writers.push(() => { i = i + 1; }); }
            writers.forEach((w: any) => w());
            console.log(readers.map((f: any) => f()).join(","));
            """;
        Assert.Equal("1,2,3\n", TestHarness.Run(source, mode));
    }

    // Same as above but the writer uses `i++` (postfix increment in a closure), exercising
    // the increment emitter's captured-cell write-through path.
    [Theory, ModeData]
    public void BodyMutatesLoopVar_ClosureIncrementsThrough(ExecutionMode mode)
    {
        var source = """
            const readers: any[] = [];
            const writers: any[] = [];
            for (let i = 0; i < 3; i++) { readers.push(() => i); writers.push(() => { i++; }); }
            writers.forEach((w: any) => w());
            console.log(readers.map((f: any) => f()).join(","));
            """;
        Assert.Equal("1,2,3\n", TestHarness.Run(source, mode));
    }

    // Nested loops where BOTH loop vars are mutate-and-restored and captured by the inner
    // closure — each needs its own cell, copied forward independently.
    [Theory, ModeData]
    public void NestedLoops_BothBindingsMutated(ExecutionMode mode)
    {
        var source = """
            const d: any[] = [];
            for (let i = 0; i < 2; i++) {
                for (let j = 0; j < 2; j++) {
                    i = i + 5; j = j + 5;
                    d.push(() => "" + i + j);
                    i = i - 5; j = j - 5;
                }
            }
            console.log(d.map((f: any) => f()).join(","));
            """;
        Assert.Equal("00,01,10,11\n", TestHarness.Run(source, mode));
    }

    // #650 Phase 2 / #817: the mutate-and-restore fix works in all state-machine contexts
    // (async function / generator / async generator / async arrow), as long as the loop body
    // has no direct await/yield — the per-iteration cell is an IL local that lives for the
    // whole loop within one MoveNext segment. Loops whose body itself suspends remain on the
    // snapshot path (cell-as-field is a further follow-up).
    [Theory, ModeData]
    public void BodyMutatesLoopVar_AsyncFunctionBody(ExecutionMode mode)
    {
        var source = """
            async function main() {
                const g: any[] = [];
                for (let i = 0; i < 3; i++) { i = i + 10; g.push(() => i); i = i - 10; }
                console.log(g.map((f: any) => f()).join(","));
            }
            main();
            """;
        Assert.Equal("0,1,2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void BodyMutatesLoopVar_GeneratorBody(ExecutionMode mode)
    {
        var source = """
            function* gen() {
                const g: any[] = [];
                for (let i = 0; i < 3; i++) { i = i + 10; g.push(() => i); i = i - 10; }
                yield g.map((f: any) => f()).join(",");
            }
            console.log(gen().next().value);
            """;
        Assert.Equal("0,1,2\n", TestHarness.Run(source, mode));
    }

    // Async generator: consumed via `.next()` (a compiled async generator consumed through
    // `for await…of` is a separate pre-existing gap that hangs even without the mutation).
    [Theory, ModeData]
    public void BodyMutatesLoopVar_AsyncGeneratorBody(ExecutionMode mode)
    {
        var source = """
            async function* agen() {
                const g: any[] = [];
                for (let i = 0; i < 3; i++) { i = i + 10; g.push(() => i); i = i - 10; }
                yield g.map((f: any) => f()).join(",");
            }
            async function main() {
                const it: any = agen();
                const r = await it.next();
                console.log(r.value);
            }
            main();
            """;
        Assert.Equal("0,1,2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void BodyMutatesLoopVar_AsyncArrowBody(ExecutionMode mode)
    {
        var source = """
            const run = async () => {
                const g: any[] = [];
                for (let i = 0; i < 3; i++) { i = i + 10; g.push(() => i); i = i - 10; }
                console.log(g.map((f: any) => f()).join(","));
            };
            run();
            """;
        Assert.Equal("0,1,2\n", TestHarness.Run(source, mode));
    }

    // ---- Negative: `var` is function-scoped — one shared binding, closures read the final value ----

    [Theory, ModeData]
    public void VarLoopVar_SharedBinding_ReadsFinal(ExecutionMode mode)
    {
        var source = """
            const fns: any[] = [];
            for (var k = 0; k < 3; k++) { fns.push(() => k); }
            console.log(fns.map((f: any) => f()).join(","));
            """;
        Assert.Equal("3,3,3\n", TestHarness.Run(source, mode));
    }

    // ---- Negative: a bare-expression initializer assigns an outer var — one shared binding ----

    [Theory, ModeData]
    public void ExpressionInitializer_OuterVar_ReadsFinal(ExecutionMode mode)
    {
        var source = """
            let m = 0;
            const fns: any[] = [];
            for (m = 0; m < 3; m++) { fns.push(() => m); }
            console.log(fns.map((f: any) => f()).join(","));
            """;
        Assert.Equal("3,3,3\n", TestHarness.Run(source, mode));
    }

    // ---- A fresh per-iteration const is unaffected (already correct in both modes) ----

    [Theory, ModeData]
    public void InnerConstSnapshot_StillWorks(ExecutionMode mode)
    {
        var source = """
            const fns: any[] = [];
            for (let i = 0; i < 3; i++) { const v = i * 2; fns.push(() => v); }
            console.log(fns.map((f: any) => f()).join(","));
            """;
        Assert.Equal("0,2,4\n", TestHarness.Run(source, mode));
    }

    // ---- Closures over a loop `let` inside a FUNCTION body are per-iteration in both modes (#649) ----
    // Before #649 the compiler promoted a captured `for (let …)` loop variable to the function's
    // single shared display class, so every closure read the loop's final value (3,3,3) inside any
    // function body — sync OR async. (Top level already stayed correct because the loop variable
    // remained a local that each closure snapshots.) The fix keeps these per-iteration loop bindings
    // out of the function display class, so they are snapshotted per iteration like the top-level case.

    [Theory, ModeData]
    public void SyncFunctionBody_PerIteration(ExecutionMode mode)
    {
        var source = """
            function main(): string {
                const fns: any[] = [];
                for (let k = 0; k < 3; k++) { fns.push(() => k); }
                return fns.map((f: any) => f()).join(",");
            }
            console.log(main());
            """;
        Assert.Equal("0,1,2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncFunctionBody_PerIteration(ExecutionMode mode)
    {
        var source = """
            async function main() {
                const fns: any[] = [];
                for (let k = 0; k < 3; k++) { fns.push(() => k); }
                console.log(fns.map((f: any) => f()).join(","));
            }
            main();
            """;
        Assert.Equal("0,1,2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ClassMethodBody_PerIteration(ExecutionMode mode)
    {
        var source = """
            class C {
                run(): string {
                    const fns: any[] = [];
                    for (let k = 0; k < 3; k++) { fns.push(() => k); }
                    return fns.map((f: any) => f()).join(",");
                }
            }
            console.log(new C().run());
            """;
        Assert.Equal("0,1,2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ArrowBody_PerIteration(ExecutionMode mode)
    {
        var source = """
            const outer = () => {
                const fns: any[] = [];
                for (let k = 0; k < 3; k++) { fns.push(() => k); }
                return fns.map((f: any) => f()).join(",");
            };
            console.log(outer());
            """;
        Assert.Equal("0,1,2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void InnerFunctionBody_PerIteration(ExecutionMode mode)
    {
        var source = """
            function host(): string {
                function inner(): string {
                    const fns: any[] = [];
                    for (let k = 0; k < 3; k++) { fns.push(() => k); }
                    return fns.map((f: any) => f()).join(",");
                }
                return inner();
            }
            console.log(host());
            """;
        Assert.Equal("0,1,2\n", TestHarness.Run(source, mode));
    }

    // A function-scoped binding declared OUTSIDE the loop stays SHARED (one binding) even when a
    // loop-body closure also captures the per-iteration loop variable: the per-iteration exclusion
    // must apply only to the loop binding, not to ordinary captured locals. `outer` ends at 3 for
    // every closure; `k` is per-iteration.
    [Theory, ModeData]
    public void FunctionBody_OuterCapturedVarStaysShared(ExecutionMode mode)
    {
        var source = """
            function f(): string {
                let outer = 0;
                const fns: any[] = [];
                for (let k = 0; k < 3; k++) { fns.push(() => outer + ":" + k); outer++; }
                return fns.map((g: any) => g()).join(",");
            }
            console.log(f());
            """;
        Assert.Equal("3:0,3:1,3:2\n", TestHarness.Run(source, mode));
    }
}

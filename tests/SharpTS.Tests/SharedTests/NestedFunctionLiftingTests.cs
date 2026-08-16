using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for nested function-like lowering on the compile path (#470, #501, #583).
///
/// The compiler relocates non-capturing nested generator/async/state-machine-nested function
/// declarations to the module top level (<c>Compilation/NestedFunctionLifter.cs</c>) so the mature
/// top-level state-machine pipeline can lower them. These tests assert interpreter/compiler parity
/// for the supported (non-capturing) shapes and pin the documented limitations (#583 §1/§2).
/// </summary>
public class NestedFunctionLiftingTests
{
    // ── #470: a plain function declared inside a state-machine body ──────────────────────────────

    [Theory, ModeData]
    public void PlainFunction_NestedInGeneratorBody_IsCallable(ExecutionMode mode)
    {
        var source = """
            function* outer(): Generator<number> {
                function helper(): number { return 7; }
                yield helper();
            }
            for (const v of outer()) console.log(v);
            """;
        Assert.Equal("7\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void PlainFunction_NestedInAsyncBody_IsCallable(ExecutionMode mode)
    {
        var source = """
            async function outer(): Promise<number> {
                function helper(): number { return 9; }
                return helper();
            }
            outer().then(r => console.log(r));
            """;
        Assert.Equal("9\n", TestHarness.Run(source, mode));
    }

    // ── #501: a nested generator/async function declaration ──────────────────────────────────────

    [Theory, ModeData]
    public void Generator_NestedInPlainFunction_IsCallable(ExecutionMode mode)
    {
        var source = """
            function outer(): number[] {
                function* g(): Generator<number> { yield 42; }
                return [...g()];
            }
            console.log(JSON.stringify(outer()));
            """;
        Assert.Equal("[42]\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_NestedInGeneratorBody_NonCapturing_IsCallable(ExecutionMode mode)
    {
        var source = """
            function* outer(): Generator<number> {
                function* inner(): Generator<number> { yield 1; yield 2; }
                yield* inner();
                yield 3;
            }
            console.log(JSON.stringify([...outer()]));
            """;
        Assert.Equal("[1,2,3]\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncFunction_NestedInPlainFunction_IsCallable(ExecutionMode mode)
    {
        var source = """
            function outer(): Promise<number> {
                async function inner(): Promise<number> { return 5; }
                return inner();
            }
            outer().then(r => console.log(r));
            """;
        Assert.Equal("5\n", TestHarness.Run(source, mode));
    }

    // ── Recursion: self-reference must resolve to the relocated declaration ───────────────────────

    [Theory, ModeData]
    public void RecursiveNestedFunction_InGeneratorBody(ExecutionMode mode)
    {
        var source = """
            function* outer(): Generator<number> {
                function fact(n: number): number { return n <= 1 ? 1 : n * fact(n - 1); }
                yield fact(5);
            }
            for (const v of outer()) console.log(v);
            """;
        Assert.Equal("120\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void RecursiveNestedGenerator_InPlainFunction(ExecutionMode mode)
    {
        var source = """
            function driver(): number[] {
                function* count(n: number): Generator<number> {
                    if (n > 0) { yield n; yield* count(n - 1); }
                }
                return [...count(3)];
            }
            console.log(JSON.stringify(driver()));
            """;
        Assert.Equal("[3,2,1]\n", TestHarness.Run(source, mode));
    }

    // ── Multiple siblings and name independence across scopes ────────────────────────────────────

    [Theory, ModeData]
    public void MultipleIndependentNestedHelpers(ExecutionMode mode)
    {
        var source = """
            function* outer(): Generator<number> {
                function a(): number { return 1; }
                function b(): number { return 2; }
                yield a();
                yield b();
            }
            console.log(JSON.stringify([...outer()]));
            """;
        Assert.Equal("[1,2]\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void SameNamedNestedHelpers_InDifferentFunctions_StayIndependent(ExecutionMode mode)
    {
        // Each relocated declaration gets a fresh unique top-level name, so two generators that both
        // declare a nested `h` do not collide.
        var source = """
            function* p(): Generator<number> { function h(): number { return 1; } yield h(); }
            function* q(): Generator<number> { function h(): number { return 2; } yield h(); }
            console.log(JSON.stringify([...p()]), JSON.stringify([...q()]));
            """;
        Assert.Equal("[1] [2]\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void LiftedHelper_DoesNotHijack_SameNamedBindingElsewhere(ExecutionMode mode)
    {
        // `a`'s nested `h` is relocated; `b`'s own (unrelated) nested `h` must keep returning 20.
        // Guards against a relocated top-level name shadowing a same-named binding in another scope.
        var source = """
            function* a(): Generator<number> { function h(): number { return 10; } yield h(); }
            function b(): number { function h(): number { return 20; } return h(); }
            console.log(JSON.stringify([...a()]), b());
            """;
        Assert.Equal("[10] 20\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NestedGenerator_NameCollidesWithTopLevelFunction_IsLifted(ExecutionMode mode)
    {
        // The nested generator `a3` shares a name with a top-level function. It is relocated under a
        // fresh name and aliased by a function-local that correctly shadows the top-level `a3`
        // (#607). Before the name-collision guard was relaxed, the lifter declined this and the
        // nested generator failed to compile with "Yield not supported in this context".
        var source = """
            function a3(): number { return 1; }
            function t3(): number { function* a3(): Generator<number> { yield 23; } return a3().next().value; }
            console.log(a3(), t3());
            """;
        Assert.Equal("1 23\n", TestHarness.Run(source, mode));
    }

    // ── Deep nesting ─────────────────────────────────────────────────────────────────────────────

    [Theory, ModeData]
    public void DeeplyNestedGenerators(ExecutionMode mode)
    {
        var source = """
            function outer(): number[] {
                function* g(): Generator<number> {
                    function* h(): Generator<number> { yield 99; }
                    yield* h();
                    yield 1;
                }
                return [...g()];
            }
            console.log(JSON.stringify(outer()));
            """;
        Assert.Equal("[99,1]\n", TestHarness.Run(source, mode));
    }

    // ── Generator value-references to a top-level function (the lift's alias relies on this) ──────

    [Theory, ModeData]
    public void TopLevelFunction_ReferencedAsValueInsideGenerator(ExecutionMode mode)
    {
        // A top-level function used as a value (not a direct call) inside a generator body must
        // resolve to a callable function, not null.
        var source = """
            function helper(): number { return 7; }
            function* outer(): Generator<string> {
                const h = helper;
                yield typeof h;
                yield String(h());
            }
            for (const v of outer()) console.log(v);
            """;
        Assert.Equal("function\n7\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NestedHelper_YieldedAsValue_IsCallable(ExecutionMode mode)
    {
        var source = """
            function* makeGen(): Generator<any> {
                function helper(): number { return 1; }
                yield helper;
            }
            const fn = makeGen().next().value;
            console.log(typeof fn, fn());
            """;
        Assert.Equal("function 1\n", TestHarness.Run(source, mode));
    }

    // ── #605: function/generator declared inside a MODULE-LEVEL block/loop/if ────────────────────
    // No enclosing function exists, so these are bound by neither the top-level definition pass nor
    // the inner-function pass and previously threw "Undefined variable" in compiled mode. The lifter
    // now relocates the non-capturing ones to the module top level.

    [Theory, ModeData]
    public void PlainFunction_InTopLevelBlock_IsCallable(ExecutionMode mode)
    {
        var source = """
            { function f() { return 1; } console.log(f()); }
            """;
        Assert.Equal("1\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Generator_InTopLevelBlock_IsCallable(ExecutionMode mode)
    {
        var source = """
            { function* g() { yield 1; yield 2; } console.log([...g()].join(",")); }
            """;
        Assert.Equal("1,2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void BlockFunction_ReferencingModuleBinding_IsCallable(ExecutionMode mode)
    {
        // A block function that references a module-level binding (not a block/loop-scoped one) is
        // safe to lift — the reference still resolves after relocation. Guards the capture-guard from
        // over-rejecting safe references (it must only block enclosing block/loop captures).
        var source = """
            const base = 10;
            { function addBase(n: number): number { return n + base; } console.log(addBase(5)); }
            """;
        Assert.Equal("15\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void RecursiveBlockFunction_IsCallable(ExecutionMode mode)
    {
        var source = """
            { function fact(n: number): number { return n <= 1 ? 1 : n * fact(n - 1); } console.log(fact(5)); }
            """;
        Assert.Equal("120\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Function_InTopLevelIfBlock_IsCallable(ExecutionMode mode)
    {
        var source = """
            if (true) { function inIf(): string { return "yes"; } console.log(inIf()); }
            """;
        Assert.Equal("yes\n", TestHarness.Run(source, mode));
    }

    // ── #622: capturing declarations inside a MODULE-LEVEL block/loop are lambda-lifted ──────────
    // A function/generator/async declared in a module-level block that captures an enclosing
    // block/loop binding is relocated to a top-level declaration whose leading parameters are the
    // captured bindings, with an in-place arrow that closes over them and forwards them. This is the
    // only route that lowers a capturing GENERATOR/ASYNC (the compiler cannot emit one as a capturing
    // closure directly). Previously these threw "ReferenceError: Undefined variable" in compiled mode.

    [Theory, ModeData]
    public void BlockFunction_CapturingBlockConst_IsCallable(ExecutionMode mode)
    {
        var source = """
            { const base = 5; function add(n: number): number { return n + base; } console.log(add(1)); }
            """;
        Assert.Equal("6\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void BlockFunction_CapturingMultipleBlockBindings_IsCallable(ExecutionMode mode)
    {
        var source = """
            { const a = 3; const b = 4; function sum(n: number): number { return a + b + n; } console.log(sum(5)); }
            """;
        Assert.Equal("12\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void BlockAsyncFunction_CapturingBlockConst_IsCallable(ExecutionMode mode)
    {
        var source = """
            { const base = "y"; async function af(): Promise<string> { return base; } af().then((v: string) => console.log(v)); }
            """;
        Assert.Equal("y\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void RecursiveBlockGenerator_CapturingBlockConst_IsCallable(ExecutionMode mode)
    {
        // The function's own name is forwarded too, so the relocated body's self-calls resolve to
        // the forwarding arrow.
        var source = """
            { const lim = 3; function* count(n: number): Generator<number> { yield n; if (n + 1 < lim) yield* count(n + 1); }
              console.log([...count(0)].join(",")); }
            """;
        Assert.Equal("0,1,2\n", TestHarness.Run(source, mode));
    }

    // The headline #622 repro: a generator in a module-level for-loop capturing the loop variable.
    // Compiled mode now lowers it conformantly — each iteration builds a fresh closure over that
    // iteration's `let` binding, so the captured values are 0,1,2. (The interpreter still shares a
    // single binding and yields 3,3,3 — a separate, pre-existing per-iteration `let` defect, #631-
    // adjacent — so this is pinned for the compiler only.)
    [Fact]
    public void GeneratorCapturingModuleLevelLoopVar_IsLoweredConformantly()
    {
        var source = """
            const gens: any[] = [];
            for (let k = 0; k < 3; k++) { function* g() { yield k; } gens.push(g()); }
            console.log(gens.map((it: any) => it.next().value).join(","));
            """;
        Assert.Equal("0,1,2\n", TestHarness.RunCompiled(source));
    }

    // ── #534/#583 §1: capturing an enclosing FUNCTION scope IS lambda-lifted ─────────────────────
    // A nested state-machine declaration that captures a local of an enclosing FUNCTION is lambda-
    // lifted (the captured local becomes a leading parameter forwarded by an arrow); it runs in both
    // modes. The decl-before-use form is pinned by GeneratorClosureCaptureTests; this pins the
    // FORWARD-reference form (#534): the reference precedes the declaration, so the forwarding binding
    // must be hoisted to the body top. The enclosing function here is PLAIN — a forwarding arrow in a
    // plain body reads its captures live at call time, so hoisting it above the local's assignment is
    // safe. An ASYNC function encloser behaves the same way and is also lifted (#924, pinned below).
    // Only a GENERATOR encloser snapshots its captures at creation, so its forward-reference form stays
    // unsupported and fails cleanly rather than miscompiling.

    [Theory, ModeData]
    public void CapturingNestedGenerator_ForwardReference_IsLifted(ExecutionMode mode)
    {
        var source = """
            function outer(): number[] {
                const x = 10;
                const g = inner;
                const r = [...g()];
                function* inner(): Generator<number> { yield x; yield x + 1; }
                return r;
            }
            console.log(outer().join(","));
            """;
        Assert.Equal("10,11\n", TestHarness.Run(source, mode));
    }

    // #924: function declarations hoist inside an ASYNC function body just as in a sync body, so a
    // forward reference to a function declared later resolves. Previously the interpreter raised
    // "Undefined variable" (ExecuteBlockAsync never ran the hoisting pass).
    [Theory, ModeData]
    public void PlainFunctionDeclaration_ForwardReference_InAsyncFunction_IsHoisted(ExecutionMode mode)
    {
        var source = """
            async function outer(): Promise<number> {
                const r = foo();
                function foo(): number { return 7; }
                return r;
            }
            outer().then((r: number) => console.log(r));
            """;
        Assert.Equal("7\n", TestHarness.Run(source, mode));
    }

    // #924: a capturing generator declaration whose forward reference precedes it, inside an ASYNC
    // (non-generator) function. The interpreter hoists it; the compiler hoists its forwarding binding
    // (the async encloser reads captures live at call time, unlike a generator encloser).
    [Theory, ModeData]
    public void CapturingGeneratorDeclaration_ForwardReference_InAsyncFunction_IsLifted(ExecutionMode mode)
    {
        var source = """
            async function outer(): Promise<number[]> {
                let y = 9;
                const arr = [...gen()];
                function* gen(): Generator<number> { yield y; yield y + 1; }
                return arr;
            }
            outer().then((r: number[]) => console.log(r.join(",")));
            """;
        Assert.Equal("9,10\n", TestHarness.Run(source, mode));
    }

    // A block declaration that uses `this`/`arguments`, or has rest/default parameters, is declined
    // by the lambda-lift (the forwarding arrow cannot faithfully reproduce the call). It must fail
    // cleanly in compiled mode, never miscompile to a wrong value.
    [Fact]
    public void BlockFunction_WithRestParam_CapturingBlockConst_FailsCleanly()
    {
        var source = """
            { const b = 1; function f(...args: number[]): number { return b + args.length; } console.log(f(9, 9)); }
            """;
        string compiled;
        try { compiled = TestHarness.RunCompiled(source); }
        catch { compiled = "<compile-or-runtime-error>"; }
        // The interpreter result is "3"; a clean failure is acceptable, a different (wrong) value is not.
        Assert.True(compiled == "<compile-or-runtime-error>" || compiled == "3\n", $"unexpected: {compiled}");
    }
}

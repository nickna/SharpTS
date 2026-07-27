using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regression tests for #588: a compiled class method/accessor that completes by
/// <b>falling off the end</b> of its body (no explicit <c>return &lt;expr&gt;</c>) must
/// produce the JavaScript <c>undefined</c> sentinel, not CLR <c>null</c>.
///
/// <para>Before the fix the method epilogue hard-coded <c>ldnull</c> for the implicit
/// completion value (so <c>typeof new C().inst()</c> was <c>"object"</c> instead of
/// <c>"undefined"</c>). The fix routes every class method/accessor epilogue — instance,
/// static, private, getter/setter, the <c>@lock</c> deferred-return default, and the
/// class-<i>expression</i> equivalents — through <c>EmitDefaultReturnValue</c>, which
/// materializes <c>$Undefined</c> for an <c>object</c> slot while preserving the correct
/// default for typed/void slots. An explicit <c>return null</c> / <c>return &lt;value&gt;</c>
/// goes through <c>EmitReturn</c> (untouched) and is asserted to be preserved.</para>
///
/// <para>Public instance/static methods are invoked through the interpreter's boxing-free
/// <c>CallV2</c> path, which is already spec-correct. Getters and private methods are invoked
/// through the interpreter's legacy boxed <c>Call</c> path; #603 fixed that path to return the
/// <c>undefined</c> sentinel off the end as well (mirroring <c>CallV2</c>), so every case below
/// now asserts cross-mode parity.</para>
/// </summary>
public class MethodCompletionValueTests
{
    // ---- Off-the-end instance / static methods (the issue's primary repro) ----
    // The interpreter is already correct (CallV2), so these assert cross-mode parity.

    [Theory, ModeData]
    public void OffEndInstanceMethod_IsUndefined(ExecutionMode mode)
    {
        var source = """
            class C { inst() {} }
            console.log(typeof new C().inst());
            """;
        Assert.Equal("undefined\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void OffEndStaticMethod_IsUndefined(ExecutionMode mode)
    {
        var source = """
            class C { static stat() {} }
            console.log(typeof C.stat());
            """;
        Assert.Equal("undefined\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void OffEndInstanceMethod_StrictlyEqualsUndefined(ExecutionMode mode)
    {
        var source = """
            class C { inst() {} }
            console.log(new C().inst() === undefined);
            """;
        Assert.Equal("true\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void OffEndInstanceMethod_InArithmetic_IsNaN(ExecutionMode mode)
    {
        // undefined + 10 === NaN, but null + 10 === 10 — this distinguishes the
        // undefined sentinel from CLR null beyond the typeof check. The `as any` is
        // required because `inst()`'s return type now correctly infers `void` (#661/#658)
        // and `void + 10` is a type error (TS2365, matching tsc); the cast keeps this a
        // pure runtime-sentinel probe.
        var source = """
            class C { inst() {} }
            console.log((new C().inst() as any) + 10);
            """;
        Assert.Equal("NaN\n", TestHarness.Run(source, mode));
    }

    // A method that reaches the epilogue after a conditional branch (real body, still
    // falls off the end on the taken path) — exercises the epilogue, not a bare return.
    [Theory, ModeData]
    public void MethodFallingOffEndAfterBranch_IsUndefined(ExecutionMode mode)
    {
        var source = """
            class C {
                m(x: number) { if (x > 100) { return "big"; } }
            }
            console.log(typeof new C().m(1));
            """;
        Assert.Equal("undefined\n", TestHarness.Run(source, mode));
    }

    // ---- Class-expression methods (separate emit path) ----
    // Interpreter invokes these via CallV2 too, so cross-mode parity holds.

    [Theory, ModeData]
    public void OffEndClassExpressionInstanceMethod_IsUndefined(ExecutionMode mode)
    {
        var source = """
            const C = class { ie() {} };
            console.log(typeof new C().ie());
            """;
        Assert.Equal("undefined\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void OffEndClassExpressionStaticMethod_IsUndefined(ExecutionMode mode)
    {
        var source = """
            const C = class { static se() {} };
            console.log(typeof C.se());
            """;
        Assert.Equal("undefined\n", TestHarness.Run(source, mode));
    }

    // ---- Regression: explicit returns must NOT be rewritten to undefined ----
    // These go through EmitReturn (untouched by the fix); asserts cross-mode parity.

    [Theory, ModeData]
    public void ExplicitReturnNull_StaysNull(ExecutionMode mode)
    {
        // `return null` is observably distinct from `undefined` — it must be preserved.
        var source = """
            class C { f() { return null; } }
            console.log(typeof new C().f());
            console.log(new C().f() === null);
            """;
        Assert.Equal("object\ntrue\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ExplicitReturnValue_IsPreserved(ExecutionMode mode)
    {
        var source = """
            class C {
                f() { return 5; }
                g(): number { return 7; }
                static s() { return "hi"; }
            }
            console.log(new C().f());
            console.log(new C().g());
            console.log(C.s());
            """;
        Assert.Equal("5\n7\nhi\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void BranchedExplicitReturns_StillWork(ExecutionMode mode)
    {
        var source = """
            class C {
                pick(x: number) {
                    if (x > 0) { return "pos"; }
                    return "nonpos";
                }
            }
            const c = new C();
            console.log(c.pick(1) + "," + c.pick(-1));
            """;
        Assert.Equal("pos,nonpos\n", TestHarness.Run(source, mode));
    }

    // ---- Regression: setter with an off-the-end body still applies its effect ----
    // The setter's CLR return value is discarded; the assignment side effect and the
    // value of the assignment expression (the RHS, per JS) must be unaffected.

    [Theory, ModeData]
    public void SetterOffEnd_StillAppliesEffect(ExecutionMode mode)
    {
        var source = """
            class C {
                _v: number = 0;
                set v(n: number) { this._v = n; }
                get v(): number { return this._v; }
            }
            const c = new C();
            const assigned = (c.v = 42);
            console.log(c.v + "," + assigned);
            """;
        Assert.Equal("42,42\n", TestHarness.Run(source, mode));
    }

    // ---- Getters & private methods (interpreter boxed `Call` path) ----
    // The interpreter invokes these through the legacy boxed `Call`. #588 fixed the compiled
    // epilogue; #603 fixed the interpreter's boxed `Call` to return the `undefined` sentinel
    // off the end, so these now assert cross-mode parity (previously compiled-only).

    [Theory, ModeData]
    public void OffEndGetter_IsUndefined(ExecutionMode mode)
    {
        var source = """
            class C { get gv() {} }
            console.log(typeof new C().gv);
            """;
        Assert.Equal("undefined\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void OffEndStaticGetter_IsUndefined(ExecutionMode mode)
    {
        var source = """
            class C { static get gv() {} }
            console.log(typeof C.gv);
            """;
        Assert.Equal("undefined\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void GetterFallingOffEndAfterBranch_IsUndefined(ExecutionMode mode)
    {
        var source = """
            class C {
                _f: boolean = false;
                get v() { if (this._f) { return 1; } }
            }
            console.log(typeof new C().v);
            """;
        Assert.Equal("undefined\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void OffEndPrivateMethod_IsUndefined(ExecutionMode mode)
    {
        var source = """
            class C {
                #secret() {}
                call() { return this.#secret(); }
            }
            console.log(typeof new C().call());
            """;
        Assert.Equal("undefined\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void OffEndPrivateStaticMethod_IsUndefined(ExecutionMode mode)
    {
        var source = """
            class C {
                static #ssecret() {}
                static call() { return C.#ssecret(); }
            }
            console.log(typeof C.call());
            """;
        Assert.Equal("undefined\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void OffEndClassExpressionGetter_IsUndefined(ExecutionMode mode)
    {
        var source = """
            const C = class { get ev() {} };
            console.log(typeof new C().ev);
            """;
        Assert.Equal("undefined\n", TestHarness.Run(source, mode));
    }

    // Off-the-end getter/private completion must be `undefined`, not CLR `null` — assert via
    // arithmetic (undefined + 10 === NaN, but null + 10 === 10) so it can't pass by `typeof`
    // coincidence. This is the #603 distinguisher on the interpreter's boxed `Call` path.
    [Theory, ModeData]
    public void OffEndGetter_InArithmetic_IsNaN(ExecutionMode mode)
    {
        var source = """
            class C { get gv() {} }
            console.log((new C().gv as any) + 10);
            """;
        Assert.Equal("NaN\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void OffEndPrivateMethod_StrictlyEqualsUndefined(ExecutionMode mode)
    {
        var source = """
            class C {
                #secret() {}
                call() { return this.#secret() === undefined; }
            }
            console.log(new C().call());
            """;
        Assert.Equal("true\n", TestHarness.Run(source, mode));
    }

    // Regression for #603: only the *off-the-end* completion becomes `undefined`. A getter with
    // an explicit `return null` must still observe CLR `null` (boxed `Call` returns via the
    // Return branch, untouched by the fix).
    [Theory, ModeData]
    public void GetterExplicitReturnNull_StaysNull(ExecutionMode mode)
    {
        var source = """
            class C { get gv() { return null; } }
            console.log(typeof new C().gv);
            console.log(new C().gv === null);
            """;
        Assert.Equal("object\ntrue\n", TestHarness.Run(source, mode));
    }

    // ---- @lock decorator: the deferred-return default ----
    // The @lock try/finally stores an implicit completion value into an object-typed
    // local before running the unlock finally; that default was `ldnull` and is now the
    // `$Undefined` sentinel. @lock is a compiled-mode code path (Stage3 decorators).

    [Fact]
    public void LockedInstanceMethodOffEnd_IsUndefined()
    {
        var source = """
            class C {
                @lock
                run(): void {}
            }
            console.log(typeof new C().run());
            """;
        Assert.Equal("undefined\n", TestHarness.RunCompiled(source, DecoratorMode.Stage3));
    }

    [Fact]
    public void LockedStaticMethodOffEnd_IsUndefined()
    {
        var source = """
            class C {
                @lock
                static run(): void {}
            }
            console.log(typeof C.run());
            """;
        Assert.Equal("undefined\n", TestHarness.RunCompiled(source, DecoratorMode.Stage3));
    }

    [Fact]
    public void LockedMethodWithExplicitReturn_IsPreserved()
    {
        // Regression: the @lock deferred-return must still surface an explicit value.
        var source = """
            class C {
                total: number = 0;
                @lock
                add(n: number): number { this.total = this.total + n; return this.total; }
            }
            const c = new C();
            console.log(c.add(5) + "," + c.add(10));
            """;
        Assert.Equal("5,15\n", TestHarness.RunCompiled(source, DecoratorMode.Stage3));
    }
}

using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regression tests for #1237: a <c>function</c> declaration inside a <b>class method</b> body must be
/// referenceable (and callable) in compiled mode, matching the interpreter.
///
/// <para>The class-method emission path (<c>ILCompiler.Classes.Methods.cs</c> /
/// <c>ILCompiler.Classes.Static.cs</c>) emitted a method body via a bare
/// <c>foreach EmitStatement</c> and never wired the inner-function materializer, so an inner
/// <c>function</c> in a method was collected (its method and display class were emitted) but never
/// materialized into a binding — every reference fell through to <c>ThrowUndefinedVariable</c>
/// (<c>ReferenceError: Undefined variable</c>). This affected non-capturing, capturing,
/// top-level-in-method, and block-nested declarations, on instance, static, and private methods.</para>
///
/// <para>The fix wires the in-place materializer onto the method context
/// (<c>WireInPlaceInnerFunctions</c>) so each inner function declaration is created at its textual
/// position by the statement emitter's <c>Stmt.Function</c> arm. Unlike a plain function or arrow, a
/// method has no function-level display class; hoisting a capturing inner function to the top of the
/// body would snapshot its captured method-locals before the body assigns them (null capture), so
/// in-place materialization is used instead — its snapshot sees the already-assigned values, matching
/// how arrows inside methods capture. The type checker rejects forward references to a method's inner
/// functions, so no valid program needs the top-of-body two-pass hoist here.</para>
/// </summary>
public class InnerFunctionInMethodTests
{
    // ---- The headline #1237 repro: non-capturing inner function in an instance method ----

    [Theory, ModeData]
    public void InstanceMethod_NonCapturingInnerFunction_IsReferenceable(ExecutionMode mode)
    {
        var source = """
            class C {
                m() {
                    function top() { return "top"; }
                    return top();
                }
            }
            console.log(new C().m());
            """;
        Assert.Equal("top\n", TestHarness.Run(source, mode));
    }

    // ---- Capturing a method-local const (was "Undefined variable", then a null capture pre-fix) ----

    [Theory, ModeData]
    public void InstanceMethod_CapturingInnerFunction_SeesMethodLocal(ExecutionMode mode)
    {
        var source = """
            class C {
                m() {
                    const k = "K";
                    function cap() { return "k=" + k; }
                    return cap();
                }
            }
            console.log(new C().m());
            """;
        Assert.Equal("k=K\n", TestHarness.Run(source, mode));
    }

    // ---- Capturing a method parameter (value-type double must be boxed into the closure field) ----

    [Theory, ModeData]
    public void InstanceMethod_InnerFunction_CapturesParameter(ExecutionMode mode)
    {
        var source = """
            class C {
                dbl(x: number) {
                    function inner() { return x * 2; }
                    return inner();
                }
            }
            console.log(new C().dbl(21));
            """;
        Assert.Equal("42\n", TestHarness.Run(source, mode));
    }

    // ---- Capturing a value-type method-local (boxing on the local fallback path) ----

    [Theory, ModeData]
    public void InstanceMethod_InnerFunction_CapturesNumericLocal(ExecutionMode mode)
    {
        var source = """
            class C {
                m() {
                    let n = 5;
                    function f() { return n + 1; }
                    return f();
                }
            }
            console.log(new C().m());
            """;
        Assert.Equal("6\n", TestHarness.Run(source, mode));
    }

    // ---- Self-recursion (own name resolved via direct dispatch, not a captured local) ----

    [Theory, ModeData]
    public void InstanceMethod_InnerFunction_SelfRecursion(ExecutionMode mode)
    {
        var source = """
            class C {
                fact() {
                    function fac(n: number): number { return n <= 1 ? 1 : n * fac(n - 1); }
                    return fac(5);
                }
            }
            console.log(new C().fact());
            """;
        Assert.Equal("120\n", TestHarness.Run(source, mode));
    }

    // ---- Two inner functions sharing a captured method-local ----

    [Theory, ModeData]
    public void InstanceMethod_TwoInnerFunctions_ShareCapture(ExecutionMode mode)
    {
        var source = """
            class C {
                m() {
                    const a = "A";
                    function f1() { return a; }
                    function f2() { return a + a; }
                    return f1() + f2();
                }
            }
            console.log(new C().m());
            """;
        Assert.Equal("AAA\n", TestHarness.Run(source, mode));
    }

    // ---- Capturing a module top-level variable from inside a method (routes via entry-point DC) ----

    [Theory, ModeData]
    public void InstanceMethod_InnerFunction_CapturesTopLevelVar(ExecutionMode mode)
    {
        var source = """
            const G = "top-level";
            class C {
                m() {
                    function g() { return G; }
                    return g();
                }
            }
            console.log(new C().m());
            """;
        Assert.Equal("top-level\n", TestHarness.Run(source, mode));
    }

    // ---- Block-nested inner function inside a method (composes with #1230 in-place path) ----

    [Theory, ModeData]
    public void Method_LoopBody_InnerFunction_CapturesLoopVariable_PerIteration(ExecutionMode mode)
    {
        var source = """
            class C {
                m() {
                    const out: string[] = [];
                    for (let i = 0; i < 3; i++) {
                        function make() { return i; }
                        out.push("" + make());
                    }
                    return out.join(",");
                }
            }
            console.log(new C().m());
            """;
        Assert.Equal("0,1,2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Method_IfBlock_InnerFunction_CapturesBlockConst(ExecutionMode mode)
    {
        var source = """
            class C {
                m() {
                    let acc = 0;
                    if (true) {
                        const step = 10;
                        function add() { return acc + step; }
                        acc = add();
                    }
                    return acc;
                }
            }
            console.log(new C().m());
            """;
        Assert.Equal("10\n", TestHarness.Run(source, mode));
    }

    // ---- Static method ----

    [Theory, ModeData]
    public void StaticMethod_InnerFunction_IsReferenceable(ExecutionMode mode)
    {
        var source = """
            class C {
                static s() {
                    const tag = "S";
                    function h() { return "static-" + tag; }
                    return h();
                }
            }
            console.log(C.s());
            """;
        Assert.Equal("static-S\n", TestHarness.Run(source, mode));
    }

    // ---- Private method (ES2022 #member) ----

    [Theory, ModeData]
    public void PrivateMethod_InnerFunction_IsReferenceable(ExecutionMode mode)
    {
        var source = """
            class C {
                #impl() {
                    const v = "priv";
                    function h() { return v + "!"; }
                    return h();
                }
                run() { return this.#impl(); }
            }
            console.log(new C().run());
            """;
        Assert.Equal("priv!\n", TestHarness.Run(source, mode));
    }
}

using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regression tests for #446: per ECMA-262 §10.2.2 [[Construct]], when a constructor body returns
/// an <i>Object</i>, that object becomes the result of <c>new</c>; a primitive return is ignored
/// and the freshly-constructed <c>this</c> is used. Functions are ordinary objects, so a returned
/// function/arrow must win.
///
/// <para>The interpreter's construct-return sites gated on
/// <c>result is SharpTSObject or SharpTSInstance or SharpTSArray …</c>, which omitted callables —
/// so a returned function fell through to <c>this</c> (and two of the sites even omitted
/// Map/Date/RegExp). They now share a single <c>IsConstructorReturnObject</c> helper that returns
/// the value for any non-primitive, matching tsc/node and the compiled <c>NewOnFunction</c> path.
/// Compiled mode was already correct, so every case asserts cross-mode parity.</para>
/// </summary>
public class ConstructorReturnValueTests
{
    // ---- The issue repro: a constructor returning an arrow yields the arrow, not `this` ----

    [Theory, ModeData]
    public void ConstructorReturningArrow_YieldsArrow(ExecutionMode mode)
    {
        var source = """
            function Make(this: any, v: string) {
                this.v = v;
                return () => "fn-result:" + v;
            }
            const f: any = new (Make as any)("X");
            console.log(typeof f);
            console.log(typeof f === "function" ? f() : "NOT CALLABLE");
            console.log(typeof f.v);
            """;
        // f is the returned arrow: it's a function and is callable (it is NOT the constructed
        // `this`, which would not be callable). Reading the missing `v` off the arrow is now a
        // clean cross-mode signal — `undefined` in both modes (functions are ordinary objects)
        // since #651 fixed compiled-mode missing-property reads off function/arrow values. If f
        // were the constructed `this`, `f.v` would be "X".
        Assert.Equal("function\nfn-result:X\nundefined\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void FunctionExpressionConstructorReturningArrow_YieldsArrow(ExecutionMode mode)
    {
        // The function-expression construct path (SharpTSArrowFunction with HasOwnThis).
        var source = """
            const Make: any = function (this: any, v: string) {
                this.v = v;
                return () => "expr:" + v;
            };
            const g: any = new Make("Y");
            console.log(typeof g);
            console.log(g());
            """;
        Assert.Equal("function\nexpr:Y\n", TestHarness.Run(source, mode));
    }

    // ---- Other object return types win too ----

    [Theory, ModeData]
    public void ConstructorReturningObject_YieldsObject(ExecutionMode mode)
    {
        var source = """
            function F(this: any) { this.a = 1; return { b: 2 }; }
            const o: any = new (F as any)();
            console.log(o.a, o.b);
            """;
        // The returned literal wins, so `a` (set on the discarded `this`) is undefined and `b` is 2.
        Assert.Equal("undefined 2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ConstructorReturningArray_YieldsArray(ExecutionMode mode)
    {
        var source = """
            function F(this: any) { this.a = 1; return [9, 8]; }
            const arr: any = new (F as any)();
            console.log(Array.isArray(arr), arr[0], arr[1]);
            """;
        Assert.Equal("true 9 8\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ConstructorReturningClassInstance_YieldsInstance(ExecutionMode mode)
    {
        var source = """
            class Other { tag = "other"; }
            function F(this: any) { this.a = 1; return new Other(); }
            const r: any = new (F as any)();
            console.log(r.tag, r.a);
            """;
        Assert.Equal("other undefined\n", TestHarness.Run(source, mode));
    }

    // ---- Primitive returns are ignored: `this` wins ----

    [Theory, ModeData]
    public void ConstructorReturningNumber_YieldsThis(ExecutionMode mode)
    {
        var source = """
            function F(this: any) { this.a = 1; return 42; }
            const r: any = new (F as any)();
            console.log(typeof r, r.a);
            """;
        Assert.Equal("object 1\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ConstructorReturningString_YieldsThis(ExecutionMode mode)
    {
        var source = """
            function F(this: any) { this.a = 1; return "hi"; }
            const r: any = new (F as any)();
            console.log(typeof r, r.a);
            """;
        Assert.Equal("object 1\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ConstructorReturningBoolean_YieldsThis(ExecutionMode mode)
    {
        var source = """
            function F(this: any) { this.a = 1; return true; }
            const r: any = new (F as any)();
            console.log(typeof r, r.a);
            """;
        Assert.Equal("object 1\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ConstructorReturningNull_YieldsThis(ExecutionMode mode)
    {
        var source = """
            function F(this: any) { this.a = 1; return null; }
            const r: any = new (F as any)();
            console.log(typeof r, r.a);
            """;
        Assert.Equal("object 1\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ConstructorFallingOffEnd_YieldsThis(ExecutionMode mode)
    {
        // No return at all — `this` wins. (Interacts with #603: the boxed Call now returns the
        // undefined sentinel off the end, which is still a primitive, so `this` is used.)
        var source = """
            function F(this: any) { this.a = 1; }
            const r: any = new (F as any)();
            console.log(typeof r, r.a);
            """;
        Assert.Equal("object 1\n", TestHarness.Run(source, mode));
    }
}

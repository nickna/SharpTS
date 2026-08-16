using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for invoking non-callable values (#260). Both modes must throw a guest
/// TypeError ("&lt;typeof&gt; is not a function") instead of silently evaluating to
/// null — the compiled-mode silent no-op masked dispatch regressions like #239.
/// </summary>
public class NonCallableInvocationTests
{
    [Theory, ModeData]
    public void NullCallee_ThrowsTypeError(ExecutionMode mode)
    {
        var source = """
            const f: any = null;
            try {
                f("x");
                console.log("no throw");
            } catch (e: any) {
                console.log(e instanceof TypeError, e.message);
            }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true object is not a function\n", output);
    }

    [Theory, ModeData]
    public void UndefinedAndMissingMethod_ThrowTypeError(ExecutionMode mode)
    {
        var source = """
            function check(label: string, fn: () => void) {
                try { fn(); console.log(label, "no throw"); }
                catch (e: any) { console.log(label, e instanceof TypeError, e.message); }
            }
            const u: any = undefined;
            check("u", () => u());
            const obj: any = {};
            check("missing", () => obj.nope());
            const withNull: any = { m: null };
            check("nullMember", () => withNull.m(1));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal(
            "u true undefined is not a function\n" +
            "missing true undefined is not a function\n" +
            "nullMember true object is not a function\n",
            output);
    }

    [Theory, ModeData]
    public void NonCallablePrimitive_ThrowsTypeError(ExecutionMode mode)
    {
        var source = """
            const n: any = 42;
            try { n(); console.log("no throw"); }
            catch (e: any) { console.log(e instanceof TypeError, e.message); }
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true number is not a function\n", output);
    }

    [Theory, ModeData]
    public void OptionalCall_OfNullish_ReturnsUndefined(ExecutionMode mode)
    {
        var source = """
            const g: any = null;
            console.log(g?.());
            const h: any = { m: undefined };
            console.log(h.m?.());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("undefined\nundefined\n", output);
    }

    [Theory, ModeData]
    public void OptionalChainMethodCall_ShortCircuitsWholeChain(ExecutionMode mode)
    {
        // ECMA-262 §13.3: a.b?.m(x) yields undefined when a.b is nullish — the
        // call must not be attempted. Compiled mode used to lean on the silent
        // non-callable no-op for this (surfaced by the yaml package's
        // tag.test?.test(value) once #260 made the fallback throw).
        var source = """
            const o: any = {};
            console.log(o.test?.test("x"));
            const arr: any = [{ a: 1 }, { test: /x/ }];
            console.log(arr.find((t: any) => t.test?.test("x"))?.test + "");
            const n: any = null;
            console.log(n?.m());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("undefined\n/x/\nundefined\n", output);
    }

    [Theory, ModeData]
    public void NonCallableMember_EvaluatesArgsBeforeCallabilityCheck(ExecutionMode mode)
    {
        // ECMA-262 §13.3.6.1: ArgumentListEvaluation (step 2) runs before the
        // IsCallable check (steps 3/4). `o.bar(se())` with undefined `o.bar` must
        // still evaluate `se()` before throwing. (test262 .../call/11.2.3-3_1)
        var source = """
            let n = 0;
            function se() { n++; return 1; }
            const o: any = {};
            try { o.bar(se()); } catch (e) {}
            console.log(n);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n", output);
    }

    [Theory, ModeData]
    public void MemberAccessOnUndefinedCallee_ThrowsBeforeArgs(ExecutionMode mode)
    {
        // ECMA-262 §13.3.2.1: evaluating the callee `o.bar.gar` calls
        // RequireObjectCoercible(o.bar). With `o.bar` undefined that throws during
        // callee evaluation, BEFORE the arguments — so `se()` never runs.
        // (test262 .../call/11.2.3-3_3; compiled mode formerly deferred the throw.)
        var source = """
            let n = 0;
            function se() { n++; return 1; }
            const o: any = {};
            try { o.bar.gar(se()); } catch (e) {}
            console.log(n);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }

    [Theory, ModeData]
    public void SloppyThisMethodCall_EvaluatesArgs(ExecutionMode mode)
    {
        // `this.bar(se())` in a function called without a receiver: sloppy-mode
        // `this` is the (coercible) global object, so accessing `.bar` does not
        // throw — `se()` runs, then the undefined `this.bar` fails the callability
        // check. Guards against the coercibility check over-firing on the
        // compiled null-`this` representation. (test262 .../call/11.2.3-3_8)
        var source = """
            let n = 0;
            function se() { n++; return 1; }
            function f() { (this as any).bar(se()); }
            try { f(); } catch (e) {}
            console.log(n);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n", output);
    }

    [Theory, ModeData]
    public void ForAwaitBreak_WithoutIteratorReturn_DoesNotThrow(ExecutionMode mode)
    {
        // iterator.return() is optional per the iterator protocol; the for-await
        // cleanup path must skip it (not throw) when absent.
        var source = """
            async function* gen() { yield 1; yield 2; }
            async function main() {
                for await (const v of gen()) {
                    console.log("got", v);
                    break;
                }
                console.log("done");
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("got 1\ndone\n", output);
    }
}

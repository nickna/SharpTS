using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regression tests for #451: a prefix/postfix increment or decrement whose operand
/// is a member access with an <c>await</c> in the receiver (or index) — e.g.
/// <c>(await foo()).n++</c>, <c>--(await foo())[i]</c>, <c>arr[await i()]++</c>.
///
/// In interpreter mode this threw a spurious "'await' can only be used inside async
/// functions." Plain reads and assignments through the same await-receiver already
/// worked; only <c>++</c>/<c>--</c> was broken, because <c>EvaluateIncrement</c> resolved
/// the receiver/index with the synchronous evaluator (which routes <c>await</c> to the
/// throwing <c>VisitAwait</c>). #451 adds an async-aware increment path.
///
/// Runs against both modes: compiled-mode member-access increment in async bodies
/// (including an <c>await</c> in the receiver/index) was fixed by #357 / PR #453, so these
/// double as an interpreter↔compiler parity check.
/// </summary>
public class AwaitReceiverIncrementTests
{
    [Theory, ModeData]
    public void PostfixIncrement_AwaitInGetReceiver_DoesNotThrow(ExecutionMode mode)
    {
        // The exact repro from issue #451.
        var source = """
            async function getObj() { return { n: 1 }; }
            async function main() {
                (await getObj()).n++;
                console.log("ok");
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("ok\n", output);
    }

    [Theory, ModeData]
    public void PostfixIncrement_AwaitInGetReceiver_MutatesAndReturnsOld(ExecutionMode mode)
    {
        var source = """
            const o = { n: 1 };
            async function get() { return o; }
            async function main() {
                const old = (await get()).n++;
                console.log(old, o.n);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1 2\n", output);
    }

    [Theory, ModeData]
    public void PrefixIncrement_AwaitInGetReceiver_ReturnsNew(ExecutionMode mode)
    {
        var source = """
            const o = { n: 1 };
            async function get() { return o; }
            async function main() {
                const val = ++(await get()).n;
                console.log(val, o.n);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2 2\n", output);
    }

    [Theory, ModeData]
    public void PrefixDecrement_AwaitInGetIndexReceiver(ExecutionMode mode)
    {
        var source = """
            const arr = [10, 20];
            async function get() { return arr; }
            async function main() {
                const val = --(await get())[0];
                console.log(val, arr[0]);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("9 9\n", output);
    }

    [Theory, ModeData]
    public void PostfixIncrement_AwaitInIndexExpression(ExecutionMode mode)
    {
        var source = """
            async function main() {
                const arr = [5, 6];
                arr[await Promise.resolve(0)]++;
                console.log(arr[0], arr[1]);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("6 6\n", output);
    }

    [Theory, ModeData]
    public void PostfixIncrement_AwaitInBothReceiverAndIndex(ExecutionMode mode)
    {
        var source = """
            const m = [[1, 2], [3, 4]];
            async function getMat() { return m; }
            async function getIdx() { return 1; }
            async function main() {
                const old = (await getMat())[await getIdx()][0]++;
                console.log(old, m[1][0]);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3 4\n", output);
    }

    [Theory, ModeData]
    public void PostfixIncrement_AwaitDeepInNestedReceiver(ExecutionMode mode)
    {
        var source = """
            const o = { a: { b: 5 } };
            async function get() { return o; }
            async function main() {
                const old = (await get()).a.b++;
                console.log(old, o.a.b);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("5 6\n", output);
    }

    [Theory, ModeData]
    public void Increment_AwaitInReceiver_UsableAsSubexpression(ExecutionMode mode)
    {
        var source = """
            const o = { n: 5 };
            async function get() { return o; }
            async function main() {
                const z = (await get()).n++ + 100;
                console.log(z, o.n);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("105 6\n", output);
    }

    [Theory, ModeData]
    public void ReadAndAssign_ThroughAwaitReceiver_StillWork(ExecutionMode mode)
    {
        // The issue notes these always worked; guard against the fix regressing them.
        // `o` is annotated so the property stays `number` (a bare `{ n: 1 }` literal is
        // mis-narrowed to the literal type `1` by the checker, rejecting `= 9`; that is an
        // unrelated conformance bug — #458 — not in scope here).
        var source = """
            const o: { n: number } = { n: 1 };
            async function get() { return o; }
            async function main() {
                const r = (await get()).n;
                (await get()).n = 9;
                console.log(r, o.n);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1 9\n", output);
    }

    [Theory, ModeData]
    public void PlainIncrements_StillWorkInsideAsyncFunction(ExecutionMode mode)
    {
        // Regression guard: variable / plain-receiver / plain-index increments
        // (no await in the operand) must still work after rewiring the async path.
        var source = """
            async function main() {
                let c = 0;
                c++;
                --c;
                c--;
                const p = { v: 7 };
                p.v++;
                const arr = [1];
                arr[0]--;
                console.log(c, p.v, arr[0]);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("-1 8 0\n", output);
    }

    [Theory, ModeData]
    public void AwaitInReceiver_InsideAsyncArrow(ExecutionMode mode)
    {
        var source = """
            const o = { n: 1 };
            async function get() { return o; }
            const f = async () => { (await get()).n++; };
            async function main() {
                await f();
                console.log(o.n);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n", output);
    }

    [Theory, ModeData]
    public void AwaitInReceiver_InsideAsyncGenerator(ExecutionMode mode)
    {
        var source = """
            const o = { n: 1 };
            async function get() { return o; }
            async function* g() { (await get()).n++; yield 99; }
            async function main() {
                const it = g();
                const r = await it.next();
                console.log(r.value, o.n);
            }
            main();
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("99 2\n", output);
    }
}

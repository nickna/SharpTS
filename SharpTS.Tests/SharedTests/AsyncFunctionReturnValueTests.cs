using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// An async (non-generator) function that completes without an explicit <c>return &lt;expr&gt;</c>
/// — a bare <c>return;</c> or falling off the end of the body — resolves its promise with
/// <c>undefined</c>, per ECMA-262, not <c>null</c> (#587). A genuine <c>return null;</c> must
/// still resolve with <c>null</c>. These run cross-mode (interpreter + compiled).
/// </summary>
public class AsyncFunctionReturnValueTests
{
    [Theory, ModeData]
    public void AsyncFunction_BareReturn_ResolvesUndefined(ExecutionMode mode)
    {
        var source = """
            async function f() { return; }
            async function main() {
                const v = await f();
                console.log(typeof v);
                console.log(v === undefined);
            }
            main();
            """;

        Assert.Equal("undefined\ntrue\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncFunction_OffEnd_ResolvesUndefined(ExecutionMode mode)
    {
        var source = """
            async function f() {}
            async function main() {
                const v = await f();
                console.log(typeof v);
                console.log(v === undefined);
            }
            main();
            """;

        Assert.Equal("undefined\ntrue\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncArrow_BareReturn_ResolvesUndefined(ExecutionMode mode)
    {
        var source = """
            const f = async () => { return; };
            async function main() {
                const v = await f();
                console.log(typeof v);
            }
            main();
            """;

        Assert.Equal("undefined\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncArrow_OffEnd_ResolvesUndefined(ExecutionMode mode)
    {
        var source = """
            const f = async () => {};
            async function main() {
                const v = await f();
                console.log(typeof v);
            }
            main();
            """;

        Assert.Equal("undefined\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncFunction_ReturnNull_ResolvesNull(ExecutionMode mode)
    {
        // Care-case: an explicit `return null;` must keep resolving with null, not undefined.
        // Null-ness is asserted via typeof/String/=== null (all correct cross-mode). We avoid
        // `v === undefined` here because the compiled await-unwrap mis-compares an awaited null
        // as `=== undefined` (pre-existing, tracked by #600) — orthogonal to this completion fix.
        var source = """
            async function f() { return null; }
            async function main() {
                const v = await f();
                console.log(typeof v);
                console.log(String(v));
                console.log(v === null);
            }
            main();
            """;

        Assert.Equal("object\nnull\ntrue\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncFunction_ReturnValue_Preserved(ExecutionMode mode)
    {
        var source = """
            async function f() { return 42; }
            async function main() {
                const v = await f();
                console.log(v);
            }
            main();
            """;

        Assert.Equal("42\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncMethod_OffEnd_ResolvesUndefined(ExecutionMode mode)
    {
        var source = """
            class C {
                async m() { return; }
                async mOff() {}
                static async s() {}
            }
            async function main() {
                const c = new C();
                console.log(typeof (await c.m()));
                console.log(typeof (await c.mOff()));
                console.log(typeof (await C.s()));
            }
            main();
            """;

        Assert.Equal("undefined\nundefined\nundefined\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncFunction_AwaitThenOffEnd_ResolvesUndefined(ExecutionMode mode)
    {
        // The implicit-completion value must be undefined even when the body suspends on an
        // await first: the fall-through after the resumed body still completes with undefined.
        var source = """
            async function delay(): Promise<number> { return 1; }
            async function f() { await delay(); }
            async function main() {
                const v = await f();
                console.log(typeof v);
            }
            main();
            """;

        Assert.Equal("undefined\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncFunction_BareReturnInTryWithAwaitFinally_ResolvesUndefined(ExecutionMode mode)
    {
        // A bare `return;` inside a try whose finally awaits routes through the pending-return
        // path; the deferred completion must still resolve with undefined.
        var source = """
            async function delay(): Promise<number> { return 1; }
            async function f() {
                try { return; } finally { await delay(); }
            }
            async function main() {
                const v = await f();
                console.log(typeof v);
            }
            main();
            """;

        Assert.Equal("undefined\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncFunction_AwaitOfUndefinedResolving_YieldsUndefined(ExecutionMode mode)
    {
        // Awaiting an async function that resolves with the implicit undefined must observe
        // undefined at the await site, confirming the sentinel propagates through await.
        var source = """
            async function f() {}
            async function main() {
                const v = await f();
                console.log(v === undefined ? "undefined" : "other");
            }
            main();
            """;

        Assert.Equal("undefined\n", TestHarness.Run(source, mode));
    }
}

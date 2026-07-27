using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// A SYNC arrow nested in an ASYNC ARROW that WRITES a variable captured from the async arrow's scope
/// must share storage with the arrow body (live capture), not snapshot it by value. Compiled mode used
/// to drop the write: async arrows never built a function display class, so a nested sync arrow's write
/// went to a by-value snapshot field (interpreted 6,6 vs compiled 6,5). The fix gives the async arrow its
/// own reference-type function DC on its state machine (instantiated once in the MoveNext prologue) that
/// the nested sync arrow shares via <c>$functionDC</c>. The interpreter has always been correct.
/// </summary>
public class AsyncArrowSyncWriteCaptureTests
{
    [Theory, ModeData]
    public void StandaloneAsyncArrow_SyncArrowWritesCapturedLocal(ExecutionMode mode)
    {
        var source = """
            const af = async (): Promise<string> => {
              let r = 5;
              const f = () => { r = r + 1; return r; };
              await Promise.resolve(0);
              return f() + "," + r;
            };
            async function main() { console.log(await af()); }
            main();
            """;

        Assert.Equal("6,6\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NestedAsyncArrow_SyncArrowWritesCapturedLocal(ExecutionMode mode)
    {
        // The async arrow is itself nested inside an async function — it still needs its OWN function DC
        // for its OWN local; the enclosing function's DC / outer relay is a separate concern.
        var source = """
            async function outer(): Promise<string> {
              const af = async (): Promise<string> => {
                let r = 5;
                const f = () => { r = r + 1; return r; };
                await Promise.resolve(0);
                return f() + "," + r;
              };
              return await af();
            }
            async function main() { console.log(await outer()); }
            main();
            """;

        Assert.Equal("6,6\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void StandaloneAsyncArrow_CompoundAssignCapturedLocal(ExecutionMode mode)
    {
        var source = """
            const af = async (): Promise<string> => {
              let r = 5;
              const f = () => { r += 1; r++; return r; };
              await Promise.resolve(0);
              return f() + "," + r;
            };
            async function main() { console.log(await af()); }
            main();
            """;

        Assert.Equal("7,7\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void StandaloneAsyncArrow_WriteCapturedBlockScopeShadow_DoesNotLeak(ExecutionMode mode)
    {
        // Combines this fix with #838: the written capture is a nested-block shadow of an outer binding;
        // the inner shadow's DC field must not collide with the outer same-named binding.
        var source = """
            const af = async (): Promise<string> => {
              const out: string[] = [];
              const r = 100;
              {
                let r = 5;
                const f = () => { r = r + 1; return r; };
                await Promise.resolve(0);
                out.push(String(f()));
                out.push(String(r));
              }
              out.push(String(r));
              return out.join(",");
            };
            async function main() { console.log(await af()); }
            main();
            """;

        Assert.Equal("6,6,100\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void StandaloneAsyncArrow_TwoArrowsWriteDistinctCapturedLocals(ExecutionMode mode)
    {
        var source = """
            const af = async (): Promise<string> => {
              let a = 1;
              let b = 10;
              const f = () => { a = a + 1; return a; };
              const g = () => { b = b + 1; return b; };
              await Promise.resolve(0);
              return f() + "," + g() + "," + a + "," + b;
            };
            async function main() { console.log(await af()); }
            main();
            """;

        Assert.Equal("2,11,2,11\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void StandaloneAsyncArrow_CapturedWriteSurvivesAcrossAwait(ExecutionMode mode)
    {
        // The mutation straddles an await — the DC lives on the (reference-held) state machine field, so
        // it persists across the suspension like a hoisted local.
        var source = """
            const af = async (): Promise<number> => {
              let n = 0;
              const inc = () => { n = n + 1; };
              inc();
              await Promise.resolve(0);
              inc();
              return n;
            };
            async function main() { console.log(await af()); }
            main();
            """;

        Assert.Equal("2\n", TestHarness.Run(source, mode));
    }
}

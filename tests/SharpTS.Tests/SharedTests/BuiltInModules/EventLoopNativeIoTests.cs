using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Event-loop liveness contract for in-flight native I/O: a program whose only pending work is
/// a slow built-in async operation must NOT be abandoned by the interpreter's "never-settling
/// promise" quiescence heuristic (WaitForPromise / RunEventLoop give up after 250ms without
/// visible work). The promise-API counterpart of the callback-API convention from #205.
///
/// Each native-async surface keeps the loop alive for the lifetime of its in-flight task by a
/// Ref/Unref pair (fs.promises via BuiltInAsyncMethod's <c>refsEventLoopWhileInFlight</c>, dns
/// via <c>RunRefed</c>, fetch and timers/promises via explicit Ref around the await). These
/// tests are the regression guards for the #320 audit: each exercises one surface as the sole
/// pending work with a latency decisively past the 250ms window, asserting only output
/// correctness (never a wall-clock window — cf. the #295 flake-class rules) so they cannot
/// themselves flake. Remove the corresponding Ref and the matching test fails 100% with empty
/// output.
///
/// Interpreted mode only: the quiescence give-up is interpreter-specific. Compiled mode keeps
/// continuations on the loop via the emitted $EventLoopSyncContext instead.
/// </summary>
public class EventLoopNativeIoTests
{
    /// <summary>
    /// Regression for the FsPromisesNamespace_Stat empty-output CI failure: on a slow runner
    /// (AV scan, cold disk) the first fs write exceeded the 250ms quiescence window, the top-level
    /// promise was abandoned, and the program exited silently. SHARPTS_TEST_FS_ASYNC_DELAY_MS
    /// reproduces that timing deterministically.
    /// </summary>
    [Fact]
    public void SlowFsPromisesIo_KeepsInterpreterEventLoopAlive()
    {
        // 600ms > 2x the 250ms quiescence window. Env var is process-wide, so concurrently
        // running fs tests also get the latency during this test's window — they still pass,
        // just slower; the value is kept as small as decisively-over-the-window allows.
        Environment.SetEnvironmentVariable("SHARPTS_TEST_FS_ASYNC_DELAY_MS", "600");
        try
        {
            var files = new Dictionary<string, string>
            {
                ["main.ts"] = """
                    import * as fs from 'fs';

                    async function test() {
                        await fs.promises.writeFile('test-eventloop-slowio.txt', 'content');
                        const stats = await fs.promises.stat('test-eventloop-slowio.txt');
                        await fs.promises.unlink('test-eventloop-slowio.txt');
                        console.log(stats.isFile());
                    }

                    test();
                """
            };

            var output = TestHarness.RunModules(files, "main.ts", ExecutionMode.Interpreted);
            Assert.Equal("true\n", output);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SHARPTS_TEST_FS_ASYNC_DELAY_MS", null);
        }
    }

    /// <summary>
    /// timers/promises setTimeout is the sole pending work. The 300ms delay (the promise's own
    /// argument — no env seam needed, the latency is intrinsic and deterministic) is past the
    /// 250ms window; without the Ref/Unref around Task.Delay in TimersPrimitiveInterpreter the
    /// top-level promise is abandoned and the program prints nothing.
    /// </summary>
    [Fact]
    public void SlowTimersPromise_KeepsInterpreterEventLoopAlive()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { setTimeout } from 'timers/promises';

                async function test() {
                    const value = await setTimeout(300, 'tick');
                    console.log(value);
                }

                test();
            """
        };

        var output = TestHarness.RunModules(files, "main.ts", ExecutionMode.Interpreted);
        Assert.Equal("tick\n", output);
    }

    /// <summary>
    /// An in-flight fetch is the sole pending work. The mock server sleeps 400ms (server-side,
    /// deterministic) before responding — past the 250ms window — so without the Ref/Unref around
    /// the request in FetchBuiltIns the top-level promise is abandoned mid-request and the body
    /// is never printed. Exercises the request leg (the slow leg); the subsequent res.text() reads
    /// an already-buffered body and completes instantly (HttpCompletionOption.ResponseContentRead).
    /// </summary>
    [Fact]
    public void SlowFetch_KeepsInterpreterEventLoopAlive()
    {
        using var server = new MockHttpServer();
        server.AddDelayRoute("/slow", 400, "slow-body");
        server.Start();

        var source = $$"""
            async function test() {
                const res = await fetch('{{server.BaseUrl}}slow');
                const text = await res.text();
                console.log(text);
            }

            test();
            """;

        var output = TestHarness.Run(source, ExecutionMode.Interpreted);
        Assert.Equal("slow-body\n", output);
    }
}

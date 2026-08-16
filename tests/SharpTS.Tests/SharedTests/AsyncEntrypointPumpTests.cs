using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Interpreter entry points that wait on a guest promise must pump the event
/// loop while waiting. Previously three sites blocked with a bare
/// <c>GetAwaiter().GetResult()</c> — async <c>main()</c>, a CommonJS module's
/// top-level promise-yielding expression, and <c>ReadableStream.tee()</c> over
/// a source with an async <c>pull()</c> — so a timer/IO continuation that could
/// only run on the blocked thread never ran and the program hard-hung (these
/// tests previously died on the harness timeout). Interpreted-only: the fixes
/// are in the interpreter's entry points; compiled mode has its own event-loop
/// bootstrap covered by existing shared tests.
/// </summary>
public class AsyncEntrypointPumpTests
{
    [Fact]
    public void SyncMain_ReturningTimerPromise_Completes()
    {
        // The main() auto-invoke convention covers a *sync* main returning a
        // promise chain (async main is deliberately not auto-invoked — the
        // `async function main() {...} main();` pattern would run twice).
        var output = TestHarness.RunInterpreted("""
            function main(): Promise<void> {
                return new Promise<void>(resolve => setTimeout(resolve, 10))
                    .then(() => { console.log("main-done"); });
            }
            """);
        Assert.Contains("main-done", output);
    }

    [Fact]
    public void CommonJs_TopLevelAsyncIife_AwaitingTimer_Completes()
    {
        var files = new Dictionary<string, string>
        {
            ["main.cjs"] = """
                (async () => {
                    await new Promise(resolve => setTimeout(resolve, 10));
                    console.log("cjs-done");
                })();
                """
        };
        var output = TestHarness.RunModules(files, "main.cjs", ExecutionMode.Interpreted);
        Assert.Contains("cjs-done", output);
    }

    [Fact]
    public void ReadableStreamTee_AsyncPull_DeliversToBothBranches()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                let n = 0;
                const rs = new ReadableStream({
                    async pull(c) {
                        await new Promise<void>(resolve => setTimeout(resolve, 1));
                        if (n < 3) c.enqueue(n++);
                        else c.close();
                    }
                });
                const branches = rs.tee();
                const r1 = branches[0].getReader();
                const r2 = branches[1].getReader();
                async function run() {
                    const a1 = await r1.read();
                    const a2 = await r1.read();
                    const a3 = await r1.read();
                    const a4 = await r1.read();
                    const b1 = await r2.read();
                    const b2 = await r2.read();
                    const b3 = await r2.read();
                    const b4 = await r2.read();
                    console.log(a1.value, a2.value, a3.value, a4.done);
                    console.log(b1.value, b2.value, b3.value, b4.done);
                }
                run();
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", ExecutionMode.Interpreted);
        Assert.Equal("0 1 2 true\n0 1 2 true\n", output);
    }

}

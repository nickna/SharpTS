using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regression tests for #205/#207: pending fs callbacks keep the event loop
/// alive, and async callbacks invoked by built-ins keep their scope chain
/// across await suspensions (the resumed frame must still see module-level
/// bindings).
/// </summary>
public class AsyncCallbackResumeTests
{
    [Theory, InterpretedOnlyData]
    public void FsReadFile_Callback_KeepsProcessAlive(ExecutionMode mode)
    {
        // #205: with no other pending work, the process must stay alive until
        // the readFile callback has fired.
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as fs from "fs";
                import * as os from "os";
                import * as path from "path";
                const p = path.join(os.tmpdir(), "sharpts-i205-" + process.pid + ".txt");
                fs.writeFileSync(p, "hello");
                fs.readFile(p, "utf8", (err: any, data: any) => {
                    console.log("callback fired:", data);
                    fs.unlinkSync(p);
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("callback fired: hello\n", output);
    }

    [Theory, ModeData]
    public void AsyncCallback_ResumesWithModuleScope_AfterAwait(ExecutionMode mode)
    {
        // #207: an async arrow passed to a built-in (server.listen) suspends at
        // its first await; the resumed continuation must still resolve
        // module-level bindings ('server') — previously the interpreter's
        // ambient environment had been restored to the outer scope by then and
        // the continuation died with "Undefined variable", leaking the server
        // handle and hanging the process.
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as http from "http";
                const server: any = http.createServer((req: any, res: any) => res.end("ok"));
                server.listen(0, async () => {
                    console.log("before await");
                    await new Promise((r: any) => setTimeout(r, 10));
                    console.log("after await");
                    server.close();
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("before await\nafter await\n", output);
    }

    [Theory, ModeData]
    public void AsyncCallback_FetchAfterAwait_InListenCallback(ExecutionMode mode)
    {
        // The full #207 repro: await fetch() against the just-started server,
        // then use module bindings after the await.
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as http from "http";
                const server: any = http.createServer((req: any, res: any) => res.end("ok"));
                server.listen(0, async () => {
                    const port = server.address().port;
                    const r: any = await fetch("http://127.0.0.1:" + port + "/");
                    console.log("status", r.status);
                    server.close();
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("status 200\n", output);
    }
}

using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for HTTP module and fetch API.
/// </summary>
public class HttpModuleTests : IDisposable
{
    private readonly MockHttpServer _server;

    public HttpModuleTests()
    {
        _server = new MockHttpServer();
        _server.AddStatusRoute("/status/200", 200, "OK");
        _server.AddJsonRoute("/json", new { message = "Hello", count = 42 });
        _server.AddTextRoute("/text", "Some text content");
        _server.AddEchoRoute("/post");
        _server.Start();
    }

    public void Dispose() => _server.Dispose();
    [Theory, ModeData]
    public void FetchIsGlobal(ExecutionMode mode)
    {
        // Test that fetch is available as a global
        var source = """
            console.log(typeof fetch);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("function\n", output);
    }

    [Theory, ModeData]
    public void FetchReturnsPromise(ExecutionMode mode)
    {
        // Test that fetch returns something with .then. Point at the local server
        // (like every sibling fetch test) rather than the real internet: a request
        // to an unreachable host keeps the event loop alive until the 30s timeout,
        // which false-reds CI on a flaky runner (#495).
        var source = $$"""
            const p = fetch('{{_server.BaseUrl}}status/200');
            console.log(typeof p.then);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("function\n", output);
    }

    [Theory, ModeData]
    public void HttpModuleImport(ExecutionMode mode)
    {
        // Test that http module can be imported
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as http from 'http';
                console.log(typeof http.createServer);
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("function\n", output);
    }

    [Theory, ModeData]
    public void HttpModuleExports(ExecutionMode mode)
    {
        // Test http module exports - use typeof instead of 'in' operator for compiler compatibility
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as http from 'http';
                console.log(typeof http.createServer !== 'undefined');
                console.log(typeof http.request !== 'undefined');
                console.log(typeof http.METHODS !== 'undefined');
                console.log(typeof http.STATUS_CODES !== 'undefined');
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void HttpCreateServer(ExecutionMode mode)
    {
        // Test creating a server (without starting it)
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as http from 'http';
                const server = http.createServer((req: any, res: any) => {
                    res.end('OK');
                });
                console.log(server.listening);
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("false\n", output);
    }

    [Theory, ModeData]
    public void HttpListenZero_BindsEphemeralPort(ExecutionMode mode)
    {
        // #214: HttpListener has no dynamic-port support, so listen(0) probes
        // a free port. server.address() must report the assigned port.
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as http from 'http';
                const server: any = http.createServer((req: any, res: any) => {
                    res.end('OK');
                });
                server.listen(0, () => {
                    const addr = server.address();
                    console.log(typeof addr.port === 'number' && addr.port > 0);
                    server.close();
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void HttpStatusCodes(ExecutionMode mode)
    {
        // Test STATUS_CODES constant
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as http from 'http';
                console.log(http.STATUS_CODES['200']);
                console.log(http.STATUS_CODES['404']);
                console.log(http.STATUS_CODES['500']);
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("OK\nNot Found\nInternal Server Error\n", output);
    }

    [Theory, ModeData]
    public void HttpMethods(ExecutionMode mode)
    {
        // Test METHODS constant
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as http from 'http';
                console.log(http.METHODS.includes('GET'));
                console.log(http.METHODS.includes('POST'));
                console.log(http.METHODS.includes('DELETE'));
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void HttpGlobalAgent(ExecutionMode mode)
    {
        // Test globalAgent object
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as http from 'http';
                console.log(typeof http.globalAgent);
                console.log(http.globalAgent.keepAlive);
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("object\ntrue\n", output);
    }

    [Theory, ModeData]
    public void FetchResponseProperties(ExecutionMode mode)
    {
        // Test Response properties
        var source = $$"""
            async function test(): Promise<void> {
                const res = await fetch('{{_server.BaseUrl}}status/200');
                console.log(res.ok);
                console.log(res.status);
            }
            test();
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n200\n", output);
    }

    [Theory, ModeData]
    public void FetchJsonMethod(ExecutionMode mode)
    {
        // Test Response.json() method
        var source = $$"""
            async function test(): Promise<void> {
                const res = await fetch('{{_server.BaseUrl}}json');
                const data = await res.json();
                console.log(typeof data);
            }
            test();
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("object\n", output);
    }

    [Theory, ModeData]
    public void FetchTextMethod(ExecutionMode mode)
    {
        // Test Response.text() method
        var source = $$"""
            async function test(): Promise<void> {
                const res = await fetch('{{_server.BaseUrl}}text');
                const text = await res.text();
                console.log(typeof text);
            }
            test();
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("string\n", output);
    }

    [Theory, ModeData]
    public void FetchWithPost(ExecutionMode mode)
    {
        // Test POST request with body
        var source = $$"""
            async function test(): Promise<void> {
                const res = await fetch('{{_server.BaseUrl}}post', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ test: 123 })
                });
                const data = await res.json();
                console.log(data.method);
                const parsed = JSON.parse(data.body);
                console.log(parsed.test);
            }
            test();
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("POST\n123\n", output);
    }

    [Theory, ModeData]
    public void GlobalThisHasFetch(ExecutionMode mode)
    {
        // Test that fetch is accessible via globalThis and is the same reference
        var source = """
            console.log(globalThis.fetch === fetch);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void GlobalThisHasFetch_TypeCheck(ExecutionMode mode)
    {
        // Test that fetch is accessible via globalThis
        var source = """
            console.log(typeof globalThis.fetch);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("function\n", output);
    }
}

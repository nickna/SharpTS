using System.Net;
using System.Net.Sockets;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for the HTTP server-management surface (#1045): closeAllConnections/
/// closeIdleConnections, the keepAliveTimeout/headersTimeout/requestTimeout/timeout/
/// maxHeadersCount/maxRequestsPerSocket config, setTimeout, and the 'connection' event.
/// </summary>
/// <remarks>
/// Default config reads + the lifecycle methods run in both modes (compiled exposes the
/// Node-default config via property getters). Mutating the config, the 'connection' event,
/// and setTimeout's listener wiring are interpreter-mode features (documented).
/// </remarks>
public class HttpServerLifecycleTests
{
    [Theory, ModeData]
    public void Server_DefaultConfig_IsReadable(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as http from 'http';
                const server: any = http.createServer((req: any, res: any) => res.end());
                console.log('keepAlive=' + server.keepAliveTimeout);
                console.log('headers=' + server.headersTimeout);
                console.log('request=' + server.requestTimeout);
                console.log('timeout=' + server.timeout);
                console.log('maxHeaders=' + server.maxHeadersCount);
                console.log('maxReq=' + server.maxRequestsPerSocket);
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("keepAlive=5000\nheaders=60000\nrequest=300000\ntimeout=0\nmaxHeaders=2000\nmaxReq=0\n", output);
    }

    [Theory, ModeData]
    public void Server_LifecycleMethods_AreCallable(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as http from 'http';
                const server: any = http.createServer((req: any, res: any) => res.end());
                console.log('closeAll=' + (typeof server.closeAllConnections));
                console.log('closeIdle=' + (typeof server.closeIdleConnections));
                console.log('setTimeout=' + (typeof server.setTimeout));
                server.closeIdleConnections();
                console.log('idle-ok');
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("closeAll=function", output);
        Assert.Contains("closeIdle=function", output);
        Assert.Contains("setTimeout=function", output);
        Assert.Contains("idle-ok", output);
    }

    [Theory, ModeData]
    public void Server_CloseAllConnections_StopsServer(ExecutionMode mode)
    {
        var port = TestPorts.GetAvailablePort();
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as http from 'http';
                const server: any = http.createServer((req: any, res: any) => res.end());
                server.listen({{port}}, () => {
                    console.log('listening=' + server.listening);
                    server.closeAllConnections();
                    console.log('done');
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("listening=true", output);
        Assert.Contains("done", output);
    }

    [Theory, InterpretedOnlyData]
    public void Server_Config_IsSettable(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as http from 'http';
                const server: any = http.createServer((req: any, res: any) => res.end());
                server.keepAliveTimeout = 1234;
                server.requestTimeout = 9999;
                server.maxHeadersCount = 50;
                console.log(server.keepAliveTimeout);
                console.log(server.requestTimeout);
                console.log(server.maxHeadersCount);
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("1234\n9999\n50\n", output);
    }

    [Theory, InterpretedOnlyData]
    public void Server_ConnectionEvent_Fires(ExecutionMode mode)
    {
        var port = TestPorts.GetAvailablePort();
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as http from 'http';
                const server: any = http.createServer((req: any, res: any) => { res.end('ok'); });
                server.on('connection', (socket: any) => {
                    console.log('connection event ' + (typeof socket));
                });
                server.on('request', () => { server.close(); });
                server.listen({{port}}, () => {
                    http.get('http://127.0.0.1:{{port}}/');
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("connection event object", output);
    }
}

using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
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
    [SkippableTheory, ModeData]
    public void Server_ListenHost_BindsAllInterfacesAndInvokesThirdArgumentCallback(ExecutionMode mode)
    {
        // HttpListener wildcard prefixes require a separately provisioned URL ACL
        // on Windows. Linux (including the production container) exercises this path.
        Skip.If(OperatingSystem.IsWindows(),
            "HttpListener wildcard prefixes require a provisioned Windows URL ACL");

        var interfaceAddress = NetworkInterface.GetAllNetworkInterfaces()
            .Where(networkInterface =>
                networkInterface.OperationalStatus == OperationalStatus.Up &&
                networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(networkInterface =>
                networkInterface.GetIPProperties().UnicastAddresses)
            .Select(unicastAddress => unicastAddress.Address)
            .FirstOrDefault(address =>
                address.AddressFamily == AddressFamily.InterNetwork &&
                !IPAddress.IsLoopback(address));
        Skip.If(interfaceAddress == null, "No non-loopback IPv4 interface is available");

        var port = TestPorts.GetAvailablePort();
        var clientTask = GetWithoutProxyWithRetryAsync(interfaceAddress, port);
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as http from 'http';
                const server = http.createServer((_req: any, res: any) => {
                    res.end('reachable');
                    server.close();
                });
                server.listen({{port}}, '0.0.0.0', () => {
                    const address = server.address();
                    console.log('callback');
                    console.log('address=' + address.address);
                    console.log('family=' + address.family);
                });
                """
        };

        var output = TestHarness.RunModules(
            files, "./main.ts", mode, timeout: TimeSpan.FromSeconds(15));
        var body = clientTask.GetAwaiter().GetResult();

        Assert.Contains("callback", output);
        Assert.Contains("address=0.0.0.0", output);
        Assert.Contains("family=IPv4", output);
        Assert.Equal("reachable", body);
    }

    private static async Task<string> GetWithoutProxyWithRetryAsync(
        IPAddress address, int port)
    {
        using var handler = new HttpClientHandler { UseProxy = false };
        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(1)
        };

        var uri = new Uri($"http://{address}:{port}/");
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        Exception? lastException = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                return await client.GetStringAsync(uri);
            }
            catch (Exception exception) when (
                exception is HttpRequestException or TaskCanceledException)
            {
                lastException = exception;
                await Task.Delay(25);
            }
        }

        throw new TimeoutException(
            $"The wildcard HTTP listener never became reachable at {uri}.",
            lastException);
    }

    [Theory, ModeData]
    public void Server_ListenHost_UsesThirdArgumentCallbackAndReportsAddress(ExecutionMode mode)
    {
        var port = TestPorts.GetAvailablePort();
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as http from 'http';
                const server = http.createServer((req: any, res: any) => res.end('ok'));
                server.listen({{port}}, '127.0.0.1', () => {
                    const address = server.address();
                    console.log('callback');
                    console.log('address=' + address.address);
                    console.log('family=' + address.family);
                    server.close();
                });
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("callback", output);
        Assert.Contains("address=127.0.0.1", output);
        Assert.Contains("family=IPv4", output);
    }

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
    public void Server_CloseAllConnections_DoesNotStopServer(ExecutionMode mode)
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
                    console.log('still-listening=' + server.listening);
                    server.close(() => console.log('done'));
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("listening=true", output);
        Assert.Contains("still-listening=true", output);
        Assert.Contains("done", output);
    }

    [Theory, ModeData]
    public void Server_CloseAllConnections_BeforeListen_IsANoOp(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as http from 'http';
                const server: any = http.createServer((_req: any, res: any) => res.end('ok'));
                server.on('close', () => console.log('unexpected-close'));
                setTimeout(() => console.log('timer-survived'), 10);
                server.closeAllConnections();
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("timer-survived", output);
        Assert.DoesNotContain("unexpected-close", output);
    }

    [Theory, ModeData]
    public void Server_CloseAllConnections_KeepsListenerOpen(ExecutionMode mode)
    {
        var port = TestPorts.GetAvailablePort();
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as http from 'http';
                const server: any = http.createServer((_req: any, res: any) => res.end('still-open'));
                server.listen({{port}}, async () => {
                    server.closeAllConnections();
                    const response = await fetch('http://127.0.0.1:{{port}}/');
                    console.log(await response.text());
                    server.close();
                });
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("still-open", output);
    }

    [Theory, ModeData]
    public void Server_CloseAllConnections_AbortsActiveResponseWithoutDoubleCompletion(ExecutionMode mode)
    {
        var port = TestPorts.GetAvailablePort();
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as http from 'http';
                let requests = 0;
                const server: any = http.createServer((_req: any, res: any) => {
                    requests++;
                    if (requests === 1) {
                        server.closeAllConnections();
                        return;
                    }
                    res.end('second-response');
                });
                server.listen({{port}}, async () => {
                    try {
                        const first = await fetch('http://127.0.0.1:{{port}}/first');
                        await first.text();
                    } catch {
                        console.log('first-aborted');
                    } finally {
                        // HttpClient may surface a peer abort either as a rejected fetch
                        // or as a successfully completed response with an empty body.
                        console.log('first-finished');
                    }
                    const response = await fetch('http://127.0.0.1:{{port}}/second');
                    console.log(await response.text());
                    server.close(() => console.log('closed'));
                });
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("first-finished", output);
        Assert.Contains("second-response", output);
        Assert.Contains("closed", output);
    }

    [Theory, ModeData]
    public void Server_Close_DrainsInFlightRequestBeforeCloseEventAndCallback(ExecutionMode mode)
    {
        var port = TestPorts.GetAvailablePort();
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as http from 'http';
                const server: any = http.createServer((req: any, res: any) => {
                    server.close(() => console.log('close-callback'));
                    req.on('data', () => {});
                    req.on('end', () => {
                        console.log('request-end');
                        res.end('drained');
                    });
                });
                server.on('close', () => console.log('close-event'));
                server.listen({{port}}, async () => {
                    const response = await fetch('http://127.0.0.1:{{port}}/', {
                        method: 'POST',
                        body: 'x'.repeat(50000)
                    });
                    console.log('client=' + await response.text());
                });
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("close-event", output);
        Assert.Contains("close-callback", output);
        Assert.True(output.IndexOf("request-end", StringComparison.Ordinal) <
                    output.IndexOf("close-event", StringComparison.Ordinal));
        Assert.True(output.IndexOf("request-end", StringComparison.Ordinal) <
                    output.IndexOf("close-callback", StringComparison.Ordinal));
        Assert.True(output.IndexOf("close-event", StringComparison.Ordinal) <
                    output.IndexOf("close-callback", StringComparison.Ordinal));
    }

    [Theory, ModeData]
    public void Server_CanRelistenFromCloseEventWithoutLosingCallback(ExecutionMode mode)
    {
        var port = TestPorts.GetAvailablePort();
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as http from 'http';
                let closeCount = 0;
                const server: any = http.createServer((_req: any, res: any) => res.end('second-cycle'));
                server.on('close', () => {
                    closeCount++;
                    console.log('close-event-' + closeCount);
                    if (closeCount === 1) {
                        server.listen({{port}}, async () => {
                            console.log('relistened');
                            const response = await fetch('http://127.0.0.1:{{port}}/');
                            console.log(await response.text());
                            server.close(() => console.log('close-callback-2'));
                        });
                    }
                });
                server.listen({{port}}, () => {
                    server.close(() => console.log('close-callback-1'));
                });
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("close-event-1", output);
        Assert.Contains("close-callback-1", output);
        Assert.Contains("relistened", output);
        Assert.Contains("second-cycle", output);
        Assert.Contains("close-event-2", output);
        Assert.Contains("close-callback-2", output);
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

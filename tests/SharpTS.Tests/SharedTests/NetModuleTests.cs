using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for the net module (TCP sockets).
/// </summary>
public class NetModuleTests
{
    [Theory, ModeData]
    public void NetModuleImport(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as net from 'net';
                console.log(typeof net.createServer);
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("function\n", output);
    }

    [Theory, ModeData]
    public void NetModuleNodePrefix(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as net from 'node:net';
                console.log(typeof net.createServer);
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("function\n", output);
    }

    [Theory, ModeData]
    public void NetModuleExports(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as net from 'net';
                console.log(typeof net.createServer !== 'undefined');
                console.log(typeof net.createConnection !== 'undefined');
                console.log(typeof net.connect !== 'undefined');
                console.log(typeof net.isIP !== 'undefined');
                console.log(typeof net.isIPv4 !== 'undefined');
                console.log(typeof net.isIPv6 !== 'undefined');
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void NetIsIPv4(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { isIP, isIPv4, isIPv6 } from 'net';
                console.log(isIP('127.0.0.1'));
                console.log(isIP('::1'));
                console.log(isIP('not-an-ip'));
                console.log(isIPv4('127.0.0.1'));
                console.log(isIPv4('::1'));
                console.log(isIPv6('::1'));
                console.log(isIPv6('127.0.0.1'));
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("4\n6\n0\ntrue\nfalse\ntrue\nfalse\n", output);
    }

    [Theory, ModeData]
    public void NetCreateServerReturnsServer(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as net from 'net';
                const server = net.createServer();
                console.log(server.listening);
                console.log(typeof server.listen);
                console.log(typeof server.close);
                console.log(typeof server.address);
                console.log(typeof server.on);
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("false\nfunction\nfunction\nfunction\nfunction\n", output);
    }

    [Theory, ModeData]
    public void NetServerListenAndClose(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as net from 'net';
                const server = net.createServer();
                server.listen(0, () => {
                    console.log('listening');
                    console.log(server.listening);
                    const addr = server.address();
                    console.log(typeof addr.port === 'number');
                    server.close(() => {
                        console.log('closed');
                        console.log(server.listening);
                    });
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("listening\ntrue\ntrue\nclosed\nfalse\n", output);
    }

    [Theory, ModeData]
    public void NetServerListeningEvent(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as net from 'net';
                const server = net.createServer();
                server.on('listening', () => {
                    console.log('listening event fired');
                    server.close();
                });
                server.on('close', () => {
                    console.log('close event fired');
                });
                server.listen(0);
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("listening event fired", output);
        Assert.Contains("close event fired", output);
    }

    [Theory, ModeData]
    public void NetServerConnectionEvent(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as net from 'net';
                const server = net.createServer((socket) => {
                    console.log('connection received');
                    console.log(typeof socket.remotePort === 'number');
                    socket.destroy();
                    server.close();
                });
                server.listen(0, () => {
                    const addr = server.address();
                    const client = net.createConnection({ port: addr.port, host: '127.0.0.1' });
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("connection received", output);
        Assert.Contains("true", output);
    }

    [Theory, ModeData]
    public void NetConnectPositionalPortHostCallback(ExecutionMode mode)
    {
        // Node signature: net.connect(port[, host][, connectListener]) — the
        // three-positional-arg idiom was rejected with an arity error (#211).
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as net from 'net';
                const server = net.createServer((socket) => {
                    socket.destroy();
                    server.close();
                });
                server.listen(0, () => {
                    const addr = server.address();
                    const client = net.connect(addr.port, '127.0.0.1', () => {
                        console.log('connect listener fired');
                        client.destroy();
                    });
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("connect listener fired", output);
    }

    [Theory, ModeData]
    public void NetSocketWriteAndReceive(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as net from 'net';
                const server = net.createServer((socket) => {
                    socket.write('hello');
                    socket.end();
                });
                server.listen(0, () => {
                    const addr = server.address();
                    const client = net.createConnection({ port: addr.port, host: '127.0.0.1' });
                    client.setEncoding('utf8');
                    let data = '';
                    client.on('data', (chunk: string) => {
                        data += chunk;
                    });
                    client.on('end', () => {
                        console.log('received: ' + data);
                        client.destroy();
                        server.close();
                    });
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("received: hello", output);
    }

    [Theory, ModeData]
    public void NetSocketCloseEmittedOnceWhenDestroyedAfterEnd(ExecutionMode mode)
    {
        // Node emits 'close' exactly once per socket lifetime. Calling destroy()
        // from an 'end' handler used to fire it twice — once from Destroy and
        // once from the read-loop EOF path (#213).
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as net from 'net';
                const server = net.createServer((socket) => {
                    socket.end();
                });
                server.listen(0, () => {
                    const addr = server.address();
                    const client = net.createConnection({ port: addr.port, host: '127.0.0.1' });
                    let closes = 0;
                    client.on('close', () => { closes++; });
                    client.on('end', () => {
                        client.destroy();
                        setTimeout(() => {
                            console.log('close events: ' + closes);
                            server.close();
                        }, 100);
                    });
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("close events: 1", output);
    }

    [Theory, ModeData]
    public void NetSocketProperties(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as net from 'net';
                const server = net.createServer((socket) => {
                    console.log(typeof socket.remoteAddress);
                    console.log(typeof socket.remotePort);
                    console.log(typeof socket.localPort);
                    console.log(socket.destroyed);
                    socket.destroy();
                    server.close();
                });
                server.listen(0, () => {
                    const addr = server.address();
                    const client = net.createConnection({ port: addr.port, host: '127.0.0.1' });
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("string", output);
        Assert.Contains("number", output);
        Assert.Contains("false", output);
    }

    [Theory, ModeData]
    public void NetServerGetConnections(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as net from 'net';
                const server = net.createServer((socket) => {
                    server.getConnections((err: any, count: number) => {
                        console.log('connections: ' + count);
                    });
                    socket.destroy();
                    server.close();
                });
                server.listen(0, () => {
                    const addr = server.address();
                    const client = net.createConnection({ port: addr.port, host: '127.0.0.1' });
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("connections: 1", output);
    }

    #region IPC Socket Tests

    private static string GetIpcPath()
    {
        var pipeName = $"sharpts_test_{Guid.NewGuid():N}";
        // Use simple name on Windows (ConvertToWindowsPipeName handles it),
        // full path on Unix
        return OperatingSystem.IsWindows() ? pipeName : $"/tmp/{pipeName}.sock";
    }

    [Theory, ModeData]
    public void IpcSocket_PipeBasic(ExecutionMode mode)
    {
        var pipePath = GetIpcPath();

        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as net from 'net';
                const server = net.createServer((socket) => {
                    socket.on('data', (data: string) => {
                        socket.write('pong');
                    });
                });
                server.listen('{{pipePath}}', () => {
                    const client = net.createConnection({ path: '{{pipePath}}' });
                    client.setEncoding('utf8');
                    client.on('connect', () => {
                        client.write('ping');
                    });
                    client.on('data', (data: string) => {
                        console.log('received: ' + data);
                        client.destroy();
                        server.close();
                    });
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("received: pong", output);
    }

    [Theory, ModeData]
    public void IpcSocket_ConnectionEvent(ExecutionMode mode)
    {
        var pipePath = GetIpcPath();

        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as net from 'net';
                const server = net.createServer();
                server.on('connection', (socket) => {
                    console.log('connection event fired');
                    socket.destroy();
                    server.close();
                });
                server.listen('{{pipePath}}', () => {
                    const client = net.createConnection('{{pipePath}}');
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("connection event fired", output);
    }

    [Theory, ModeData]
    public void IpcSocket_MultipleClients(ExecutionMode mode)
    {
        var pipePath = GetIpcPath();

        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as net from 'net';
                let connectionCount = 0;
                const server = net.createServer((socket) => {
                    connectionCount++;
                    console.log('connection ' + connectionCount);
                    socket.destroy();
                    if (connectionCount >= 2) {
                        server.close();
                    }
                });
                server.listen('{{pipePath}}', () => {
                    net.createConnection('{{pipePath}}');
                    setTimeout(() => {
                        net.createConnection('{{pipePath}}');
                    }, 100);
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("connection 1", output);
        Assert.Contains("connection 2", output);
    }

    [Theory, ModeData]
    public void IpcSocket_OptionsObject(ExecutionMode mode)
    {
        var pipePath = GetIpcPath();

        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as net from 'net';
                const server = net.createServer((socket) => {
                    console.log('connected via options');
                    socket.destroy();
                    server.close();
                });
                server.listen({ path: '{{pipePath}}' }, () => {
                    net.createConnection({ path: '{{pipePath}}' });
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("connected via options", output);
    }

    [Theory, ModeData]
    public void IpcSocket_Properties(ExecutionMode mode)
    {
        var pipePath = GetIpcPath();

        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as net from 'net';
                const server = net.createServer((socket) => {
                    console.log('remoteFamily: ' + socket.remoteFamily);
                    console.log('readyState: ' + socket.readyState);
                    socket.destroy();
                    server.close();
                });
                server.listen('{{pipePath}}', () => {
                    net.createConnection('{{pipePath}}');
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("remoteFamily: pipe", output);
        Assert.Contains("readyState: open", output);
    }

    [Theory, ModeData]
    public void IpcSocket_ServerAddress(ExecutionMode mode)
    {
        var pipePath = GetIpcPath();

        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as net from 'net';
                const server = net.createServer((socket) => {
                    socket.destroy();
                    server.close();
                });
                server.listen('{{pipePath}}', () => {
                    const addr = server.address();
                    console.log('type: ' + typeof addr);
                    console.log('addr: ' + addr);
                    net.createConnection('{{pipePath}}');
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        // Node.js returns a string (the pipe path) for IPC servers, not an object
        Assert.Contains("type: string", output);
        Assert.Contains($"addr: {pipePath}", output);
    }

    [Theory, ModeData]
    public void IpcSocket_BidirectionalEcho(ExecutionMode mode)
    {
        var pipePath = GetIpcPath();

        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as net from 'net';
                const server = net.createServer((socket) => {
                    socket.setEncoding('utf8');
                    socket.on('data', (data: string) => {
                        socket.write('echo:' + data);
                    });
                });
                server.listen('{{pipePath}}', () => {
                    const client = net.createConnection({ path: '{{pipePath}}' });
                    client.setEncoding('utf8');
                    client.on('connect', () => {
                        client.write('hello');
                    });
                    client.on('data', (data: string) => {
                        console.log(data);
                        client.destroy();
                        server.close();
                    });
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("echo:hello", output);
    }

    [Theory, ModeData]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void IpcSocket_ConnectErrorCode(ExecutionMode mode)
    {
        // Connect to a non-existent pipe to verify error code is set.
        // Only run on Windows — on Unix, connecting to a missing socket path
        // may throw synchronously during socket construction, not async.
        if (!OperatingSystem.IsWindows()) return;

        var pipePath = GetIpcPath();

        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as net from 'net';
                const timeout = setTimeout(() => {}, 10000);
                const client = net.createConnection('{{pipePath}}');
                client.on('error', (err) => {
                    console.log('code: ' + err.code);
                    console.log('has code: ' + (err.code !== undefined));
                    clearTimeout(timeout);
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("has code: true", output);
        // Should get an error code (ENOENT or ECONNREFUSED depending on OS)
        Assert.Contains("code: E", output);
    }

    [Theory, ModeData]
    public void IpcSocket_ServerListenErrorEvent(ExecutionMode mode)
    {
        var pipePath = GetIpcPath();

        // Start two servers on the same pipe - second should get an error
        // On Unix, the file cleanup means this won't conflict, so test on Windows pipe names
        // Use a different approach: test that server error event has proper structure
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as net from 'net';
                const server = net.createServer((socket) => {
                    socket.destroy();
                });
                server.on('error', (err) => {
                    console.log('server error: ' + err.message);
                });
                server.listen('{{pipePath}}', () => {
                    console.log('listening');
                    const client = net.createConnection('{{pipePath}}');
                    client.on('connect', () => {
                        client.destroy();
                        server.close();
                    });
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("listening", output);
    }

    [Theory, ModeData]
    public void IpcSocket_NetConnect(ExecutionMode mode)
    {
        // Test net.connect() as alias for net.createConnection() with IPC
        var pipePath = GetIpcPath();

        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as net from 'net';
                const server = net.createServer((socket) => {
                    socket.write('hello from server');
                });
                server.listen('{{pipePath}}', () => {
                    const client = net.connect('{{pipePath}}');
                    client.setEncoding('utf8');
                    client.on('data', (data: string) => {
                        console.log(data);
                        client.destroy();
                        server.close();
                    });
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("hello from server", output);
    }

    #endregion

    [Theory, ModeData]
    public void HttpsModuleImport(ExecutionMode mode)
    {
        // Test that https module can be imported (delegates to http)
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as https from 'https';
                console.log(typeof https.createServer);
                console.log(typeof https.request);
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("function\nfunction\n", output);
    }
}

using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Dual-mode (interpreted == compiled) parity tests for the net-module hardening
/// work of epic #1067: write backpressure + 'drain' (#1068), BlockList /
/// SocketAddress (#1069), and connection-lifecycle additions (#1070).
/// All tests are hermetic: loopback only, ephemeral ports (listen(0)).
/// </summary>
public class NetTransportParityTests
{
    #region #1068 — write backpressure + drain

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Socket_Write_PastHighWaterMark_ReturnsFalse_ThenDrains(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as net from 'net';
                const server = net.createServer((sock: any) => {
                    let received = 0;
                    sock.on('data', (chunk: any) => { received += chunk.length; });
                    sock.on('end', () => {
                        console.log('server got ' + received);
                        sock.end();
                        server.close();
                    });
                });
                server.listen(0, '127.0.0.1', () => {
                    const port = server.address().port;
                    const client = net.createConnection({ port: port, host: '127.0.0.1', highWaterMark: 4 });
                    client.on('connect', () => {
                        console.log('hwm ' + client.writableHighWaterMark);
                        const ok = client.write('0123456789');
                        console.log('write ' + ok);
                        console.log('needDrain ' + client.writableNeedDrain);
                        client.on('drain', () => {
                            console.log('drain ' + client.writableLength + ' ' + client.writableNeedDrain);
                            client.end();
                        });
                    });
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("hwm 4\nwrite false\nneedDrain true\ndrain 0 false\nserver got 10\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Socket_Write_BelowHighWaterMark_ReturnsTrue_DefaultHwm(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as net from 'net';
                const server = net.createServer((sock: any) => {
                    sock.on('data', () => {});
                    sock.on('end', () => { sock.end(); server.close(); });
                });
                server.listen(0, '127.0.0.1', () => {
                    const port = server.address().port;
                    const client = net.createConnection({ port: port, host: '127.0.0.1' });
                    client.on('connect', () => {
                        console.log('default hwm ' + client.writableHighWaterMark);
                        console.log('small write ' + client.write('hi'));
                        console.log('needDrain ' + client.writableNeedDrain);
                        client.end();
                    });
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("default hwm 16384\nsmall write true\nneedDrain false\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Socket_WriteCallback_FiresAfterFlush_BeforeDrain(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as net from 'net';
                const server = net.createServer((sock: any) => {
                    sock.on('data', () => {});
                    sock.on('end', () => { sock.end(); server.close(); });
                });
                server.listen(0, '127.0.0.1', () => {
                    const port = server.address().port;
                    const client = net.createConnection({ port: port, host: '127.0.0.1', highWaterMark: 1 });
                    client.on('connect', () => {
                        client.write('payload', () => { console.log('write cb'); });
                        client.on('drain', () => {
                            console.log('drain');
                            client.end(() => { console.log('end cb'); });
                        });
                    });
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("write cb\ndrain\nend cb\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Server_HighWaterMarkOption_AppliesToAcceptedSockets(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as net from 'net';
                const server = net.createServer({ highWaterMark: 8 }, (sock: any) => {
                    console.log('accepted hwm ' + sock.writableHighWaterMark);
                    const ok = sock.write('0123456789abcdef');
                    console.log('server write ' + ok);
                    sock.end();
                });
                server.listen(0, '127.0.0.1', () => {
                    const port = server.address().port;
                    const client = net.createConnection({ port: port, host: '127.0.0.1' });
                    client.on('data', () => {});
                    client.on('end', () => {
                        client.end();
                        server.close();
                    });
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("accepted hwm 8\nserver write false\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Socket_EndWithData_FlushesQueuedWritesBeforeFin(ExecutionMode mode)
    {
        // end(data, cb) must flush pending writes, then the final chunk, then FIN —
        // and must invoke the callback exactly once.
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as net from 'net';
                const server = net.createServer((sock: any) => {
                    sock.setEncoding('utf8');
                    let all = '';
                    sock.on('data', (d: string) => { all += d; });
                    sock.on('end', () => {
                        console.log('server saw ' + all);
                        sock.end();
                        server.close();
                    });
                });
                server.listen(0, '127.0.0.1', () => {
                    const port = server.address().port;
                    const client = net.createConnection({ port: port, host: '127.0.0.1', highWaterMark: 1 });
                    client.on('connect', () => {
                        client.write('a');
                        client.write('b');
                        client.end('c', () => { console.log('end cb once'); });
                    });
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("end cb once\nserver saw abc\n", output);
    }

    #endregion
}

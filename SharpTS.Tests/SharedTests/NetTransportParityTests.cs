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

    #region #1069 — net.BlockList + net.SocketAddress

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BlockList_AddressRangeSubnet_CheckAndRules(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as net from 'net';
                const bl = new net.BlockList();
                bl.addAddress('123.123.123.123');
                bl.addRange('10.0.0.1', '10.0.0.10');
                bl.addSubnet('8.8.8.0', 24);
                bl.addAddress('::1', 'ipv6');
                console.log(bl.check('123.123.123.123'));
                console.log(bl.check('123.123.123.124'));
                console.log(bl.check('10.0.0.5'));
                console.log(bl.check('10.0.0.11'));
                console.log(bl.check('8.8.8.255'));
                console.log(bl.check('8.8.9.0'));
                console.log(bl.check('::1', 'ipv6'));
                console.log(bl.check('::2', 'ipv6'));
                console.log(bl.check('::ffff:123.123.123.123', 'ipv6'));
                console.log(bl.check('not-an-ip'));
                console.log(bl.rules.length);
                console.log(bl.rules[0]);
                console.log(bl.rules[1]);
                console.log(bl.rules[2]);
                console.log(bl.rules[3]);
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal(
            "true\nfalse\ntrue\nfalse\ntrue\nfalse\ntrue\nfalse\ntrue\nfalse\n4\n" +
            "Address: IPv4 123.123.123.123\nRange: IPv4 10.0.0.1-10.0.0.10\nSubnet: IPv4 8.8.8.0/24\nAddress: IPv6 ::1\n",
            output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void SocketAddress_DefaultsAndOptions(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as net from 'net';
                const sa = new net.SocketAddress({ address: '1.2.3.4', port: 80 });
                console.log(sa.address + ' ' + sa.family + ' ' + sa.port + ' ' + sa.flowlabel);
                const sa6 = new net.SocketAddress({ family: 'ipv6' });
                console.log(sa6.address + ' ' + sa6.family);
                const def = new net.SocketAddress({});
                console.log(def.address + ' ' + def.family + ' ' + def.port);
                const bl = new net.BlockList();
                bl.addAddress(sa);
                console.log(bl.check('1.2.3.4'));
                console.log(bl.check(sa));
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("1.2.3.4 ipv4 80 0\n:: ipv6\n127.0.0.1 ipv4 0\ntrue\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BlockList_InvalidInputs_ThrowOrFalse(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as net from 'net';
                const bl = new net.BlockList();
                try { bl.addAddress('not-an-ip'); console.log('no throw'); } catch (e) { console.log('addAddress threw'); }
                try { bl.addRange('10.0.0.10', '10.0.0.1'); console.log('no throw'); } catch (e) { console.log('addRange threw'); }
                try { bl.addSubnet('10.0.0.0', 99); console.log('no throw'); } catch (e) { console.log('addSubnet threw'); }
                console.log(bl.check('10.0.0.5'));
                console.log(bl.rules.length);
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("addAddress threw\naddRange threw\naddSubnet threw\nfalse\n0\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Server_BlockList_RejectsBlockedPeerSilently(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as net from 'net';
                const bl = new net.BlockList();
                bl.addAddress('127.0.0.1');
                const server = net.createServer({ blockList: bl }, () => {
                    console.log('BLOCKED PEER ACCEPTED - BUG');
                });
                server.listen(0, '127.0.0.1', () => {
                    const port = server.address().port;
                    const client = net.createConnection({ port: port, host: '127.0.0.1' });
                    client.on('error', () => {});
                    client.on('close', () => {
                        console.log('blocked client closed');
                        server.close();
                    });
                    setTimeout(() => { client.destroy(); }, 500);
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("blocked client closed\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void BlockList_NamedImport_Constructible(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import { BlockList, SocketAddress } from 'net';
                const bl = new BlockList();
                bl.addAddress('9.9.9.9');
                console.log(bl.check('9.9.9.9'));
                const sa = new SocketAddress({ address: '5.6.7.8' });
                console.log(sa.address);
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("true\n5.6.7.8\n", output);
    }

    #endregion
}

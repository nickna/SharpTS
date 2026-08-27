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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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
            "Address: IPv6 ::1\nSubnet: IPv4 8.8.8.0/24\nRange: IPv4 10.0.0.1-10.0.0.10\nAddress: IPv4 123.123.123.123\n",
            output);
    }

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
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

    [Theory, ModeData]
    public void Server_AllowHalfOpen_KeepsWritableSideAfterFin(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as net from 'net';
                const server = net.createServer({ allowHalfOpen: true }, (sock: any) => {
                    sock.setEncoding('utf8');
                    let got = '';
                    sock.on('data', (d: string) => { got += d; });
                    sock.on('end', () => {
                        console.log('server end, readyState ' + sock.readyState);
                        sock.write('reply:' + got);
                        sock.end();
                    });
                });
                server.listen(0, '127.0.0.1', () => {
                    const port = server.address().port;
                    const client = net.createConnection({ port: port, host: '127.0.0.1' });
                    client.setEncoding('utf8');
                    console.log('pending ' + client.pending);
                    let recv = '';
                    client.on('connect', () => {
                        console.log('pending ' + client.pending);
                        client.write('hi');
                        client.end();
                        console.log('client readyState ' + client.readyState);
                    });
                    client.on('data', (d: string) => { recv += d; });
                    client.on('close', () => {
                        console.log('recv ' + recv);
                        server.close();
                    });
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal(
            "pending true\npending false\nclient readyState readOnly\nserver end, readyState writeOnly\nrecv reply:hi\n",
            output);
    }

    [Theory, ModeData]
    public void Server_Drop_FiresPastMaxConnections(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as net from 'net';
                const server = net.createServer((sock: any) => {
                    sock.on('data', () => {});
                    sock.on('end', () => { sock.end(); });
                });
                server.maxConnections = 1;
                server.on('drop', (data: any) => {
                    console.log('drop ' + (typeof data.remoteAddress) + ' ' + (typeof data.remotePort) + ' ' + data.localFamily);
                });
                server.listen(0, '127.0.0.1', () => {
                    const port = server.address().port;
                    const c1 = net.createConnection({ port: port, host: '127.0.0.1' });
                    c1.on('error', () => {});
                    c1.on('connect', () => {
                        const c2 = net.createConnection({ port: port, host: '127.0.0.1' });
                        c2.on('error', () => {});
                        c2.on('close', () => {
                            console.log('c2 closed');
                            c1.end();
                        });
                        setTimeout(() => { c2.destroy(); }, 500);
                    });
                    c1.on('close', () => { server.close(); });
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("drop string number IPv4\nc2 closed\n", output);
    }

    [Theory, ModeData]
    public void Net_AutoSelectFamilyDefaults_GetAndSet(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as net from 'net';
                console.log(net.getDefaultAutoSelectFamily());
                console.log(net.getDefaultAutoSelectFamilyAttemptTimeout());
                net.setDefaultAutoSelectFamily(false);
                net.setDefaultAutoSelectFamilyAttemptTimeout(500);
                console.log(net.getDefaultAutoSelectFamily());
                console.log(net.getDefaultAutoSelectFamilyAttemptTimeout());
                net.setDefaultAutoSelectFamily(true);
                net.setDefaultAutoSelectFamilyAttemptTimeout(250);
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("true\n250\nfalse\n500\n", output);
    }

    [Theory, ModeData]
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

    [Theory, ModeData]
    public void NetFacade_StrictIpParsing_AndConnectIdentity(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as net from 'net';
                const values = [
                    '127.0.0.1', '127.00.0.1', '127.1', '0x7f000001', '2130706433',
                    '::1', '1::2::3', '::ffff:192.0.2.128',
                    '::ffff:192.168.001.1', 'fe80::1%eth0', 'fe80::1%', '[::1]'
                ];
                console.log(values.map((value: string) => net.isIP(value)).join(','));
                console.log(net.isIPv4('255.255.255.255'));
                console.log(net.isIPv6('2001:db8::1'));
                console.log(net.connect === net.createConnection);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("4,0,0,0,0,6,0,6,0,6,0,0\ntrue\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void NetFacade_CallableConstructors_AndSocketAddressValidation(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as net from 'net';
                const serverA = net.Server();
                const serverB = new net.Server();
                const socketA = net.Socket();
                const socketB = new net.Socket({ allowHalfOpen: true, highWaterMark: 1234 });
                console.log(typeof serverA.listen + ' ' + typeof serverB.listen);
                console.log(typeof socketA.connect + ' ' + socketB.allowHalfOpen + ' ' + socketB.writableHighWaterMark);
                try { net.createServer(123 as any); } catch (e) { console.log((e as any).code); }

                const sa = new net.SocketAddress({
                    family: 'IPv6', address: '2001:0db8:0:0:0:ff00:0042:8329',
                    port: '80', flowlabel: 7
                });
                console.log(sa.address + ' ' + sa.family + ' ' + sa.port + ' ' + sa.flowlabel);
                try { new net.SocketAddress({ address: '127.1' }); } catch (e) { console.log((e as any).code); }
                try { new net.SocketAddress({ port: 1.5 }); } catch (e) { console.log((e as any).code); }
                try { new net.SocketAddress({ family: 'ipv6', flowlabel: '3' }); } catch (e) { console.log((e as any).code); }
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal(
            "function function\nfunction true 1234\nERR_INVALID_ARG_TYPE\n2001:db8::ff00:42:8329 ipv6 80 7\n" +
            "ERR_INVALID_ADDRESS\nERR_SOCKET_BAD_PORT\nERR_INVALID_ARG_TYPE\n",
            output);
    }

    [Theory, ModeData]
    public void NetFacade_BlockListOrderingFamilyAndErrorCodes(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as net from 'net';
                const list = new net.BlockList();
                list.addAddress('127.0.0.1', 'IPv4');
                list.addSubnet('2001:db8::', 32, 'IPV6');
                console.log(list.rules.join('|'));
                console.log(list.check('127.0.0.1', 'IPv4'));
                console.log(list.check('bad-address'));
                try { list.addAddress('127.1'); } catch (e) { console.log((e as any).code); }
                try { list.addSubnet('127.0.0.1', 24.5); } catch (e) { console.log((e as any).code); }
                try { net.setDefaultAutoSelectFamily('yes' as any); } catch (e) { console.log((e as any).code); }
                try { net.setDefaultAutoSelectFamilyAttemptTimeout(0); } catch (e) { console.log((e as any).code); }
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal(
            "Subnet: IPv6 2001:db8::/32|Address: IPv4 127.0.0.1\ntrue\nfalse\n" +
            "ERR_INVALID_ADDRESS\nERR_OUT_OF_RANGE\nERR_INVALID_ARG_TYPE\nERR_OUT_OF_RANGE\n",
            output);
    }

    #endregion
}

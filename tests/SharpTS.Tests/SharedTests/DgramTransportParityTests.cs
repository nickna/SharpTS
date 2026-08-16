using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Dual-mode (interpreted == compiled) parity tests for the dgram-module
/// hardening work of epic #1067 (#1071): multicast completeness and
/// connected-socket send validation with structured error codes.
/// Hermetic: loopback only, ephemeral ports; multicast joins use loopback-scoped
/// groups and are wrapped in try/catch so platform refusals stay parity-equal.
/// </summary>
public class DgramTransportParityTests
{
    [Theory, ModeData]
    public void Dgram_MulticastControls_LoopbackInterfaceMembership(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as dgram from 'dgram';
                const sock = dgram.createSocket('udp4');
                sock.bind(0, '127.0.0.1', () => {
                    try { sock.setMulticastLoopback(true); console.log('loopback on'); } catch (e: any) { console.log('loopback threw'); }
                    try { sock.setMulticastLoopback(false); console.log('loopback off'); } catch (e: any) { console.log('loopback off threw'); }
                    try { sock.setMulticastInterface('127.0.0.1'); console.log('iface set'); } catch (e: any) { console.log('iface threw'); }
                    try {
                        sock.addMembership('224.0.0.114', '127.0.0.1');
                        console.log('joined');
                        // The drop must name the same interface as the join:
                        // Linux matches IP_DROP_MEMBERSHIP by (group, interface)
                        // and rejects an INADDR_ANY drop of a scoped join.
                        sock.dropMembership('224.0.0.114', '127.0.0.1');
                        console.log('dropped');
                    } catch (e: any) { console.log('membership threw'); }
                    sock.close();
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("loopback on\nloopback off\niface set\njoined\ndropped\n", output);
    }

    [Theory, ModeData]
    public void Dgram_SourceSpecificMembership_JoinAndLeave(ExecutionMode mode)
    {
        // SSM support varies by platform — assert only that both modes agree
        // (join+drop both succeed or both throw identically).
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as dgram from 'dgram';
                const sock = dgram.createSocket('udp4');
                sock.bind(0, '127.0.0.1', () => {
                    try {
                        sock.addSourceSpecificMembership('127.0.0.1', '232.0.0.114');
                        sock.dropSourceSpecificMembership('127.0.0.1', '232.0.0.114');
                        console.log('ssm round-trip ok');
                    } catch (e: any) {
                        console.log('ssm unsupported');
                    }
                    const s6 = dgram.createSocket('udp6');
                    try {
                        s6.addSourceSpecificMembership('::1', 'ff02::114');
                        console.log('udp6 ssm ok');
                    } catch (e: any) {
                        console.log('udp6 ssm rejected');
                    }
                    s6.close();
                    sock.close();
                });
                """
        };
        var interpreted = TestHarness.RunModules(files, "./main.ts", ExecutionMode.Interpreted);
        var actual = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal(interpreted, actual);
        Assert.Contains("udp6 ssm rejected", actual);
    }

    [Theory, ModeData]
    public void Dgram_ConnectedSend_ValidatesDestination(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as dgram from 'dgram';
                const target = dgram.createSocket('udp4');
                target.bind(0, '127.0.0.1', () => {
                    const tport = target.address().port;
                    const c = dgram.createSocket('udp4');
                    c.connect(tport, '127.0.0.1', () => {
                        try { c.send('x', tport, '127.0.0.1'); console.log('no throw'); }
                        catch (e: any) { console.log('send+port ' + e.code); }
                        try { c.send('ok'); console.log('plain send ok'); }
                        catch (e: any) { console.log('plain send threw'); }
                        try { c.disconnect(); console.log('disconnected'); }
                        catch (e: any) { console.log('disconnect threw'); }
                        try { c.disconnect(); console.log('no throw'); }
                        catch (e: any) { console.log('re-disconnect ' + e.code); }
                        try { c.send('y'); console.log('no throw'); }
                        catch (e: any) { console.log('portless ' + e.code); }
                        c.close();
                        target.close();
                    });
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal(
            "send+port ERR_SOCKET_DGRAM_IS_CONNECTED\nplain send ok\ndisconnected\n" +
            "re-disconnect ERR_SOCKET_DGRAM_NOT_CONNECTED\nportless ERR_SOCKET_DGRAM_NOT_CONNECTED\n",
            output);
    }
}

using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Parity tests for the dns-module hardening work of epic #1067 (#1072):
/// Resolver.cancel(), Resolver.setLocalAddress(), and
/// set/getDefaultResultOrder. Hermetic — no live DNS: lookups use 'localhost',
/// and cancel() targets a never-responding loopback blackhole created in-script.
/// </summary>
public class DnsTransportParityTests
{
    [Theory, ModeData]
    public void Dns_DefaultResultOrder_GetSetAndValidation(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as dns from 'dns';
                console.log(dns.getDefaultResultOrder());
                dns.setDefaultResultOrder('ipv6first');
                console.log(dns.getDefaultResultOrder());
                try { dns.setDefaultResultOrder('bogus'); console.log('no throw'); }
                catch (e: any) { console.log('bogus rejected'); }
                console.log(dns.getDefaultResultOrder());
                dns.setDefaultResultOrder('verbatim');
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("verbatim\nipv6first\nbogus rejected\nipv6first\n", output);
    }

    [Theory, ModeData]
    public void Dns_ResultOrder_AffectsLookupFamily(ExecutionMode mode)
    {
        // 'localhost' resolves to 127.0.0.1 everywhere; with ipv4first the lookup
        // must pick the IPv4 address regardless of resolver ordering.
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as dns from 'dns';
                dns.setDefaultResultOrder('ipv4first');
                const r = dns.lookup('localhost');
                console.log('family ' + r.family);
                dns.setDefaultResultOrder('verbatim');
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("family 4\n", output);
    }

    [Theory, ModeData]
    public void Dns_Resolver_SetLocalAddress_ValidatesAndAccepts(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as dns from 'dns';
                const r = new dns.Resolver();
                r.setLocalAddress('127.0.0.1');
                console.log('v4 ok');
                r.setLocalAddress('127.0.0.1', '::1');
                console.log('v4+v6 ok');
                try { r.setLocalAddress('not-an-ip'); console.log('no throw'); }
                catch (e: any) { console.log('rejected ' + (('' + e.message).indexOf('Invalid IP address') >= 0)); }
                console.log(typeof r.cancel);
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("v4 ok\nv4+v6 ok\nrejected true\nfunction\n", output);
    }

    [Theory, ModeData]
    public void Dns_Resolver_Cancel_RejectsPendingResolve(ExecutionMode mode)
    {
        // The resolver targets an in-script UDP blackhole that never answers;
        // cancel() must reject promptly with ECANCELLED instead of waiting out
        // the DNS timeout in either execution mode.
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as dgram from 'dgram';
                import * as dns from 'dns';
                const blackhole = dgram.createSocket('udp4');
                blackhole.bind(0, '127.0.0.1', () => {
                    const port = blackhole.address().port;
                    const r = new dns.Resolver();
                    r.setServers(['127.0.0.1:' + port]);
                    r.resolve4('cancel.test', (err: any, res: any) => {
                        console.log('err ' + (err ? err.code : 'none'));
                        blackhole.close();
                    });
                    r.cancel();
                });
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("err ECANCELLED\n", output);
    }

    [Theory, ModeData]
    public void Dns_Resolver_CancelledResolverStillUsable(ExecutionMode mode)
    {
        // Node semantics: cancel() aborts *outstanding* queries; the resolver
        // itself remains usable for new ones.
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as dgram from 'dgram';
                import * as dns from 'dns';
                const blackhole = dgram.createSocket('udp4');
                blackhole.bind(0, '127.0.0.1', () => {
                    const port = blackhole.address().port;
                    const r = new dns.Resolver();
                    r.setServers(['127.0.0.1:' + port]);
                    r.resolve4('first.test', (err: any) => {
                        console.log('first ' + (err ? err.code : 'none'));
                        // A post-cancel query uses a fresh token: cancel the
                        // second query separately to prove it was re-armed.
                        r.resolve4('second.test', (err2: any) => {
                            console.log('second ' + (err2 ? err2.code : 'none'));
                            blackhole.close();
                        });
                        r.cancel();
                    });
                    r.cancel();
                });
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("first ECANCELLED\nsecond ECANCELLED\n", output);
    }
}

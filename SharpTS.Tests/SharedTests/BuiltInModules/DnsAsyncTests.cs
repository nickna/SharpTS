using SharpTS.Runtime.BuiltIns.Modules;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Tests for async DNS resolution: resolve, resolve4, resolve6, reverse.
/// <para>
/// resolve4/resolve6 use the DNS wire protocol (Node/c-ares semantics — they query the
/// configured DNS server, not the hosts file), so they run against a loopback
/// <see cref="FakeDnsServer"/> via SHARPTS_DNS_SERVER for deterministic, hermetic
/// results in both runtimes (no live network — see #495). dns.reverse stays on the OS
/// resolver (getaddrinfo) and uses 127.0.0.1, which is hosts-file resolved.
/// </para>
/// </summary>
[Collection(DnsFakeServerEnvCollection.Name)]
public class DnsAsyncTests
{
    /// <summary>Fake server answering A=127.0.0.1 / AAAA=::1 for any name (NXDOMAIN otherwise).</summary>
    private static FakeDnsServer CreateAddressServer() => new((request, _) =>
    {
        var qtype = DnsPackets.QueryType(request);
        byte[]? rdata = qtype switch
        {
            DnsWireProtocol.TypeA => DnsPackets.A("127.0.0.1"),
            DnsWireProtocol.TypeAAAA => DnsPackets.Aaaa("::1"),
            _ => null
        };
        return rdata is null
            ? DnsPackets.Response(request, 3) // NXDOMAIN
            : DnsPackets.Response(request, 0, DnsPackets.Record(qtype, rdata));
    });

    /// <summary>Runs main.ts with SHARPTS_DNS_SERVER pointed at the fake server.</summary>
    private static string RunWithFakeDns(FakeDnsServer server, string source, ExecutionMode mode)
    {
        Environment.SetEnvironmentVariable("SHARPTS_DNS_SERVER", server.Address);
        try
        {
            return TestHarness.RunModules(
                new Dictionary<string, string> { ["main.ts"] = source }, "main.ts", mode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SHARPTS_DNS_SERVER", null);
        }
    }

    [Theory, ModeData]
    public void Resolve4_FakeServer(ExecutionMode mode)
    {
        using var server = CreateAddressServer();
        var output = RunWithFakeDns(server, """
            import * as dns from 'dns';
            dns.resolve4('fake.test', (err: any, addresses: any) => {
                console.log(err === null);
                console.log(Array.isArray(addresses));
                console.log(addresses[0]);
            });
            """, mode);

        Assert.Equal("true\ntrue\n127.0.0.1\n", output);
    }

    [Theory, ModeData]
    public void Resolve_DefaultRrtypeA_FakeServer(ExecutionMode mode)
    {
        using var server = CreateAddressServer();
        var output = RunWithFakeDns(server, """
            import * as dns from 'dns';
            dns.resolve('fake.test', (err: any, addresses: any) => {
                console.log(err === null);
                console.log(Array.isArray(addresses));
                console.log(addresses[0]);
            });
            """, mode);

        Assert.Equal("true\ntrue\n127.0.0.1\n", output);
    }

    [Theory, ModeData]
    public void Resolve4_NestedInsideCallback_CallbackFires(ExecutionMode mode)
    {
        // #239: in compiled mode, dns.* calls inside another callback (or any
        // function body) resolved the module member dynamically to null and
        // silently dropped the callback.
        using var server = CreateAddressServer();
        var output = RunWithFakeDns(server, """
            import * as dns from 'dns';
            dns.resolve4('fake.test', (err: any, a: any) => {
                console.log('outer ' + (err === null));
                dns.resolve4('fake.test', (err2: any, b: any) => {
                    console.log('inner ' + (err2 === null));
                });
            });
            """, mode);

        Assert.Equal("outer true\ninner true\n", output);
    }

    [Theory, ModeData]
    public void Resolve4_InsideTimerCallback_CallbackFires(ExecutionMode mode)
    {
        using var server = CreateAddressServer();
        var output = RunWithFakeDns(server, """
            import * as dns from 'dns';
            setTimeout(() => {
                dns.resolve4('fake.test', (err: any, a: any) => {
                    console.log('dns-in-timer ' + (err === null));
                });
            }, 20);
            """, mode);

        Assert.Equal("dns-in-timer true\n", output);
    }

    [Theory, ModeData]
    public void Resolve4_InsideFunctionBody_CallbackFires(ExecutionMode mode)
    {
        using var server = CreateAddressServer();
        var output = RunWithFakeDns(server, """
            import * as dns from 'dns';
            function go() {
                dns.resolve4('fake.test', (err: any, a: any) => {
                    console.log('fn ' + (err === null));
                });
            }
            go();
            """, mode);

        Assert.Equal("fn true\n", output);
    }

    [Theory, ModeData]
    public void Reverse_Loopback(ExecutionMode mode)
    {
        // dns.reverse uses the OS resolver (getaddrinfo); 127.0.0.1 reverse-resolves
        // from the hosts file — no live network, no fake-server redirect needed.
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as dns from 'dns';
                dns.reverse('127.0.0.1', (err: any, hostnames: any) => {
                    console.log(err === null);
                    console.log(Array.isArray(hostnames));
                    console.log(hostnames.length > 0);
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void DnsConstants_Defined(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as dns from 'dns';
                console.log(dns.NOTFOUND === 'ENOTFOUND');
                console.log(dns.NODATA === 'ENODATA');
                console.log(dns.SERVFAIL === 'ESERVFAIL');
                console.log(dns.CONNREFUSED === 'ECONNREFUSED');
                console.log(dns.TIMEOUT === 'ETIMEOUT');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void DnsPromises_Resolve4_FakeServer(ExecutionMode mode)
    {
        using var server = CreateAddressServer();
        var output = RunWithFakeDns(server, """
            import dns from 'dns/promises';
            async function main() {
                const addresses = await dns.resolve4('fake.test');
                console.log(Array.isArray(addresses));
                console.log(addresses[0]);
            }
            main();
            """, mode);

        Assert.Equal("true\n127.0.0.1\n", output);
    }

    [Theory, ModeData]
    public void DnsPromises_Lookup(ExecutionMode mode)
    {
        // dns.lookup uses the OS resolver (hosts file) rather than a live DNS query, so
        // 'localhost' resolves deterministically — no fake-server redirect needed.
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import dns from 'dns/promises';
                async function main() {
                    const result = await dns.lookup('localhost');
                    console.log(typeof result.address === 'string');
                    console.log(result.family === 4 || result.family === 6);
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void DnsPromises_ViaModule_FakeServer(ExecutionMode mode)
    {
        using var server = CreateAddressServer();
        var output = RunWithFakeDns(server, """
            import * as dns from 'dns';
            async function main() {
                const addresses = await dns.promises.resolve4('fake.test');
                console.log(Array.isArray(addresses));
                console.log(addresses[0]);
            }
            main();
            """, mode);

        Assert.Equal("true\n127.0.0.1\n", output);
    }
}

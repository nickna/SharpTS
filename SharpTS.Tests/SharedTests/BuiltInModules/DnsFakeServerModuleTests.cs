using SharpTS.Runtime.BuiltIns.Modules;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Behavioral DNS tests against a loopback fake server, in both interpreted and
/// compiled modes. SHARPTS_DNS_SERVER redirects the "system DNS server" of both
/// runtimes at the fake, so in compiled mode these exercise the emitted IL wire
/// protocol (RuntimeEmitter.Dns.cs: DnsSendReceive, DnsSendViaTcp, DnsParseResponse,
/// DnsParseRecord, DnsReadName) — the historically drift-prone twin of
/// DnsWireProtocol — with deterministic, exact-value assertions that live-DNS
/// tests could never make.
/// </summary>
[Collection(DnsFakeServerEnvCollection.Name)]
public class DnsFakeServerModuleTests
{
    /// <summary>
    /// Runs main.ts with the given source while SHARPTS_DNS_SERVER points at the
    /// fake server (and optionally a short SHARPTS_DNS_TIMEOUT_MS for timeout tests).
    /// </summary>
    private static string RunWithFakeDns(FakeDnsServer server, string source, ExecutionMode mode, int? timeoutMs = null)
    {
        Environment.SetEnvironmentVariable("SHARPTS_DNS_SERVER", server.Address);
        if (timeoutMs is int ms)
            Environment.SetEnvironmentVariable("SHARPTS_DNS_TIMEOUT_MS", ms.ToString());
        try
        {
            return TestHarness.RunModules(new Dictionary<string, string> { ["main.ts"] = source }, "main.ts", mode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SHARPTS_DNS_SERVER", null);
            Environment.SetEnvironmentVariable("SHARPTS_DNS_TIMEOUT_MS", null);
        }
    }

    /// <summary>
    /// Fake server answering every record type with one canonical record. Names in
    /// RDATA use compression pointers at the question name, as real resolvers emit.
    /// </summary>
    private static FakeDnsServer CreateRecordServer() => new((request, _) =>
    {
        var qtype = DnsPackets.QueryType(request);
        var rdata = qtype switch
        {
            DnsWireProtocol.TypeA => DnsPackets.A("93.184.216.34"),
            DnsWireProtocol.TypeAAAA => DnsPackets.Aaaa("2606:2800:220:1:248:1893:25c8:1946"),
            DnsWireProtocol.TypeMX => DnsPackets.Mx(10, DnsPackets.LabelsThenPointer("mail")),
            DnsWireProtocol.TypeTXT => DnsPackets.Txt("v=spf1 -all"),
            DnsWireProtocol.TypeNS => DnsPackets.LabelsThenPointer("ns1"),
            DnsWireProtocol.TypeSOA => DnsPackets.Soa(
                DnsPackets.LabelsThenPointer("ns1"), DnsPackets.LabelsThenPointer("hostmaster"),
                serial: 2024010101, refresh: 7200, retry: 900, expire: 1209600, minimum: 86400),
            DnsWireProtocol.TypeSRV => DnsPackets.Srv(priority: 1, weight: 5, port: 5060,
                DnsPackets.LabelsThenPointer("sip")),
            DnsWireProtocol.TypeCAA => DnsPackets.Caa(flags: 0x80, "issue", "ca.example.net"),
            DnsWireProtocol.TypeCNAME => DnsPackets.LabelsThenPointer("canonical"),
            DnsWireProtocol.TypePTR => DnsPackets.Name("host.example.net"),
            DnsWireProtocol.TypeNAPTR => DnsPackets.Naptr(order: 100, preference: 50, "s", "SIP+D2T", "",
                DnsPackets.LabelsThenPointer("_sip._tcp")),
            _ => null
        };
        return rdata is null
            ? DnsPackets.Response(request, 3) // NXDOMAIN
            : DnsPackets.Response(request, 0, DnsPackets.Record(qtype, rdata));
    });

    // Exactly one top-level dns call per program: dns callback wrappers invoked
    // from inside another callback never fire in compiled mode (see issue filed
    // from this work) — and concurrent top-level calls would interleave output.
    private static readonly (string Method, string Logs, string Expected)[] RecordTypeAssertions =
    [
        ("resolveMx", "console.log(r[0].exchange); console.log(r[0].priority);",
            "mail.fake.test\n10\n"),
        ("resolveTxt", "console.log(r[0][0]);",
            "v=spf1 -all\n"),
        ("resolveNs", "console.log(r[0]);",
            "ns1.fake.test\n"),
        ("resolveSoa", "console.log(r.nsname); console.log(r.hostmaster); console.log(r.serial); console.log(r.minttl);",
            "ns1.fake.test\nhostmaster.fake.test\n2024010101\n86400\n"),
        ("resolveSrv", "console.log(r[0].name); console.log(r[0].port); console.log(r[0].priority); console.log(r[0].weight);",
            "sip.fake.test\n5060\n1\n5\n"),
        ("resolveCaa", "console.log(r[0].critical); console.log(r[0].issue);",
            "128\nca.example.net\n"),
        ("resolveCname", "console.log(r[0]);",
            "canonical.fake.test\n"),
        ("resolvePtr", "console.log(r[0]);",
            "host.example.net\n"),
        ("resolveNaptr", "console.log(r[0].order); console.log(r[0].preference); console.log(r[0].flags); console.log(r[0].service); console.log(r[0].replacement);",
            "100\n50\ns\nSIP+D2T\n_sip._tcp.fake.test\n"),
    ];

    public static IEnumerable<object[]> RecordTypeCases()
    {
        foreach (var modeRow in ExecutionModes.All)
            foreach (var (method, logs, expected) in RecordTypeAssertions)
                yield return [modeRow[0], method, logs, expected];
    }

    [Theory]
    [MemberData(nameof(RecordTypeCases))]
    public void ResolveRecordType_FakeServer_ExactValues(ExecutionMode mode, string method, string logs, string expected)
    {
        using var server = CreateRecordServer();
        var output = RunWithFakeDns(server, $$"""
            import * as dns from 'dns';
            dns.{{method}}('fake.test', (err: any, r: any) => {
                console.log(err === null);
                {{logs}}
            });
            """, mode);

        Assert.Equal("true\n" + expected, output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Resolve_WithMxRrtype_FakeServer_ExactValues(ExecutionMode mode)
    {
        using var server = CreateRecordServer();
        var output = RunWithFakeDns(server, """
            import * as dns from 'dns';
            dns.resolve('fake.test', 'MX', (err: any, records: any) => {
                console.log(err === null);
                console.log(records[0].exchange);
                console.log(records[0].priority);
            });
            """, mode);

        Assert.Equal("true\nmail.fake.test\n10\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void DnsPromises_ResolveMxAndTxt_FakeServer_ExactValues(ExecutionMode mode)
    {
        using var server = CreateRecordServer();
        var output = RunWithFakeDns(server, """
            import dns from 'dns/promises';
            async function main() {
                const mx: any = await dns.resolveMx('fake.test');
                console.log(mx[0].exchange);
                console.log(mx[0].priority);
                const txt: any = await dns.resolveTxt('fake.test');
                console.log(txt[0][0]);
            }
            main();
            """, mode);

        Assert.Equal("mail.fake.test\n10\nv=spf1 -all\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ResolveTxt_TruncatedUdp_FallsBackToTcp(ExecutionMode mode)
    {
        using var server = new FakeDnsServer(
            (request, _) => DnsPackets.Truncated(request),
            tcpResponder: request => DnsPackets.Response(request, 0,
                DnsPackets.Record(DnsWireProtocol.TypeTXT, DnsPackets.Txt("via-tcp"))));

        var output = RunWithFakeDns(server, """
            import * as dns from 'dns';
            dns.resolveTxt('fake.test', (err: any, records: any) => {
                console.log(err === null);
                console.log(records[0][0]);
            });
            """, mode);

        Assert.Equal("true\nvia-tcp\n", output);
        Assert.Equal(1, server.TcpQueryCount);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ResolveMx_Nxdomain_CallsBackWithError(ExecutionMode mode)
    {
        using var server = new FakeDnsServer((request, _) => DnsPackets.Response(request, 3));

        var output = RunWithFakeDns(server, """
            import * as dns from 'dns';
            dns.resolveMx('fake.test', (err: any, records: any) => {
                console.log(err !== null);
                console.log(records === null);
            });
            """, mode);

        Assert.Equal("true\ntrue\n", output);
        Assert.Equal(1, server.QueryCount); // NXDOMAIN is final — no retry
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ResolveNs_Nxdomain_Promise_Rejects(ExecutionMode mode)
    {
        using var server = new FakeDnsServer((request, _) => DnsPackets.Response(request, 3));

        var output = RunWithFakeDns(server, """
            import dns from 'dns/promises';
            async function main() {
                try {
                    await dns.resolveNs('fake.test');
                    console.log('no error');
                } catch (e) {
                    console.log('error caught');
                }
            }
            main();
            """, mode);

        Assert.Equal("error caught\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ResolveMx_ServerNeverAnswers_TimesOutAfterRetries(ExecutionMode mode)
    {
        using var server = new FakeDnsServer((_, _) => null); // swallow every query

        var output = RunWithFakeDns(server, """
            import * as dns from 'dns';
            dns.resolveMx('fake.test', (err: any, records: any) => {
                console.log(err !== null);
                console.log(records === null);
            });
            """, mode, timeoutMs: 250);

        Assert.Equal("true\ntrue\n", output);
        Assert.Equal(3, server.QueryCount); // initial attempt + MaxRetries (2)
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Resolver_SetServers_QueriesFakeServer(ExecutionMode mode)
    {
        // No env redirection here: the Resolver is pointed at the fake explicitly
        // via setServers, covering the DnsResolverInstance custom-server path
        // (which both runtimes share) end to end.
        using var server = CreateRecordServer();

        var source = $$"""
            import { Resolver } from 'dns';
            const resolver = new Resolver();
            resolver.setServers(['{{server.Address}}']);
            resolver.resolveMx('fake.test', (err: any, records: any) => {
                console.log(err === null);
                console.log(records[0].exchange);
                console.log(records[0].priority);
            });
            """;

        var output = TestHarness.RunModules(
            new Dictionary<string, string> { ["main.ts"] = source }, "main.ts", mode);

        Assert.Equal("true\nmail.fake.test\n10\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Resolve4_CnameOnlyAnswer_FollowsChain(ExecutionMode mode)
    {
        // CNAME-chain following (#1073): a response carrying ONLY a CNAME makes the
        // client re-query the target. Both wire protocols (C# and emitted IL) must
        // chase identically.
        using var server = new FakeDnsServer((request, _) =>
            DnsPackets.QueryName(request) == "alias.test"
                ? DnsPackets.Response(request, 0,
                    DnsPackets.Record(DnsWireProtocol.TypeCNAME, DnsPackets.Name("target.test")))
                : DnsPackets.Response(request, 0,
                    DnsPackets.Record(DnsWireProtocol.TypeA, DnsPackets.A("10.1.2.3"))));

        var output = RunWithFakeDns(server, """
            import * as dns from 'dns';
            dns.resolve4('alias.test', (err: any, addrs: any) => {
                console.log(err === null);
                console.log(addrs[0]);
            });
            """, mode);

        Assert.Equal("true\n10.1.2.3\n", output);
        Assert.Equal(2, server.QueryCount);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Resolve4_CnameLoop_BoundedAndErrors(ExecutionMode mode)
    {
        // A CNAME pointing at itself must terminate at the chase depth bound and
        // surface an error rather than spin forever (#1073).
        using var server = new FakeDnsServer((request, _) =>
            DnsPackets.Response(request, 0,
                DnsPackets.Record(DnsWireProtocol.TypeCNAME, DnsPackets.Name("loop.test"))));

        var output = RunWithFakeDns(server, """
            import * as dns from 'dns';
            dns.resolve4('loop.test', (err: any, addrs: any) => {
                console.log(err !== null);
            });
            """, mode);

        Assert.Equal("true\n", output);
        Assert.Equal(9, server.QueryCount); // initial + MaxCnameChaseDepth (8)
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ResolveCaa_CriticalFlagsByte_Parity(ExecutionMode mode)
    {
        // Node reports the whole CAA flags byte as 'critical' (#1073).
        using var server = new FakeDnsServer((request, _) =>
            DnsPackets.Response(request, 0,
                DnsPackets.Record(DnsWireProtocol.TypeCAA,
                    DnsPackets.Caa(flags: 0, "issue", "ca.example.net"))));

        var output = RunWithFakeDns(server, """
            import * as dns from 'dns';
            dns.resolveCaa('fake.test', (err: any, records: any) => {
                console.log(err === null);
                console.log(records[0].critical);
                console.log(records[0].issue);
            });
            """, mode);

        Assert.Equal("true\n0\nca.example.net\n", output);
    }
}

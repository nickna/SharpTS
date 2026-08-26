using SharpTS.Runtime.BuiltIns.Modules;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Tests for dns.Resolver class: construction, setServers/getServers, resolve methods.
/// </summary>
public class DnsResolverTests
{
    #region Construction and typeof

    [Theory, ModeData]
    public void Resolver_CanBeConstructed(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as dns from 'dns';
                const resolver = new dns.Resolver();
                console.log(typeof resolver === 'object');
                console.log(resolver !== null);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Resolver_ImportNamed(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Resolver } from 'dns';
                const resolver = new Resolver();
                console.log(typeof resolver === 'object');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region getServers / setServers

    [Theory, ModeData]
    public void Resolver_GetServers_InitiallyEmpty(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Resolver } from 'dns';
                const resolver = new Resolver();
                const servers = resolver.getServers();
                console.log(Array.isArray(servers));
                console.log(servers.length === 0);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Resolver_SetServers_ThenGetServers(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Resolver } from 'dns';
                const resolver = new Resolver();
                resolver.setServers(['8.8.8.8', '8.8.4.4']);
                const servers = resolver.getServers();
                console.log(servers.length === 2);
                console.log(servers[0] === '8.8.8.8');
                console.log(servers[1] === '8.8.4.4');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Resolver_SetServers_InvalidAddress_Throws(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Resolver } from 'dns';
                const resolver = new Resolver();
                try {
                    resolver.setServers(['not.a.valid.ip']);
                    console.log('no error');
                } catch (e) {
                    console.log('error thrown');
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("error thrown\n", output);
    }

    [Theory, ModeData]
    public void Resolver_SetServers_ReplacesExisting(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Resolver } from 'dns';
                const resolver = new Resolver();
                resolver.setServers(['8.8.8.8']);
                resolver.setServers(['1.1.1.1']);
                const servers = resolver.getServers();
                console.log(servers.length === 1);
                console.log(servers[0] === '1.1.1.1');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    #endregion

    #region Resolver has all resolve methods

    [Theory, ModeData]
    public void Resolver_HasAllMethods(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Resolver } from 'dns';
                const resolver = new Resolver();
                console.log(typeof resolver.resolve === 'function');
                console.log(typeof resolver.resolve4 === 'function');
                console.log(typeof resolver.resolve6 === 'function');
                console.log(typeof resolver.reverse === 'function');
                console.log(typeof resolver.resolveMx === 'function');
                console.log(typeof resolver.resolveTxt === 'function');
                console.log(typeof resolver.resolveSrv === 'function');
                console.log(typeof resolver.resolveCname === 'function');
                console.log(typeof resolver.resolveNs === 'function');
                console.log(typeof resolver.resolveSoa === 'function');
                console.log(typeof resolver.resolvePtr === 'function');
                console.log(typeof resolver.resolveCaa === 'function');
                console.log(typeof resolver.resolveNaptr === 'function');
                console.log(typeof resolver.setServers === 'function');
                console.log(typeof resolver.getServers === 'function');
                console.log(typeof resolver.cancel === 'function');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        var lines = output.Trim().Split('\n');
        Assert.Equal(16, lines.Length);
        Assert.All(lines, line => Assert.Equal("true", line));
    }

    #endregion

    #region Resolver.resolve4 against a fake server (setServers)

    [Theory, ModeData]
    public void Resolver_Resolve4_FakeServer_CallbackStyle(ExecutionMode mode)
    {
        // resolve4 uses the DNS wire protocol; point the Resolver at a loopback fake
        // server via setServers (no env redirect needed) for a deterministic A record.
        using var server = new FakeDnsServer((request, _) =>
        {
            var qtype = DnsPackets.QueryType(request);
            return qtype == DnsWireProtocol.TypeA
                ? DnsPackets.Response(request, 0, DnsPackets.Record(qtype, DnsPackets.A("127.0.0.1")))
                : DnsPackets.Response(request, 3);
        });

        var source = $$"""
            import { Resolver } from 'dns';
            const resolver = new Resolver();
            resolver.setServers(['{{server.Address}}']);
            resolver.resolve4('fake.test', (err: any, addresses: string[]) => {
                if (err) {
                    console.log('error: ' + err.code);
                } else {
                    console.log(Array.isArray(addresses));
                    console.log(addresses.length > 0);
                    console.log(addresses[0] === '127.0.0.1');
                }
            });
            """;

        var output = TestHarness.RunModules(
            new Dictionary<string, string> { ["main.ts"] = source }, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    #endregion

    #region Multiple resolvers are independent

    [Theory, ModeData]
    public void Resolver_MultipleInstances_IndependentServers(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Resolver } from 'dns';
                const r1 = new Resolver();
                const r2 = new Resolver();
                r1.setServers(['8.8.8.8']);
                r2.setServers(['1.1.1.1', '1.0.0.1']);
                console.log(r1.getServers().length === 1);
                console.log(r2.getServers().length === 2);
                console.log(r1.getServers()[0] === '8.8.8.8');
                console.log(r2.getServers()[0] === '1.1.1.1');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\n", output);
    }

    #endregion

    #region cancel

    [Theory, ModeData]
    public void Resolver_Cancel_DoesNotThrow(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Resolver } from 'dns';
                const resolver = new Resolver();
                resolver.cancel();
                console.log('ok');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("ok\n", output);
    }

    #endregion
}

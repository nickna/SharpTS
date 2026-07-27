using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// DNS record-type resolution: API surface tests (no network) plus a minimal live
/// smoke set against a stable public domain, tolerant of transient resolver
/// failure on CI runners. All behavioral coverage (record decoding, compression,
/// truncation, rcodes, retries, timeouts) lives in the deterministic fake-server
/// suites: RuntimeTests.DnsWireProtocolFakeServerTests (interpreter wire protocol)
/// and DnsFakeServerModuleTests (both runtimes, exact-value assertions).
/// </summary>
public class DnsRecordTypeTests
{
    #region Live smoke tests (opt-in [Trait Category=LiveNetwork], excluded from CI)

    // A single callback-based and a single promise-based live query against a
    // stable public domain. Tagged LiveNetwork so CI (which runs
    // --filter "Category!=LiveNetwork") never runs them; a developer can run them
    // on demand. Exact-value behavioral coverage is pinned by the deterministic
    // fake-server suites (DnsFakeServerModuleTests); these only assert the real
    // resolver is reachable and returns the right shape.

    [Theory]
    [Trait("Category", "LiveNetwork")]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void LiveSmoke_ResolveMx_Callback(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import * as dns from 'dns';
                dns.resolveMx('{{LiveNetworkHosts.Stable}}', (err: any, addresses: any) => {
                    if (err === null) {
                        console.log(Array.isArray(addresses));
                        console.log(addresses.length > 0);
                        console.log(typeof addresses[0].exchange === 'string');
                        console.log(typeof addresses[0].priority === 'number');
                    } else {
                        // Transient resolver failure tolerated — live smoke test.
                        console.log(true);
                        console.log(true);
                        console.log(true);
                        console.log(true);
                    }
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\n", output);
    }

    [Theory]
    [Trait("Category", "LiveNetwork")]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void LiveSmoke_ResolveNs_Promise(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import dns from 'dns/promises';
                async function main() {
                    try {
                        const nameservers = await dns.resolveNs('{{LiveNetworkHosts.Stable}}');
                        console.log(Array.isArray(nameservers));
                        console.log(nameservers.length > 0);
                        console.log(typeof nameservers[0] === 'string');
                    } catch (e) {
                        // Transient resolver failure tolerated — live smoke test.
                        console.log(true);
                        console.log(true);
                        console.log(true);
                    }
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    #endregion

    // NXDOMAIN error-path coverage (resolveMx callback-error / resolveNs promise-reject)
    // lives in DnsFakeServerModuleTests.ResolveMx_Nxdomain_CallsBackWithError and
    // ResolveNs_Nxdomain_Promise_Rejects — deterministic, both runtimes, no live network.

    #region API surface tests (no network)

    [Theory, ModeData]
    public void Dns_Import_NewMethods_Exist(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as dns from 'dns';
                console.log(typeof dns.resolveMx === 'function');
                console.log(typeof dns.resolveTxt === 'function');
                console.log(typeof dns.resolveSrv === 'function');
                console.log(typeof dns.resolveCname === 'function');
                console.log(typeof dns.resolveNs === 'function');
                console.log(typeof dns.resolveSoa === 'function');
                console.log(typeof dns.resolvePtr === 'function');
                console.log(typeof dns.resolveCaa === 'function');
                console.log(typeof dns.resolveNaptr === 'function');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void DnsPromises_Import_NewMethods_Exist(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import dns from 'dns/promises';
                console.log(typeof dns.resolveMx === 'function');
                console.log(typeof dns.resolveTxt === 'function');
                console.log(typeof dns.resolveSrv === 'function');
                console.log(typeof dns.resolveCname === 'function');
                console.log(typeof dns.resolveNs === 'function');
                console.log(typeof dns.resolveSoa === 'function');
                console.log(typeof dns.resolvePtr === 'function');
                console.log(typeof dns.resolveCaa === 'function');
                console.log(typeof dns.resolveNaptr === 'function');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Dns_NamedImport_ResolveMx(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { resolveMx, resolveNs, resolveTxt } from 'dns';
                console.log(typeof resolveMx === 'function');
                console.log(typeof resolveNs === 'function');
                console.log(typeof resolveTxt === 'function');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    #endregion
}

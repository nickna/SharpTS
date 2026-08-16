using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Tests for the Node.js 'dns' module: lookup, lookupService.
/// </summary>
public class DnsModuleTests
{
    #region Import Tests

    [Theory, ModeData]
    public void Dns_Import_Namespace(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as dns from 'dns';
                console.log(typeof dns === 'object');
                console.log(typeof dns.lookup === 'function');
                console.log(typeof dns.lookupService === 'function');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Dns_Import_Named(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { lookup, lookupService } from 'dns';
                console.log(typeof lookup === 'function');
                console.log(typeof lookupService === 'function');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    #endregion

    #region Constants Tests

    [Theory, ModeData]
    public void Dns_Constants_Defined(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as dns from 'dns';
                console.log(dns.ADDRCONFIG === 1);
                console.log(dns.V4MAPPED === 2);
                console.log(dns.ALL === 4);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    #endregion

    #region dns.lookup Tests

    [Theory, ModeData]
    public void Dns_Lookup_Localhost_ReturnsObject(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { lookup } from 'dns';
                const result = lookup('localhost');
                console.log(typeof result === 'object');
                console.log(result !== null);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Dns_Lookup_Localhost_HasAddressAndFamily(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { lookup } from 'dns';
                const result = lookup('localhost');
                console.log(typeof result.address === 'string');
                console.log(typeof result.family === 'number');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Dns_Lookup_Localhost_AddressIsValid(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { lookup } from 'dns';
                const result = lookup('localhost');
                // localhost should resolve to 127.0.0.1 or ::1
                const isIPv4Localhost = result.address === '127.0.0.1';
                const isIPv6Localhost = result.address === '::1';
                console.log(isIPv4Localhost || isIPv6Localhost);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Dns_Lookup_Localhost_FamilyIs4Or6(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { lookup } from 'dns';
                const result = lookup('localhost');
                console.log(result.family === 4 || result.family === 6);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Dns_Lookup_WithFamilyOption_IPv4(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { lookup } from 'dns';
                const result = lookup('localhost', 4);
                console.log(result.family === 4);
                console.log(result.address === '127.0.0.1');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    // LiveNetwork: dns.lookup uses the OS resolver (getaddrinfo), which has no
    // SHARPTS_DNS_SERVER redirect seam, so a real NXDOMAIN answer requires the live
    // resolver. Tagged so CI excludes it; run on demand to verify the error path.
    [Theory]
    [Trait("Category", "LiveNetwork")]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Dns_Lookup_InvalidHostname_Throws(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import { lookup } from 'dns';
                try {
                    lookup('{{LiveNetworkHosts.Nonexistent}}');
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
    public void Dns_Lookup_RequiresHostname(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { lookup } from 'dns';
                try {
                    (lookup as any)();
                    console.log('no error');
                } catch (e) {
                    console.log('error thrown');
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("error thrown\n", output);
    }

    [Theory, InterpretedOnlyData]
    public void Dns_Lookup_Callback_InvokedWithAddressAndFamily(ExecutionMode mode)
    {
        // #206: the callback form must invoke (err, address, family)
        // asynchronously and keep the event loop alive until it fires.
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { lookup } from 'dns';
                lookup('localhost', (err: any, address: any, family: any) => {
                    console.log(err === null);
                    console.log(address === '127.0.0.1' || address === '::1');
                    console.log(family === 4 || family === 6);
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Theory, InterpretedOnlyData]
    public void Dns_Lookup_Callback_AllOption_ReceivesArray(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { lookup } from 'dns';
                lookup('localhost', { all: true }, (err: any, addresses: any) => {
                    console.log(err === null);
                    console.log(addresses.length > 0);
                    console.log(typeof addresses[0].address === 'string');
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Theory]
    [Trait("Category", "LiveNetwork")]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void Dns_Lookup_Callback_InvalidHostname_ReceivesError(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import { lookup } from 'dns';
                lookup('{{LiveNetworkHosts.Nonexistent}}', (err: any, address: any) => {
                    console.log(err !== null);
                    console.log(address === null);
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    #endregion

    #region dns.lookupService Tests

    [Theory, InterpretedOnlyData]
    public void Dns_LookupService_Callback_InvokedWithHostnameAndService(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { lookupService } from 'dns';
                lookupService('127.0.0.1', 80, (err: any, hostname: any, service: any) => {
                    console.log(err === null);
                    console.log(typeof hostname === 'string');
                    console.log(service === '80');
                });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Dns_LookupService_LocalhostPort80_ReturnsObject(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { lookupService } from 'dns';
                const result = lookupService('127.0.0.1', 80);
                console.log(typeof result === 'object');
                console.log(result !== null);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Dns_LookupService_HasHostnameAndService(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { lookupService } from 'dns';
                const result = lookupService('127.0.0.1', 80);
                console.log(typeof result.hostname === 'string');
                console.log(typeof result.service === 'string');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Dns_LookupService_ServiceIsPortString(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { lookupService } from 'dns';
                const result = lookupService('127.0.0.1', 8080);
                console.log(result.service === '8080');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Dns_LookupService_InvalidAddress_Throws(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { lookupService } from 'dns';
                try {
                    lookupService('not.an.ip.address', 80);
                    console.log('no error');
                } catch (e) {
                    console.log('error thrown');
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("error thrown\n", output);
    }

    #endregion
}

using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Dual-mode tests for tls.checkServerIdentity (#1036). Interp == compiled.
/// </summary>
public class TlsCheckIdentityTests
{
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void CheckServerIdentity_SanMatch(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as tls from 'tls';
                const cert = { subject: 'CN=example.com', subjectaltname: 'DNS:example.com, DNS:www.example.com' };
                console.log(tls.checkServerIdentity('example.com', cert) === undefined);
                console.log(tls.checkServerIdentity('www.example.com', cert) === undefined);
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void CheckServerIdentity_Mismatch(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as tls from 'tls';
                const cert = { subject: 'CN=example.com', subjectaltname: 'DNS:example.com' };
                const err = tls.checkServerIdentity('evil.com', cert);
                console.log(err !== undefined);
                console.log(err instanceof Error);
                console.log(err !== undefined && err.message.indexOf('evil.com') >= 0);
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void CheckServerIdentity_Wildcard(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as tls from 'tls';
                const cert = { subjectaltname: 'DNS:*.example.com' };
                console.log(tls.checkServerIdentity('api.example.com', cert) === undefined);
                console.log(tls.checkServerIdentity('example.com', cert) !== undefined);
                console.log(tls.checkServerIdentity('a.b.example.com', cert) !== undefined);
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void CheckServerIdentity_CnFallback(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as tls from 'tls';
                const cert = { subject: 'CN=localhost, O=Acme' };
                console.log(tls.checkServerIdentity('localhost', cert) === undefined);
                console.log(tls.checkServerIdentity('other', cert) !== undefined);
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }
}

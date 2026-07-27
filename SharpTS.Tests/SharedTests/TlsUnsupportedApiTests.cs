using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// #1039: advanced TLS APIs not exposed by .NET SslStream throw a clear error (not a silent
/// no-op), identically in interp and compiled mode.
/// </summary>
public class TlsUnsupportedApiTests
{
    [Theory, ModeData]
    public void UnsupportedApis_Throw(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as tls from 'tls';
                const socket = tls.TLSSocket();
                const names = ['getSession', 'setSession', 'getTLSTicket', 'getPeerFinished',
                               'getFinished', 'setMaxSendFragment', 'exportKeyingMaterial'];
                for (const n of names) {
                    try {
                        (socket as any)[n]();
                        console.log(n + ':no-throw');
                    } catch (e: any) {
                        const msg = e && e.message ? e.message : '';
                        console.log(n + ':' + (e instanceof Error && msg.indexOf('not supported') >= 0));
                    }
                }
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal(
            "getSession:true\nsetSession:true\ngetTLSTicket:true\ngetPeerFinished:true\n" +
            "getFinished:true\nsetMaxSendFragment:true\nexportKeyingMaterial:true\n",
            output);
    }
}

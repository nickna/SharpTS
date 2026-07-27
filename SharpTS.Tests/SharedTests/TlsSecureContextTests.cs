using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Dual-mode tests for tls.getCiphers / tls.rootCertificates / SecureContext (#1037).
/// </summary>
public class TlsSecureContextTests
{
    [Theory, ModeData]
    public void GetCiphers(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as tls from 'tls';
                const c = tls.getCiphers();
                console.log(Array.isArray(c));
                console.log(c.length > 0);
                console.log(c.indexOf('tls_aes_128_gcm_sha256') >= 0);
                console.log(c[0] === c[0].toLowerCase());
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void RootCertificates(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as tls from 'tls';
                const roots = tls.rootCertificates;
                console.log(Array.isArray(roots));
                console.log(roots.length > 0);
                console.log(roots[0].indexOf('-----BEGIN CERTIFICATE-----') >= 0);
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void SecureContext_ServerHandshake(ExecutionMode mode)
    {
        var (certPem, keyPem) = TlsModuleTestsCertHelper.GenerateSelfSignedCert();
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as tls from 'tls';
                const cert = `{{certPem}}`;
                const key = `{{keyPem}}`;
                const ctx = tls.createSecureContext({ cert, key });
                console.log('ctx-ok:' + (ctx !== null && ctx !== undefined));
                const server = tls.createServer({ secureContext: ctx }, (socket: any) => {
                    socket.write('hi');
                    socket.end();
                    server.close();
                });
                server.listen(0, '127.0.0.1', () => {
                    const addr = server.address();
                    const client = tls.connect(addr.port, '127.0.0.1', { rejectUnauthorized: false });
                    client.setEncoding('utf8');
                    let data = '';
                    client.on('data', (c: string) => { data += c; });
                    client.on('end', () => { console.log('recv:' + data); client.destroy(); });
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("ctx-ok:true\nrecv:hi\n", output);
    }
}

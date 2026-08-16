using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for the https module — a real TLS-terminating server (#1049) and a TLS client (#1050),
/// built on the tls #1032 work (no longer a cleartext proxy to http).
/// </summary>
/// <remarks>
/// Interpreter-mode: the HTTPS server runs the HTTP pipeline over a real SslStream and the client
/// negotiates TLS via HttpClient. The compiled HTTPS server (HTTP parser/serializer over SslStream
/// in IL) is a documented follow-up, so the server round-trip tests are interpreted-only. Basic
/// shape (createServer/request/get exist) is dual-mode.
/// </remarks>
public class HttpsModuleTests
{
    [Theory, ModeData]
    public void Https_Exports_Exist(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as https from 'https';
                console.log(typeof https.createServer);
                console.log(typeof https.request);
                console.log(typeof https.get);
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("function\nfunction\nfunction\n", output);
    }

    [Theory, InterpretedOnlyData]
    public void Https_ServerClient_RoundTrip(ExecutionMode mode)
    {
        var (certPem, keyPem) = TlsModuleTestsCertHelper.GenerateSelfSignedCert();
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as https from 'https';
                const server: any = https.createServer(
                    { key: `{{keyPem}}`, cert: `{{certPem}}` },
                    (req: any, res: any) => {
                        res.writeHead(200, { 'Content-Type': 'text/plain' });
                        res.end('secure:' + req.url);
                    });
                server.listen(0, () => {
                    const port = server.address().port;
                    https.get({ host: '127.0.0.1', port, path: '/hello', rejectUnauthorized: false }, (res: any) => {
                        console.log('status=' + res.statusCode);
                        let body = '';
                        res.on('data', (c: any) => { body += c.toString(); });
                        res.on('end', () => {
                            console.log('body=' + body);
                            server.close();
                        });
                    });
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("status=200", output);
        Assert.Contains("body=secure:/hello", output);
    }

    [Theory, InterpretedOnlyData]
    public void Https_ServerClient_PostBody(ExecutionMode mode)
    {
        var (certPem, keyPem) = TlsModuleTestsCertHelper.GenerateSelfSignedCert();
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as https from 'https';
                const server: any = https.createServer(
                    { key: `{{keyPem}}`, cert: `{{certPem}}` },
                    (req: any, res: any) => {
                        let body = '';
                        req.on('data', (c: any) => { body += c.toString(); });
                        req.on('end', () => { res.end('echo:' + body); });
                    });
                server.listen(0, () => {
                    const port = server.address().port;
                    const r: any = https.request(
                        { host: '127.0.0.1', port, path: '/', method: 'POST', rejectUnauthorized: false },
                        (res: any) => {
                            let body = '';
                            res.on('data', (c: any) => { body += c.toString(); });
                            res.on('end', () => { console.log(body); server.close(); });
                        });
                    r.write('payload-over-tls');
                    r.end();
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("echo:payload-over-tls", output);
    }
}

using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Type-surface tests (#1053): idiomatic http/https client + server code type-checks against the
/// refined GetHttpModuleTypes surface (ClientRequest/IncomingMessage/ServerResponse/Server/Agent,
/// utilities, https TLS options).
/// </summary>
public class HttpTypeSurfaceTests
{
    [Theory, ModeData]
    public void ServerAndClient_TypeCheck(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as http from 'http';
                const server = http.createServer((req, res) => { res.end('ok'); });
                server.keepAliveTimeout = 5000;
                server.requestTimeout = 1000;
                server.closeAllConnections();
                server.closeIdleConnections();
                const max: number = http.maxHeaderSize;
                http.validateHeaderName('X-A');
                http.validateHeaderValue('X-A', 'v');
                http.setMaxIdleHTTPParsers(4);
                console.log('typecheck ok ' + max);
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("typecheck ok 16384", output);
    }

    [Theory, ModeData]
    public void HttpsCreateServerWithTlsOptions_TypeChecks(ExecutionMode mode)
    {
        var (certPem, keyPem) = TlsModuleTestsCertHelper.GenerateSelfSignedCert();
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as https from 'https';
                const server = https.createServer({ key: `{{keyPem}}`, cert: `{{certPem}}` },
                    (req, res) => { res.end(); });
                console.log(typeof server.listen);
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("function\n", output);
    }
}

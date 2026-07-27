using System.Net;
using System.Net.Sockets;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for the advanced server events (#1046): checkContinue (Expect: 100-continue) plus the
/// registrable-but-platform-limited upgrade/connect/clientError surface.
/// </summary>
/// <remarks>
/// upgrade/connect/clientError require raw-socket handoff that HttpListener does not provide, so
/// they are registrable (EventEmitter) but never fire — an HttpListener ceiling matching the
/// epic's stated scope (WebSocket framing / CONNECT tunneling are userland). checkContinue is
/// fully supported; firing it needs an Expect: 100-continue client, exercised interpreted-only
/// via the ClientRequest (the compiled client still routes through fetch).
/// </remarks>
public class HttpServerAdvancedEventsTests
{
    [Theory, ModeData]
    public void Server_AdvancedEventListeners_AreRegistrable(ExecutionMode mode)
    {
        // Registering upgrade/connect/clientError/checkContinue must not break normal serving.
        var port = TestPorts.GetAvailablePort();
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as http from 'http';
                const server: any = http.createServer((req: any, res: any) => {
                    console.log('handled');
                    res.end('ok');
                    server.close();
                });
                server.on('upgrade', () => {});
                server.on('connect', () => {});
                server.on('clientError', () => {});
                server.on('checkContinue', () => {});
                console.log('upgrade=' + server.listenerCount('upgrade'));
                console.log('connect=' + server.listenerCount('connect'));
                server.listen({{port}}, () => { http.get('http://127.0.0.1:{{port}}/'); });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("upgrade=1", output);
        Assert.Contains("connect=1", output);
        Assert.Contains("handled", output);
    }

    [Theory, InterpretedOnlyData]
    public void Server_CheckContinue_FiresInsteadOfRequest(ExecutionMode mode)
    {
        var port = TestPorts.GetAvailablePort();
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as http from 'http';
                const server: any = http.createServer((req: any, res: any) => {
                    console.log('NORMAL-HANDLER');
                    res.end('normal');
                });
                server.on('checkContinue', (req: any, res: any) => {
                    console.log('checkContinue:' + req.method);
                    res.writeContinue();
                    let body = '';
                    req.on('data', (c: any) => { body += c.toString(); });
                    req.on('end', () => { res.end('continued:' + body); });
                });
                server.listen({{port}}, () => {
                    const req: any = http.request(
                        { host: '127.0.0.1', port: {{port}}, path: '/', method: 'POST',
                          headers: { 'Expect': '100-continue' } },
                        (res: any) => {
                            let t = '';
                            res.on('data', (c: any) => { t += c.toString(); });
                            res.on('end', () => { console.log('client:' + t); server.close(); });
                        });
                    req.write('payload');
                    req.end();
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("checkContinue:POST", output);
        Assert.Contains("client:continued:payload", output);
        Assert.DoesNotContain("NORMAL-HANDLER", output);
    }
}

using System.Net;
using System.Net.Sockets;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for the http.Agent connection-pool surface (#1051): getName, observable sockets/
/// freeSockets state, keep-alive socket reuse, agent:false, and destroy().
/// </summary>
/// <remarks>
/// Real socket ownership lives in HttpClient; the agent exposes a Node-shaped shadow pool that
/// the ClientRequest populates. The pooling behaviour is exercised interpreted-only (the compiled
/// client routes through fetch); getName and the config surface are dual-mode.
/// </remarks>
public class HttpAgentPoolTests
{
    [Theory, ModeData]
    public void Agent_GetName_FormatsOriginKey(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as http from 'http';
                const agent: any = new http.Agent({ keepAlive: true });
                console.log(agent.getName({ host: 'example.com', port: 8080 }));
                console.log('keepAlive=' + agent.keepAlive);
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("example.com:8080:", output);
        Assert.Contains("keepAlive=true", output);
    }

    [Theory, InterpretedOnlyData]
    public void Agent_KeepAlive_PopulatesFreeSockets(ExecutionMode mode)
    {
        var port = TestPorts.GetAvailablePort();
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as http from 'http';
                const agent: any = new http.Agent({ keepAlive: true });
                const server: any = http.createServer((req: any, res: any) => { res.end('ok'); });
                server.listen({{port}}, () => {
                    const req: any = http.request(
                        { host: '127.0.0.1', port: {{port}}, path: '/', agent },
                        (res: any) => {
                            res.on('data', () => {});
                            res.on('end', () => {
                                const keys = Object.keys(agent.freeSockets);
                                console.log('freeOrigins=' + keys.length);
                                server.close();
                            });
                        });
                    req.end();
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("freeOrigins=1", output);
    }

    [Theory, InterpretedOnlyData]
    public void Agent_NoKeepAlive_LeavesFreeSocketsEmpty(ExecutionMode mode)
    {
        var port = TestPorts.GetAvailablePort();
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as http from 'http';
                const agent: any = new http.Agent({ keepAlive: false });
                const server: any = http.createServer((req: any, res: any) => { res.end('ok'); });
                server.listen({{port}}, () => {
                    const req: any = http.request(
                        { host: '127.0.0.1', port: {{port}}, path: '/', agent },
                        (res: any) => {
                            res.on('data', () => {});
                            res.on('end', () => {
                                console.log('freeOrigins=' + Object.keys(agent.freeSockets).length);
                                console.log('activeOrigins=' + Object.keys(agent.sockets).length);
                                server.close();
                            });
                        });
                    req.end();
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("freeOrigins=0", output);
        Assert.Contains("activeOrigins=0", output);
    }

    [Theory, InterpretedOnlyData]
    public void Agent_Destroy_ClearsPool(ExecutionMode mode)
    {
        var port = TestPorts.GetAvailablePort();
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as http from 'http';
                const agent: any = new http.Agent({ keepAlive: true });
                const server: any = http.createServer((req: any, res: any) => { res.end('ok'); });
                server.listen({{port}}, () => {
                    const req: any = http.request(
                        { host: '127.0.0.1', port: {{port}}, path: '/', agent },
                        (res: any) => {
                            res.on('data', () => {});
                            res.on('end', () => {
                                console.log('before=' + Object.keys(agent.freeSockets).length);
                                agent.destroy();
                                console.log('after=' + Object.keys(agent.freeSockets).length);
                                server.close();
                            });
                        });
                    req.end();
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("before=1", output);
        Assert.Contains("after=0", output);
    }
}

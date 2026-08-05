using System.Net;
using System.Net.Sockets;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for IncomingMessage completeness (#1048): rawHeaders (array, case), httpVersionMajor/
/// Minor, trailers/rawTrailers, socket (remoteAddress/family), statusCode undefined on the
/// server side, and on-demand body streaming. The server request object ($HttpRequest) is
/// dual-mode; body content is driven with a dual-mode fetch POST.
/// </summary>
public class HttpIncomingMessageTests
{
    [Theory, ModeData]
    public void IncomingMessage_VersionSocketAndFields(ExecutionMode mode)
    {
        var port = TestPorts.GetAvailablePort();
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as http from 'http';
                const server: any = http.createServer((req: any, res: any) => {
                    console.log('major=' + req.httpVersionMajor);
                    console.log('minor=' + req.httpVersionMinor);
                    console.log('socketType=' + (typeof req.socket));
                    console.log('remoteType=' + (typeof req.socket.remoteAddress));
                    console.log('family=' + req.socket.family);
                    console.log('statusCode=' + (typeof req.statusCode));
                    console.log('rawTrailers=' + Array.isArray(req.rawTrailers));
                    console.log('trailers=' + (typeof req.trailers));
                    res.end('ok');
                    server.close();
                });
                server.listen({{port}}, () => { http.get('http://127.0.0.1:{{port}}/'); });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("major=1", output);
        Assert.Contains("minor=1", output);
        Assert.Contains("socketType=object", output);
        Assert.Contains("remoteType=string", output);
        Assert.Contains("family=IPv4", output);
        Assert.Contains("statusCode=undefined", output);
        Assert.Contains("rawTrailers=true", output);
        Assert.Contains("trailers=object", output);
    }

    [Theory, ModeData]
    public void IncomingMessage_RawHeaders_IsAlternatingArray(ExecutionMode mode)
    {
        var port = TestPorts.GetAvailablePort();
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as http from 'http';
                const server: any = http.createServer((req: any, res: any) => {
                    const raw = req.rawHeaders;
                    console.log('isArray=' + Array.isArray(raw));
                    console.log('even=' + (raw.length % 2 === 0));
                    let hasHost = false;
                    for (let i = 0; i < raw.length; i += 2) {
                        if (String(raw[i]).toLowerCase() === 'host') hasHost = true;
                    }
                    console.log('hasHost=' + hasHost);
                    console.log('lowerHost=' + (typeof req.headers.host));
                    res.end('ok');
                    server.close();
                });
                server.listen({{port}}, () => { http.get('http://127.0.0.1:{{port}}/'); });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("isArray=true", output);
        Assert.Contains("even=true", output);
        Assert.Contains("hasHost=true", output);
        Assert.Contains("lowerHost=string", output);
    }

    [Theory, ModeData]
    public void IncomingMessage_StreamsRequestBody(ExecutionMode mode)
    {
        var port = TestPorts.GetAvailablePort();
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as http from 'http';
                const server: any = http.createServer((req: any, res: any) => {
                    let body = '';
                    req.on('data', (c: any) => { body += c.toString(); });
                    req.on('end', () => {
                        res.end('echo:' + body);
                    });
                });
                server.listen({{port}}, async () => {
                    const r = await fetch('http://127.0.0.1:{{port}}/', { method: 'POST', body: 'streamed-payload' });
                    const t = await r.text();
                    console.log(t);
                    server.close();
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("echo:streamed-payload", output);
    }

    [Theory, ModeData]
    public void IncomingMessage_StreamsLargeBodyInBoundedChunks_AndMarksComplete(ExecutionMode mode)
    {
        var port = TestPorts.GetAvailablePort();
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as http from 'http';
                const server: any = http.createServer((req: any, res: any) => {
                    let chunks = 0;
                    let bytes = 0;
                    req.on('data', (chunk: any) => {
                        chunks++;
                        bytes += chunk.length;
                    });
                    req.on('end', () => {
                        console.log('multiple=' + (chunks > 1));
                        console.log('bytes=' + bytes);
                        console.log('complete=' + req.complete);
                        console.log('aborted=' + req.aborted);
                        res.end('ok');
                    });
                });
                server.listen({{port}}, async () => {
                    const response = await fetch('http://127.0.0.1:{{port}}/', {
                        method: 'POST',
                        body: 'x'.repeat(50000)
                    });
                    await response.text();
                    server.close();
                });
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("multiple=true", output);
        Assert.Contains("bytes=50000", output);
        Assert.Contains("complete=true", output);
        Assert.Contains("aborted=false", output);
    }

    [Theory, ModeData]
    public void IncomingMessage_Destroy_StopsBodyConsumptionAndMarksAborted(ExecutionMode mode)
    {
        var port = TestPorts.GetAvailablePort();
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as http from 'http';
                const server: any = http.createServer((req: any, res: any) => {
                    let chunks = 0;
                    req.on('aborted', () => {
                        console.log('aborted=' + req.aborted);
                        console.log('complete=' + req.complete);
                        console.log('chunks=' + chunks);
                        server.close();
                    });
                    req.on('end', () => console.log('unexpected-end'));
                    req.on('error', () => console.log('unexpected-error'));
                    req.on('close', () => console.log('destroyed=' + req.destroyed));
                    req.on('data', () => {
                        chunks++;
                        if (chunks === 1) {
                            res.statusCode = 413;
                            res.end('too large');
                            req.destroy();
                        }
                    });
                });
                server.listen({{port}}, async () => {
                    try {
                        await fetch('http://127.0.0.1:{{port}}/', {
                            method: 'POST',
                            body: 'x'.repeat(50000)
                        });
                    } catch {
                        // destroy() may close the upload before the client finishes sending.
                    }
                });
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("aborted=true", output);
        Assert.Contains("complete=false", output);
        Assert.Contains("chunks=1", output);
        Assert.Contains("destroyed=true", output);
        Assert.DoesNotContain("unexpected-end", output);
        Assert.DoesNotContain("unexpected-error", output);
    }
}

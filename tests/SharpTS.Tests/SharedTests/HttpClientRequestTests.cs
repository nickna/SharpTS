using System.Net;
using System.Net.Sockets;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for the real http.ClientRequest object model (#1043) and its response event
/// lifecycle + IncomingMessage (#1044).
/// </summary>
/// <remarks>
/// http.request/http.get return a writable ClientRequest whose 'response' fires with a
/// streaming IncomingMessage. This is implemented for the interpreter; the compiled path
/// still routes the client through fetch (a documented follow-up — emitting a full
/// $ClientRequest IL object model), so these object-model tests run interpreted-only.
/// The dual-mode server tests in HttpEventTests still exercise http.get in compiled mode.
/// </remarks>
public class HttpClientRequestTests
{
    [Theory, InterpretedOnlyData]
    public void ClientRequest_IsWritable_WithHeaderManipulation(ExecutionMode mode)
    {
        var port = TestPorts.GetAvailablePort();
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as http from 'http';
                const server = http.createServer((req: any, res: any) => {
                    res.end('ok');
                    server.close();
                });
                server.listen({{port}}, () => {
                    const req: any = http.request({ host: '127.0.0.1', port: {{port}}, path: '/', method: 'POST' });
                    console.log('write=' + (typeof req.write));
                    console.log('end=' + (typeof req.end));
                    console.log('method=' + req.method);
                    console.log('path=' + req.path);
                    console.log('protocol=' + req.protocol);
                    req.setHeader('X-Test', 'abc');
                    console.log('getHeader=' + req.getHeader('X-Test'));
                    console.log('hasHeader=' + req.hasHeader('x-test'));
                    console.log('names=' + req.getHeaderNames().join(','));
                    req.removeHeader('X-Test');
                    console.log('afterRemove=' + req.hasHeader('X-Test'));
                    req.end();
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("write=function", output);
        Assert.Contains("end=function", output);
        Assert.Contains("method=POST", output);
        Assert.Contains("path=/", output);
        Assert.Contains("protocol=http:", output);
        Assert.Contains("getHeader=abc", output);
        Assert.Contains("hasHeader=true", output);
        Assert.Contains("names=x-test", output);
        Assert.Contains("afterRemove=false", output);
    }

    [Theory, InterpretedOnlyData]
    public void ClientRequest_PostStreamedBody_ResponseCallback(ExecutionMode mode)
    {
        var port = TestPorts.GetAvailablePort();
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as http from 'http';
                const server = http.createServer((req: any, res: any) => {
                    let body = '';
                    req.on('data', (c: any) => { body += c.toString(); });
                    req.on('end', () => {
                        res.writeHead(201, { 'Content-Type': 'text/plain', 'X-Echo': req.method });
                        res.end('got:' + body);
                    });
                });
                server.listen({{port}}, () => {
                    const req: any = http.request(
                        { host: '127.0.0.1', port: {{port}}, path: '/x', method: 'POST',
                          headers: { 'Content-Type': 'text/plain' } },
                        (res: any) => {
                            console.log('status=' + res.statusCode);
                            console.log('statusMessage=' + res.statusMessage);
                            console.log('echo=' + res.headers['x-echo']);
                            console.log('httpVersion=' + res.httpVersion);
                            console.log('rawHasContentType=' + (res.rawHeaders.indexOf('Content-Type') >= 0));
                            let data = '';
                            res.on('data', (c: any) => { data += c.toString(); });
                            res.on('end', () => {
                                console.log('body=' + data);
                                console.log('complete=' + res.complete);
                                server.close();
                            });
                        });
                    req.write('hello ');
                    req.write('world');
                    req.end();
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("status=201", output);
        Assert.Contains("statusMessage=Created", output);
        Assert.Contains("echo=POST", output);
        Assert.Contains("httpVersion=1.1", output);
        Assert.Contains("rawHasContentType=true", output);
        Assert.Contains("body=got:hello world", output);
        Assert.Contains("complete=true", output);
    }

    [Theory, InterpretedOnlyData]
    public void ClientRequest_ResponseEvent_FiresWithIncomingMessage(ExecutionMode mode)
    {
        var port = TestPorts.GetAvailablePort();
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as http from 'http';
                const server = http.createServer((req: any, res: any) => {
                    res.statusCode = 200;
                    res.end('hi');
                });
                server.listen({{port}}, () => {
                    const req: any = http.get('http://127.0.0.1:{{port}}/');
                    req.on('response', (res: any) => {
                        console.log('got response event ' + res.statusCode);
                        res.on('data', () => {});
                        res.on('end', () => { server.close(); });
                    });
                    req.on('socket', () => { console.log('socket event'); });
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("socket event", output);
        Assert.Contains("got response event 200", output);
    }

    [Theory, InterpretedOnlyData]
    public void ClientRequest_ConnectionFailure_EmitsError(ExecutionMode mode)
    {
        // Nothing listening on this port → ECONNREFUSED → 'error' event.
        var port = TestPorts.GetAvailablePort();
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as http from 'http';
                const req: any = http.get('http://127.0.0.1:{{port}}/');
                req.on('error', (err: any) => {
                    console.log('error event');
                    console.log('hasCode=' + (typeof err.code === 'string'));
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("error event", output);
        Assert.Contains("hasCode=true", output);
    }

    [Theory, InterpretedOnlyData]
    public void ClientRequest_GetHeaders_ReturnsObject(ExecutionMode mode)
    {
        var port = TestPorts.GetAvailablePort();
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as http from 'http';
                const server = http.createServer((req: any, res: any) => { res.end(); server.close(); });
                server.listen({{port}}, () => {
                    const req: any = http.request({ host: '127.0.0.1', port: {{port}}, path: '/' });
                    req.setHeader('Accept', 'application/json');
                    req.setHeader('X-A', '1');
                    const h = req.getHeaders();
                    console.log('accept=' + h.accept);
                    console.log('xa=' + h['x-a']);
                    req.end();
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("accept=application/json", output);
        Assert.Contains("xa=1", output);
    }
}

using System.Net;
using System.Net.Sockets;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for ServerResponse completeness (#1047): writeContinue/writeProcessing/addTrailers/
/// flushHeaders, writable statusMessage, sendDate, chunked streaming, and write() returning a
/// boolean. The server response object ($HttpResponse) is dual-mode; body content is read back
/// with fetch (also dual-mode).
/// </summary>
public class HttpServerResponseTests
{
    [Theory, ModeData]
    public void ServerResponse_StatusMessage_Writable(ExecutionMode mode)
    {
        var port = TestPorts.GetAvailablePort();
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as http from 'http';
                const server: any = http.createServer((req: any, res: any) => {
                    res.statusMessage = 'Custom OK';
                    console.log('msg=' + res.statusMessage);
                    res.end('done');
                    server.close();
                });
                server.listen({{port}}, () => { http.get('http://127.0.0.1:{{port}}/'); });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("msg=Custom OK", output);
    }

    [Theory, ModeData]
    public void ServerResponse_ExtraMethods_Callable(ExecutionMode mode)
    {
        var port = TestPorts.GetAvailablePort();
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as http from 'http';
                const server: any = http.createServer((req: any, res: any) => {
                    console.log('wc=' + (typeof res.writeContinue));
                    console.log('wp=' + (typeof res.writeProcessing));
                    console.log('at=' + (typeof res.addTrailers));
                    console.log('fh=' + (typeof res.flushHeaders));
                    console.log('sd=' + res.sendDate);
                    res.writeContinue();
                    res.writeProcessing();
                    res.addTrailers({ 'X-Trailer': 'v' });
                    res.flushHeaders();
                    console.log('all-ok');
                    res.end('done');
                    server.close();
                });
                server.listen({{port}}, () => { http.get('http://127.0.0.1:{{port}}/'); });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("wc=function", output);
        Assert.Contains("wp=function", output);
        Assert.Contains("at=function", output);
        Assert.Contains("fh=function", output);
        Assert.Contains("sd=true", output);
        Assert.Contains("all-ok", output);
    }

    [Theory, ModeData]
    public void ServerResponse_Write_ReturnsBoolean(ExecutionMode mode)
    {
        var port = TestPorts.GetAvailablePort();
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as http from 'http';
                const server: any = http.createServer((req: any, res: any) => {
                    const r = res.write('chunk');
                    console.log('writeType=' + (typeof r));
                    res.end();
                    server.close();
                });
                server.listen({{port}}, () => { http.get('http://127.0.0.1:{{port}}/'); });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("writeType=boolean", output);
    }

    [Theory, ModeData]
    public void ServerResponse_ChunkedStreaming_BodyReceived(ExecutionMode mode)
    {
        var port = TestPorts.GetAvailablePort();
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = $$"""
                import * as http from 'http';
                const server: any = http.createServer((req: any, res: any) => {
                    res.write('Hello ');
                    res.write('World');
                    res.end('!');
                });
                server.listen({{port}}, async () => {
                    const r = await fetch('http://127.0.0.1:{{port}}/');
                    const t = await r.text();
                    console.log('body=' + t);
                    server.close();
                });
                """
        };
        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Contains("body=Hello World!", output);
    }
}

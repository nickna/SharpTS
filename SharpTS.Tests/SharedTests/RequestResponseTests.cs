using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for standalone Request and Response constructors and static methods.
/// Tests run against both interpreter and compiler modes.
/// </summary>
public class RequestResponseTests
{
    // ========== Response Constructor Tests ==========

    [Theory, ModeData]
    public void Response_DefaultConstructor(ExecutionMode mode)
    {
        var source = @"
            const r = new Response();
            console.log(r.status);
            console.log(r.ok);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("200\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Response_StringBody(ExecutionMode mode)
    {
        var source = @"
            async function main() {
                const r = new Response(""hello"");
                const text = await r.text();
                console.log(text);
            }
            main();
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello\n", output);
    }

    [Theory, ModeData]
    public void Response_NullBody(ExecutionMode mode)
    {
        var source = @"
            async function main() {
                const r = new Response(null);
                const text = await r.text();
                console.log(text);
            }
            main();
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("\n", output);
    }

    [Theory, ModeData]
    public void Response_CustomStatus(ExecutionMode mode)
    {
        var source = @"
            const r = new Response("""", { status: 404, statusText: ""Not Found"" });
            console.log(r.status);
            console.log(r.statusText);
            console.log(r.ok);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("404\nNot Found\nfalse\n", output);
    }

    [Theory, ModeData]
    public void Response_CustomHeaders(ExecutionMode mode)
    {
        var source = @"
            const r = new Response("""", { headers: { ""X-Custom"": ""val"" } });
            console.log(r.headers.get(""x-custom""));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("val\n", output);
    }

    [Theory, ModeData]
    public void Response_BodyUsed(ExecutionMode mode)
    {
        var source = @"
            async function main() {
                const r = new Response(""data"");
                console.log(r.bodyUsed);
                await r.text();
                console.log(r.bodyUsed);
            }
            main();
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Response_Json(ExecutionMode mode)
    {
        var source = @"
            async function main() {
                const r = new Response('{""a"":1}');
                const data = await r.json();
                console.log(data.a);
            }
            main();
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n", output);
    }

    [Theory, ModeData]
    public void Response_ArrayBuffer(ExecutionMode mode)
    {
        var source = @"
            async function main() {
                const r = new Response(""hello"");
                const buf = await r.arrayBuffer();
                console.log(buf.length);
            }
            main();
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\n", output);
    }

    [Theory, ModeData]
    public void Response_Clone(ExecutionMode mode)
    {
        var source = @"
            async function main() {
                const r = new Response(""data"");
                const r2 = r.clone();
                const t1 = await r.text();
                const t2 = await r2.text();
                console.log(t1);
                console.log(t2);
            }
            main();
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("data\ndata\n", output);
    }

    // ========== Response Static Method Tests ==========

    [Theory, ModeData]
    public void Response_Json_Static(ExecutionMode mode)
    {
        var source = @"
            async function main() {
                const r = Response.json({ key: ""value"" });
                console.log(r.status);
                console.log(r.headers.get(""content-type""));
                const data = await r.json();
                console.log(data.key);
            }
            main();
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("200\napplication/json\nvalue\n", output);
    }

    [Theory, ModeData]
    public void Response_Json_WithInit(ExecutionMode mode)
    {
        var source = @"
            const r = Response.json({ ok: true }, { status: 201 });
            console.log(r.status);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("201\n", output);
    }

    [Theory, ModeData]
    public void Response_Redirect(ExecutionMode mode)
    {
        var source = @"
            const r = Response.redirect(""https://example.com"");
            console.log(r.status);
            console.log(r.headers.get(""location""));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("302\nhttps://example.com\n", output);
    }

    [Theory, ModeData]
    public void Response_Redirect_CustomStatus(ExecutionMode mode)
    {
        var source = @"
            const r = Response.redirect(""https://example.com"", 301);
            console.log(r.status);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("301\n", output);
    }

    [Theory, ModeData]
    public void Response_Error(ExecutionMode mode)
    {
        var source = @"
            const r = Response.error();
            console.log(r.status);
            console.log(r.type);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\nerror\n", output);
    }

    // ========== Request Constructor Tests ==========

    [Theory, ModeData]
    public void Request_BasicGet(ExecutionMode mode)
    {
        var source = @"
            const req = new Request(""https://example.com"");
            console.log(req.method);
            console.log(req.url);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("GET\nhttps://example.com\n", output);
    }

    [Theory, ModeData]
    public void Request_PostMethod(ExecutionMode mode)
    {
        var source = @"
            const req = new Request(""https://example.com"", { method: ""POST"" });
            console.log(req.method);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("POST\n", output);
    }

    [Theory, ModeData]
    public void Request_CustomHeaders(ExecutionMode mode)
    {
        var source = @"
            const req = new Request(""https://example.com"", {
                headers: { ""Authorization"": ""Bearer token123"" }
            });
            console.log(req.headers.get(""authorization""));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("Bearer token123\n", output);
    }

    [Theory, ModeData]
    public void Request_Body(ExecutionMode mode)
    {
        var source = @"
            async function main() {
                const req = new Request(""https://example.com"", {
                    method: ""POST"",
                    body: ""hello world""
                });
                const text = await req.text();
                console.log(text);
            }
            main();
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("hello world\n", output);
    }

    [Theory, ModeData]
    public void Request_Clone(ExecutionMode mode)
    {
        var source = @"
            const req = new Request(""https://example.com"", { method: ""POST"" });
            const req2 = req.clone();
            console.log(req2.method);
            console.log(req2.url);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("POST\nhttps://example.com\n", output);
    }

    [Theory, ModeData]
    public void Request_Properties(ExecutionMode mode)
    {
        var source = @"
            const req = new Request(""https://example.com"", { method: ""PUT"" });
            console.log(req.url);
            console.log(req.method);
            console.log(req.bodyUsed);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("https://example.com\nPUT\nfalse\n", output);
    }
}

using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// Integration tests for fetch API using a mock HTTP server.
/// Tests both compiled and interpreted modes for parity.
/// </summary>
public class FetchIntegrationTests : IDisposable
{
    private readonly MockHttpServer _server;

    public FetchIntegrationTests()
    {
        _server = new MockHttpServer();

        // Configure routes
        _server.AddJsonRoute("/json", new { message = "Hello", count = 42 });
        _server.AddTextRoute("/text", "Hello, World!");
        _server.AddEchoRoute("/echo");
        _server.AddBinaryRoute("/binary", new byte[] { 0x48, 0x65, 0x6c, 0x6c, 0x6f }); // "Hello"
        _server.AddStatusRoute("/status/404", 404, "Not Found");
        _server.AddStatusRoute("/status/500", 500, "Server Error");

        _server.Start();
    }

    public void Dispose()
    {
        _server.Dispose();
    }

    [Fact]
    public void FetchJson_Compiled()
    {
        var source = $$"""
            async function test(): Promise<void> {
                const res = await fetch('{{_server.BaseUrl}}json');
                const data = await res.json();
                console.log(data.message);
                console.log(data.count);
            }
            test();
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Hello\n42\n", output);
    }

    [Fact]
    public void FetchJson_Interpreted()
    {
        var source = $$"""
            async function test(): Promise<void> {
                const res = await fetch('{{_server.BaseUrl}}json');
                const data = await res.json();
                console.log(data.message);
                console.log(data.count);
            }
            test();
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Hello\n42\n", output);
    }

    [Fact]
    public void FetchText_Compiled()
    {
        var source = $$"""
            async function test(): Promise<void> {
                const res = await fetch('{{_server.BaseUrl}}text');
                const text = await res.text();
                console.log(text);
            }
            test();
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("Hello, World!\n", output);
    }

    [Fact]
    public void FetchText_Interpreted()
    {
        var source = $$"""
            async function test(): Promise<void> {
                const res = await fetch('{{_server.BaseUrl}}text');
                const text = await res.text();
                console.log(text);
            }
            test();
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("Hello, World!\n", output);
    }

    [Fact]
    public void FetchPost_Compiled()
    {
        var source = $$"""
            async function test(): Promise<void> {
                const res = await fetch('{{_server.BaseUrl}}echo', {
                    method: 'POST',
                    body: 'test body'
                });
                const data = await res.json();
                console.log(data.method);
                console.log(data.body);
            }
            test();
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("POST\ntest body\n", output);
    }

    [Fact]
    public void FetchPost_Interpreted()
    {
        var source = $$"""
            async function test(): Promise<void> {
                const res = await fetch('{{_server.BaseUrl}}echo', {
                    method: 'POST',
                    body: 'test body'
                });
                const data = await res.json();
                console.log(data.method);
                console.log(data.body);
            }
            test();
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("POST\ntest body\n", output);
    }

    [Fact]
    public void FetchWithCustomHeaders_Compiled()
    {
        var source = $$"""
            async function test(): Promise<void> {
                const res = await fetch('{{_server.BaseUrl}}echo', {
                    method: 'GET',
                    headers: {
                        'X-Custom-Header': 'CustomValue',
                        'Accept': 'application/json'
                    }
                });
                const data = await res.json();
                console.log(data.headers['X-Custom-Header']);
            }
            test();
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("CustomValue\n", output);
    }

    [Fact]
    public void FetchWithCustomHeaders_Interpreted()
    {
        var source = $$"""
            async function test(): Promise<void> {
                const res = await fetch('{{_server.BaseUrl}}echo', {
                    method: 'GET',
                    headers: {
                        'X-Custom-Header': 'CustomValue',
                        'Accept': 'application/json'
                    }
                });
                const data = await res.json();
                console.log(data.headers['X-Custom-Header']);
            }
            test();
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("CustomValue\n", output);
    }

    [Fact]
    public void FetchResponseStatus_Compiled()
    {
        var source = $$"""
            async function test(): Promise<void> {
                const res = await fetch('{{_server.BaseUrl}}status/404');
                console.log(res.status);
                console.log(res.ok);
            }
            test();
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("404\nfalse\n", output);
    }

    [Fact]
    public void FetchResponseStatus_Interpreted()
    {
        var source = $$"""
            async function test(): Promise<void> {
                const res = await fetch('{{_server.BaseUrl}}status/404');
                console.log(res.status);
                console.log(res.ok);
            }
            test();
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("404\nfalse\n", output);
    }

    [Fact]
    public void FetchArrayBuffer_Compiled()
    {
        var source = $$"""
            async function test(): Promise<void> {
                const res = await fetch('{{_server.BaseUrl}}binary');
                const buffer = await res.arrayBuffer();
                console.log(buffer.length);
                console.log(buffer.toString('utf8'));
            }
            test();
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("5\nHello\n", output);
    }

    [Fact]
    public void FetchArrayBuffer_Interpreted()
    {
        var source = $$"""
            async function test(): Promise<void> {
                const res = await fetch('{{_server.BaseUrl}}binary');
                const buffer = await res.arrayBuffer();
                console.log(buffer.length);
                console.log(buffer.toString('utf8'));
            }
            test();
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("5\nHello\n", output);
    }

    [Fact]
    public void FetchResponseHeaders_Compiled()
    {
        var source = $$"""
            async function test(): Promise<void> {
                const res = await fetch('{{_server.BaseUrl}}json');
                // Content-Type header should be present
                console.log(typeof res.headers);
            }
            test();
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("object\n", output);
    }

    [Fact]
    public void FetchResponseHeaders_Interpreted()
    {
        var source = $$"""
            async function test(): Promise<void> {
                const res = await fetch('{{_server.BaseUrl}}json');
                // Content-Type header should be present
                console.log(typeof res.headers);
                console.log(res.headers['content-type']);
            }
            test();
            """;
        var output = TestHarness.RunInterpreted(source);
        Assert.Contains("object\n", output);
        Assert.Contains("application/json", output);
    }

    [Fact]
    public void FetchPutMethod_Compiled()
    {
        var source = $$"""
            async function test(): Promise<void> {
                const res = await fetch('{{_server.BaseUrl}}echo', {
                    method: 'PUT',
                    body: 'update data'
                });
                const data = await res.json();
                console.log(data.method);
            }
            test();
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("PUT\n", output);
    }

    [Fact]
    public void FetchDeleteMethod_Compiled()
    {
        var source = $$"""
            async function test(): Promise<void> {
                const res = await fetch('{{_server.BaseUrl}}echo', {
                    method: 'DELETE'
                });
                const data = await res.json();
                console.log(data.method);
            }
            test();
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("DELETE\n", output);
    }
}

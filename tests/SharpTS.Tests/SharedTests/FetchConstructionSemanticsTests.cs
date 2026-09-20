using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class FetchConstructionSemanticsTests
{
    [Theory]
    [InlineData("interpreted")]
    [InlineData("compiled")]
    [InlineData("standalone")]
    public void HeadersInputsAreIndependentCopies(string mode)
    {
        const string source = """
            const original = new Headers({'x-tag': 'one'});
            original.append('x-tag', 'two');
            original.append('set-cookie', 'a=1');
            original.append('set-cookie', 'b=2');
            const copied = new Headers(original);
            const request = new Request('http://example.test', {headers: original});
            const response = new Response(null, {headers: original});
            const jsonResponse = Response.json({value: 1}, {headers: original});
            const requestClone = request.clone();
            const responseClone = response.clone();
            original.append('x-tag', 'three');
            original.append('set-cookie', 'c=3');
            console.log(copied.get('X-Tag'));
            console.log(request.headers.get('x-tag'));
            console.log(response.headers.get('x-tag'));
            console.log(copied.getSetCookie().length);
            console.log(request.headers.getSetCookie().length);
            console.log(response.headers.getSetCookie().length);
            copied.append('x-tag', 'four');
            console.log(original.get('x-tag'));
            request.headers.append('x-tag', 'request');
            response.headers.append('x-tag', 'response');
            console.log(requestClone.headers.get('x-tag'));
            console.log(responseClone.headers.get('x-tag'));
            console.log(jsonResponse.headers.get('x-tag'));
            console.log(request.method);
            console.log(new Request('http://example.test', {method: undefined}).method);
            """;
        var output = mode == "standalone" ? TestHarness.RunCompiledStandalone(source)
            : TestHarness.Run(source, mode == "compiled" ? ExecutionMode.Compiled : ExecutionMode.Interpreted);
        Assert.Equal("one, two\none, two\none, two\n2\n2\n2\none, two, three\none, two\none, two\none, two\nGET\nGET\n", output);
    }

    [Fact]
    public void StandaloneFetchDefaultsMissingAndUndefinedMethods()
    {
        using var server = new MockHttpServer();
        server.AddRedirectRoute("/redirect", "/target", 302);
        server.Start();
        var source = $$"""
            async function main() {
                const a = await fetch('{{server.BaseUrl}}redirect', {redirect: 'manual'});
                const b = await fetch('{{server.BaseUrl}}redirect', {redirect: 'manual', method: undefined});
                console.log(a.status);
                console.log(a.headers.get('location'));
                console.log(b.status);
            }
            main();
            """;
        Assert.Equal($"302\n{server.BaseUrl}target\n302\n", TestHarness.RunCompiledStandalone(source));
    }
}

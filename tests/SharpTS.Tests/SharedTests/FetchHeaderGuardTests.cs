using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class FetchHeaderGuardTests
{
    private const string Program = """
        function check(headers) {
            try { headers.append('x-tag', 'changed'); console.log('mutable'); } catch(e) { console.log(e.name); }
            try { headers.set('x-tag', 'changed'); console.log('mutable'); } catch(e) { console.log(e.name); }
            try { headers.delete('x-tag'); console.log('mutable'); } catch(e) { console.log(e.name); }
            try { headers.delete('absent'); console.log('mutable'); } catch(e) { console.log(e.name); }
        }
        async function main() {
            const response = await fetch('URL');
            const clone = response.clone();
            console.log(response.headers === response.headers);
            console.log(clone.headers === clone.headers);
            console.log(response.headers === clone.headers);
            check(response.headers);
            check(clone.headers);
            console.log(response.headers.get('content-type'));
            const copy = new Headers(response.headers);
            copy.set('x-copy', 'yes');
            const request = new Request('http://example.test', {headers: response.headers});
            request.headers.append('x-copy', 'yes');
            const constructed = new Response(null, {headers: clone.headers});
            constructed.headers.set('x-copy', 'yes');
            console.log(copy.get('x-copy'));
            console.log(request.headers.get('x-copy'));
            console.log(constructed.headers.get('x-copy'));
            console.log(response.headers.get('x-copy'));
        }
        main();
        """;

    [Theory]
    [InlineData("interpreted")]
    [InlineData("compiled")]
    [InlineData("standalone")]
    public void FetchedHeadersAreStableAndImmutableButCopiesAreMutable(string mode)
    {
        using var server = new MockHttpServer();
        server.AddTextRoute("/body", "body");
        server.Start();
        var source = Program.Replace("URL", server.BaseUrl + "body");
        var output = mode == "standalone" ? TestHarness.RunCompiledStandalone(source)
            : TestHarness.Run(source, mode == "compiled" ? ExecutionMode.Compiled : ExecutionMode.Interpreted);
        Assert.Equal("true\ntrue\nfalse\n" + string.Concat(Enumerable.Repeat("TypeError\n", 8))
            + "text/plain\nyes\nyes\nyes\nnull\n", output);
    }
}

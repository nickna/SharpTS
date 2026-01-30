using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// Tests for HTTP module and fetch API in compiled mode.
/// </summary>
public class HttpModuleTests
{
    [Fact]
    public void FetchIsGlobal()
    {
        // Test that fetch is available as a global
        var source = """
            console.log(typeof fetch);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("function\n", output);
    }

    [Fact]
    public void FetchReturnsPromise()
    {
        // Test that fetch returns something with .then
        var source = """
            const p = fetch('http://example.com');
            console.log(typeof p.then);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("function\n", output);
    }

    [Fact]
    public void HttpModuleImport()
    {
        // Test that http module can be imported
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as http from 'http';
                console.log(typeof http.createServer);
                """
        };
        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("function\n", output);
    }

    [Fact]
    public void HttpModuleExports()
    {
        // Test http module exports - use typeof instead of 'in' operator
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as http from 'http';
                console.log(typeof http.createServer !== 'undefined');
                console.log(typeof http.request !== 'undefined');
                console.log(typeof http.METHODS !== 'undefined');
                console.log(typeof http.STATUS_CODES !== 'undefined');
                """
        };
        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("true\ntrue\ntrue\ntrue\n", output);
    }

    [Fact]
    public void HttpCreateServer()
    {
        // Test creating a server (without starting it)
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as http from 'http';
                const server = http.createServer((req: any, res: any) => {
                    res.end('OK');
                });
                console.log(server.listening);
                """
        };
        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("false\n", output);
    }

    [Fact]
    public void HttpStatusCodes()
    {
        // Test STATUS_CODES constant
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as http from 'http';
                console.log(http.STATUS_CODES['200']);
                console.log(http.STATUS_CODES['404']);
                console.log(http.STATUS_CODES['500']);
                """
        };
        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("OK\nNot Found\nInternal Server Error\n", output);
    }

    [Fact]
    public void HttpMethods()
    {
        // Test METHODS constant
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as http from 'http';
                console.log(http.METHODS.includes('GET'));
                console.log(http.METHODS.includes('POST'));
                console.log(http.METHODS.includes('DELETE'));
                """
        };
        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Fact]
    public void HttpGlobalAgent()
    {
        // Test globalAgent object
        var files = new Dictionary<string, string>
        {
            ["./main.ts"] = """
                import * as http from 'http';
                console.log(typeof http.globalAgent);
                console.log(http.globalAgent.keepAlive);
                """
        };
        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("object\ntrue\n", output);
    }

    [Fact]
    public void GlobalThisHasFetch()
    {
        // Test that fetch is accessible via globalThis
        var source = """
            console.log(typeof globalThis.fetch);
            """;
        var output = TestHarness.RunCompiled(source);
        Assert.Equal("function\n", output);
    }
}

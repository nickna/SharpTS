using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests.BuiltInModules;

/// <summary>
/// Tests for the built-in 'url' module in compiled mode.
/// </summary>
public class UrlModuleTests
{
    [Fact]
    public void Url_Parse_ParsesFullUrl()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { parse } from 'url';
                const parsed = parse('https://example.com:8080/path?query=value#hash');
                console.log(parsed.protocol);
                console.log(parsed.hostname);
                console.log(parsed.port);
                console.log(parsed.pathname);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("https:", output);
        Assert.Contains("example.com", output);
        Assert.Contains("8080", output);
        Assert.Contains("/path", output);
    }

    [Fact]
    public void Url_Parse_ParsesQueryString()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { parse } from 'url';
                const parsed = parse('https://example.com?foo=bar');
                console.log(parsed.search);
                console.log(parsed.query);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("?foo=bar", output);
        Assert.Contains("foo=bar", output);
    }

    [Fact]
    public void Url_Parse_ParsesHash()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { parse } from 'url';
                const parsed = parse('https://example.com#section');
                console.log(parsed.hash);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("#section", output);
    }

    [Fact]
    public void Url_Parse_HandlesDefaultPort()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { parse } from 'url';
                const parsed = parse('https://example.com/path');
                console.log(parsed.port === null);
                console.log(parsed.hostname);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("true", output);
        Assert.Contains("example.com", output);
    }

    [Fact]
    public void Url_Format_CreatesUrlString()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { format } from 'url';
                const formatted = format({
                    protocol: 'https:',
                    hostname: 'example.com',
                    pathname: '/path',
                    search: '?key=value'
                });
                console.log(formatted);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("https://example.com/path?key=value", output);
    }

    [Fact]
    public void Url_Format_HandlesPort()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { format } from 'url';
                const formatted = format({
                    protocol: 'http:',
                    hostname: 'localhost',
                    port: '3000',
                    pathname: '/api'
                });
                console.log(formatted);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("localhost:3000", output);
    }

    [Fact]
    public void Url_Resolve_ResolvesRelativeUrl()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { resolve } from 'url';
                const resolved = resolve('https://example.com/base/', '../other/path');
                console.log(resolved);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("example.com/other/path", output);
    }

    [Fact]
    public void Url_Resolve_ResolvesAbsoluteUrl()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { resolve } from 'url';
                const resolved = resolve('https://example.com/base/', '/absolute');
                console.log(resolved);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("example.com/absolute", output);
    }

    [Fact]
    public void Url_Resolve_KeepsFullUrl()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { resolve } from 'url';
                const resolved = resolve('https://example.com/', 'https://other.com/path');
                console.log(resolved);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("other.com/path", output);
    }

    [Fact]
    public void Url_NamespaceImport_Works()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as url from 'url';
                const parsed = url.parse('https://example.com/path');
                console.log(parsed.hostname);
                const formatted = url.format({ protocol: 'http:', hostname: 'test.com', pathname: '/' });
                console.log(formatted.includes('test.com'));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("example.com", output);
        Assert.Contains("true", output);
    }

    [Fact]
    public void Url_ParseFormat_RoundTrip()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { parse, format } from 'url';
                const original = 'https://example.com/path?query=value';
                const parsed = parse(original);
                const formatted = format(parsed);
                console.log(formatted.includes('example.com'));
                console.log(formatted.includes('/path'));
                console.log(formatted.includes('query=value'));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Url_Parse_HandlesHttpProtocol()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { parse } from 'url';
                const parsed = parse('http://localhost:8080/api/users');
                console.log(parsed.protocol);
                console.log(parsed.hostname);
                console.log(parsed.port);
                console.log(parsed.pathname);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("http:", output);
        Assert.Contains("localhost", output);
        Assert.Contains("8080", output);
        Assert.Contains("/api/users", output);
    }

    [Fact]
    public void Url_Parse_HandlesFileProtocol()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { parse } from 'url';
                const parsed = parse('file:///home/user/file.txt');
                console.log(parsed.protocol);
                console.log(parsed.pathname.includes('file.txt'));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("file:", output);
        Assert.Contains("true", output);
    }
}

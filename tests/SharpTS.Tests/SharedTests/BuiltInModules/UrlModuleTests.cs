using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Tests for the built-in 'url' module across interpreter and compiled modes.
/// </summary>
public class UrlModuleTests
{
    [Theory, ModeData]
    public void Url_Parse_ParsesFullUrl(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("https:", output);
        Assert.Contains("example.com", output);
        Assert.Contains("8080", output);
        Assert.Contains("/path", output);
    }

    [Theory, ModeData]
    public void Url_Parse_ParsesQueryString(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("?foo=bar", output);
        Assert.Contains("foo=bar", output);
    }

    [Theory, ModeData]
    public void Url_Parse_ParsesHash(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { parse } from 'url';
                const parsed = parse('https://example.com#section');
                console.log(parsed.hash);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("#section", output);
    }

    [Theory, ModeData]
    public void Url_Parse_HandlesDefaultPort(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("true", output);
        Assert.Contains("example.com", output);
    }

    [Theory, ModeData]
    public void Url_Format_CreatesUrlString(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("https://example.com/path?key=value", output);
    }

    [Theory, ModeData]
    public void Url_Format_HandlesPort(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("localhost:3000", output);
    }

    [Theory, ModeData]
    public void Url_Resolve_ResolvesRelativeUrl(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { resolve } from 'url';
                const resolved = resolve('https://example.com/base/', '../other/path');
                console.log(resolved);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("example.com/other/path", output);
    }

    [Theory, ModeData]
    public void Url_Resolve_ResolvesAbsoluteUrl(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { resolve } from 'url';
                const resolved = resolve('https://example.com/base/', '/absolute');
                console.log(resolved);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("example.com/absolute", output);
    }

    [Theory, ModeData]
    public void Url_Resolve_KeepsFullUrl(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { resolve } from 'url';
                const resolved = resolve('https://example.com/', 'https://other.com/path');
                console.log(resolved);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("other.com/path", output);
    }

    [Theory, ModeData]
    public void Url_NamespaceImport_Works(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("example.com", output);
        Assert.Contains("true", output);
    }

    [Theory, ModeData]
    public void Url_ParseFormat_RoundTrip(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Url_Parse_HandlesHttpProtocol(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("http:", output);
        Assert.Contains("localhost", output);
        Assert.Contains("8080", output);
        Assert.Contains("/api/users", output);
    }

    [Theory, ModeData]
    public void Url_Parse_HandlesFileProtocol(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { parse } from 'url';
                const parsed = parse('file:///home/user/file.txt');
                console.log(parsed.protocol);
                console.log(parsed.pathname !== null && parsed.pathname.includes('file.txt'));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("file:", output);
        Assert.Contains("true", output);
    }
}

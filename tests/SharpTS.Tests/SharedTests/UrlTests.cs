using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for the URL and URLSearchParams classes exported from the 'url' stdlib module.
/// Tests run against both interpreter and compiler modes.
/// </summary>
/// <remarks>
/// URL and URLSearchParams migrated from C# globals to the TS stdlib module in
/// stdlib/node/url.ts. Tests that used to rely on them as implicit globals now
/// import them explicitly — same runtime behavior, different binding site.
/// </remarks>
public class UrlTests
{
    private static string RunWithUrlImport(string body, ExecutionMode mode)
    {
        var files = new System.Collections.Generic.Dictionary<string, string>
        {
            ["main.ts"] = "import { URL, URLSearchParams } from 'url';\n" + body
        };
        return TestHarness.RunModules(files, "main.ts", mode);
    }

    // ========== URL Constructor ==========

    [Theory, ModeData]
    public void URL_Constructor_ParsesFullUrl(ExecutionMode mode)
    {
        var body = @"
            const u = new URL(""https://example.com/path?q=1#hash"");
            console.log(u.href);
        ";
        var output = RunWithUrlImport(body, mode);
        Assert.Equal("https://example.com/path?q=1#hash\n", output);
    }

    [Theory, ModeData]
    public void URL_Constructor_WithBase(ExecutionMode mode)
    {
        var body = @"
            const u = new URL(""/path"", ""https://example.com"");
            console.log(u.href);
        ";
        var output = RunWithUrlImport(body, mode);
        Assert.Equal("https://example.com/path\n", output);
    }

    // ========== URL Properties ==========

    [Theory, ModeData]
    public void URL_Protocol(ExecutionMode mode)
    {
        var body = @"
            const u = new URL(""https://example.com"");
            console.log(u.protocol);
        ";
        var output = RunWithUrlImport(body, mode);
        Assert.Equal("https:\n", output);
    }

    [Theory, ModeData]
    public void URL_Host_DefaultPort(ExecutionMode mode)
    {
        var body = @"
            const u = new URL(""https://example.com/path"");
            console.log(u.host);
        ";
        var output = RunWithUrlImport(body, mode);
        Assert.Equal("example.com\n", output);
    }

    [Theory, ModeData]
    public void URL_Host_NonDefaultPort(ExecutionMode mode)
    {
        var body = @"
            const u = new URL(""https://example.com:8080/path"");
            console.log(u.host);
        ";
        var output = RunWithUrlImport(body, mode);
        Assert.Equal("example.com:8080\n", output);
    }

    [Theory, ModeData]
    public void URL_Hostname(ExecutionMode mode)
    {
        var body = @"
            const u = new URL(""https://example.com:8080/path"");
            console.log(u.hostname);
        ";
        var output = RunWithUrlImport(body, mode);
        Assert.Equal("example.com\n", output);
    }

    [Theory, ModeData]
    public void URL_Port_NonDefault(ExecutionMode mode)
    {
        var body = @"
            const u = new URL(""https://example.com:8080/path"");
            console.log(u.port);
        ";
        var output = RunWithUrlImport(body, mode);
        Assert.Equal("8080\n", output);
    }

    [Theory, ModeData]
    public void URL_Port_Default_Empty(ExecutionMode mode)
    {
        var body = @"
            const u = new URL(""https://example.com/path"");
            console.log(u.port);
        ";
        var output = RunWithUrlImport(body, mode);
        Assert.Equal("\n", output);
    }

    [Theory, ModeData]
    public void URL_Pathname(ExecutionMode mode)
    {
        var body = @"
            const u = new URL(""https://example.com/path/to/resource"");
            console.log(u.pathname);
        ";
        var output = RunWithUrlImport(body, mode);
        Assert.Equal("/path/to/resource\n", output);
    }

    [Theory, ModeData]
    public void URL_Search(ExecutionMode mode)
    {
        var body = @"
            const u = new URL(""https://example.com/path?q=1&r=2"");
            console.log(u.search);
        ";
        var output = RunWithUrlImport(body, mode);
        Assert.Equal("?q=1&r=2\n", output);
    }

    [Theory, ModeData]
    public void URL_Hash(ExecutionMode mode)
    {
        var body = @"
            const u = new URL(""https://example.com/path#section"");
            console.log(u.hash);
        ";
        var output = RunWithUrlImport(body, mode);
        Assert.Equal("#section\n", output);
    }

    [Theory, ModeData]
    public void URL_Origin(ExecutionMode mode)
    {
        var body = @"
            const u = new URL(""https://example.com:8080/path"");
            console.log(u.origin);
        ";
        var output = RunWithUrlImport(body, mode);
        Assert.Equal("https://example.com:8080\n", output);
    }

    // ========== URL Methods ==========

    [Theory, ModeData]
    public void URL_ToString(ExecutionMode mode)
    {
        var body = @"
            const u = new URL(""https://example.com/path"");
            console.log(u.toString());
        ";
        var output = RunWithUrlImport(body, mode);
        Assert.Equal("https://example.com/path\n", output);
    }

    [Theory, ModeData]
    public void URL_ToJSON(ExecutionMode mode)
    {
        var body = @"
            const u = new URL(""https://example.com/path"");
            console.log(u.toJSON());
        ";
        var output = RunWithUrlImport(body, mode);
        Assert.Equal("https://example.com/path\n", output);
    }

    // ========== URL.searchParams ==========

    [Theory, ModeData]
    public void URL_SearchParams_Get(ExecutionMode mode)
    {
        var body = @"
            const u = new URL(""https://example.com/path?q=hello&r=world"");
            console.log(u.searchParams.get(""q""));
            console.log(u.searchParams.get(""r""));
        ";
        var output = RunWithUrlImport(body, mode);
        Assert.Equal("hello\nworld\n", output);
    }

    // ========== URLSearchParams Constructor ==========

    [Theory, ModeData]
    public void URLSearchParams_Constructor_Empty(ExecutionMode mode)
    {
        var body = @"
            const sp = new URLSearchParams();
            console.log(sp.toString());
        ";
        var output = RunWithUrlImport(body, mode);
        Assert.Equal("\n", output);
    }

    [Theory, ModeData]
    public void URLSearchParams_Constructor_String(ExecutionMode mode)
    {
        var body = @"
            const sp = new URLSearchParams(""a=1&b=2"");
            console.log(sp.toString());
        ";
        var output = RunWithUrlImport(body, mode);
        Assert.Equal("a=1&b=2\n", output);
    }

    [Theory, ModeData]
    public void URLSearchParams_Constructor_Object(ExecutionMode mode)
    {
        var body = @"
            const sp = new URLSearchParams({a: ""1"", b: ""2""});
            console.log(sp.get(""a""));
            console.log(sp.get(""b""));
        ";
        var output = RunWithUrlImport(body, mode);
        Assert.Equal("1\n2\n", output);
    }

    // ========== URLSearchParams Methods ==========

    [Theory, ModeData]
    public void URLSearchParams_Get_ReturnsNull(ExecutionMode mode)
    {
        var body = @"
            const sp = new URLSearchParams(""a=1"");
            console.log(sp.get(""missing""));
        ";
        var output = RunWithUrlImport(body, mode);
        Assert.Equal("null\n", output);
    }

    [Theory, ModeData]
    public void URLSearchParams_Has(ExecutionMode mode)
    {
        var body = @"
            const sp = new URLSearchParams(""a=1&b=2"");
            console.log(sp.has(""a""));
            console.log(sp.has(""c""));
        ";
        var output = RunWithUrlImport(body, mode);
        Assert.Equal("true\nfalse\n", output);
    }

    [Theory, ModeData]
    public void URLSearchParams_Set(ExecutionMode mode)
    {
        var body = @"
            const sp = new URLSearchParams(""a=1"");
            sp.set(""a"", ""2"");
            console.log(sp.get(""a""));
        ";
        var output = RunWithUrlImport(body, mode);
        Assert.Equal("2\n", output);
    }

    [Theory, ModeData]
    public void URLSearchParams_Append(ExecutionMode mode)
    {
        var body = @"
            const sp = new URLSearchParams(""a=1"");
            sp.append(""a"", ""2"");
            sp.append(""b"", ""3"");
            console.log(sp.toString());
        ";
        var output = RunWithUrlImport(body, mode);
        Assert.Equal("a=1&a=2&b=3\n", output);
    }

    [Theory, ModeData]
    public void URLSearchParams_Delete(ExecutionMode mode)
    {
        var body = @"
            const sp = new URLSearchParams(""a=1&b=2&a=3"");
            sp.delete(""a"");
            console.log(sp.toString());
        ";
        var output = RunWithUrlImport(body, mode);
        Assert.Equal("b=2\n", output);
    }

    [Theory, ModeData]
    public void URLSearchParams_GetAll(ExecutionMode mode)
    {
        var body = @"
            const sp = new URLSearchParams(""a=1&b=2&a=3"");
            const all = sp.getAll(""a"");
            console.log(all.length);
            console.log(all[0]);
            console.log(all[1]);
        ";
        var output = RunWithUrlImport(body, mode);
        Assert.Equal("2\n1\n3\n", output);
    }

    [Theory, ModeData]
    public void URLSearchParams_Sort(ExecutionMode mode)
    {
        var body = @"
            const sp = new URLSearchParams(""c=3&a=1&b=2"");
            sp.sort();
            console.log(sp.toString());
        ";
        var output = RunWithUrlImport(body, mode);
        Assert.Equal("a=1&b=2&c=3\n", output);
    }

    [Theory, ModeData]
    public void URLSearchParams_Size(ExecutionMode mode)
    {
        var body = @"
            const sp = new URLSearchParams(""a=1&b=2&c=3"");
            console.log(sp.size);
        ";
        var output = RunWithUrlImport(body, mode);
        Assert.Equal("3\n", output);
    }

    [Theory, ModeData]
    public void URLSearchParams_ToString(ExecutionMode mode)
    {
        var body = @"
            const sp = new URLSearchParams(""hello=world&foo=bar"");
            console.log(sp.toString());
        ";
        var output = RunWithUrlImport(body, mode);
        Assert.Equal("hello=world&foo=bar\n", output);
    }

    // ========== Legacy parse() — relative URLs (issue #47) ==========

    private static string RunWithParseImport(string body, ExecutionMode mode)
    {
        var files = new System.Collections.Generic.Dictionary<string, string>
        {
            ["main.ts"] = "import { parse } from 'url';\n" + body
        };
        return TestHarness.RunModules(files, "main.ts", mode);
    }

    [Theory, ModeData]
    public void Parse_RelativeUrl_SplitsQueryString(ExecutionMode mode)
    {
        // Regression for #47: parse('/api/echo?k=v') used to return
        // pathname='/api/echo?k=v' with search=null because basicUrlParse
        // returns null for schemeless inputs and the fallback dropped the
        // whole string into pathname.
        var body = @"
            const r = parse('/api/echo?k=v');
            console.log('pathname:', r.pathname);
            console.log('search:', r.search);
            console.log('query:', r.query);
            console.log('hash:', r.hash);
            console.log('path:', r.path);
        ";
        var output = RunWithParseImport(body, mode);
        Assert.Equal(
            "pathname: /api/echo\nsearch: ?k=v\nquery: k=v\nhash: null\npath: /api/echo?k=v\n",
            output);
    }

    [Theory, ModeData]
    public void Parse_RelativeUrl_SplitsFragment(ExecutionMode mode)
    {
        var body = @"
            const r = parse('/a/b#frag');
            console.log('pathname:', r.pathname);
            console.log('hash:', r.hash);
            console.log('search:', r.search);
        ";
        var output = RunWithParseImport(body, mode);
        Assert.Equal("pathname: /a/b\nhash: #frag\nsearch: null\n", output);
    }

    [Theory, ModeData]
    public void Parse_RelativeUrl_SplitsBothQueryAndFragment(ExecutionMode mode)
    {
        var body = @"
            const r = parse('/a?x=1#f');
            console.log('pathname:', r.pathname);
            console.log('search:', r.search);
            console.log('query:', r.query);
            console.log('hash:', r.hash);
        ";
        var output = RunWithParseImport(body, mode);
        Assert.Equal("pathname: /a\nsearch: ?x=1\nquery: x=1\nhash: #f\n", output);
    }

    [Theory, ModeData]
    public void Parse_RelativeUrl_HashBeforeQuestionMarkIsFragment(ExecutionMode mode)
    {
        // Split order matters: a '?' inside a fragment is literal.
        var body = @"
            const r = parse('/a#frag?not-a-query');
            console.log('pathname:', r.pathname);
            console.log('hash:', r.hash);
            console.log('search:', r.search);
        ";
        var output = RunWithParseImport(body, mode);
        Assert.Equal("pathname: /a\nhash: #frag?not-a-query\nsearch: null\n", output);
    }

    [Theory, ModeData]
    public void Parse_EmptyString_DegenerateCase(ExecutionMode mode)
    {
        var body = @"
            const r = parse('');
            console.log('pathname:', r.pathname);
            console.log('search:', r.search);
            console.log('hash:', r.hash);
        ";
        var output = RunWithParseImport(body, mode);
        Assert.Equal("pathname: \nsearch: null\nhash: null\n", output);
    }

    [Theory, ModeData]
    public void Parse_AbsoluteUrl_StillSplitsCorrectly(ExecutionMode mode)
    {
        // Regression guard: the absolute-URL path must be unchanged.
        var body = @"
            const r = parse('http://host/api/echo?k=v');
            console.log('pathname:', r.pathname);
            console.log('search:', r.search);
            console.log('host:', r.host);
        ";
        var output = RunWithParseImport(body, mode);
        Assert.Equal("pathname: /api/echo\nsearch: ?k=v\nhost: host\n", output);
    }
}

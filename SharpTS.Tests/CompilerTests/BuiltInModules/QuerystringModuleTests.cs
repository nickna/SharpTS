using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests.BuiltInModules;

/// <summary>
/// Tests for the built-in 'querystring' module in compiled mode.
/// </summary>
public class QuerystringModuleTests
{
    [Fact]
    public void Querystring_Parse_ParsesSimpleString()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { parse } from 'querystring';
                const result = parse('foo=bar&baz=qux');
                console.log(result.foo);
                console.log(result.baz);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("bar\nqux\n", output);
    }

    [Fact]
    public void Querystring_Parse_HandlesUrlEncoding()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { parse } from 'querystring';
                const result = parse('name=John%20Doe&city=New%20York');
                console.log(result.name);
                console.log(result.city);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("John Doe\nNew York\n", output);
    }

    [Fact]
    public void Querystring_Parse_HandlesPlusAsSpace()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { parse } from 'querystring';
                const result = parse('name=John+Doe');
                console.log(result.name);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("John Doe\n", output);
    }

    [Fact]
    public void Querystring_Parse_HandlesEmptyValue()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { parse } from 'querystring';
                const result = parse('foo=&bar=value');
                console.log(result.foo === '');
                console.log(result.bar);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\nvalue\n", output);
    }

    [Fact]
    public void Querystring_Parse_CustomSeparator()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { parse } from 'querystring';
                const result = parse('foo=bar;baz=qux', ';');
                console.log(result.foo);
                console.log(result.baz);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("bar\nqux\n", output);
    }

    [Fact]
    public void Querystring_Stringify_CreatesQueryString()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { stringify } from 'querystring';
                const str = stringify({ foo: 'bar', baz: 'qux' });
                console.log(str.includes('foo=bar'));
                console.log(str.includes('baz=qux'));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Querystring_Stringify_EncodesSpecialChars()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { stringify } from 'querystring';
                const str = stringify({ name: 'hello world' });
                console.log(str);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("hello%20world", output);
    }

    [Fact]
    public void Querystring_Escape_EncodesString()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { escape } from 'querystring';
                console.log(escape('hello world'));
                console.log(escape('a=b&c=d'));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("hello%20world", output);
        Assert.Contains("%26", output);
    }

    [Fact]
    public void Querystring_Unescape_DecodesString()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { unescape } from 'querystring';
                console.log(unescape('hello%20world'));
                console.log(unescape('hello+world'));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("hello world\nhello world\n", output);
    }

    [Fact]
    public void Querystring_DecodeAlias_WorksLikeParse()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { decode } from 'querystring';
                const result = decode('foo=bar');
                console.log(result.foo);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("bar\n", output);
    }

    [Fact]
    public void Querystring_EncodeAlias_WorksLikeStringify()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { encode } from 'querystring';
                const str = encode({ key: 'value' });
                console.log(str);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("key=value\n", output);
    }

    [Fact]
    public void Querystring_NamespaceImport_Works()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as qs from 'querystring';
                const parsed = qs.parse('a=1');
                console.log(parsed.a);
                const str = qs.stringify({ b: '2' });
                console.log(str);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("1\nb=2\n", output);
    }

    [Fact]
    public void Querystring_RoundTrip_PreservesData()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { parse, stringify } from 'querystring';
                const original = { name: 'test', value: '123' };
                const encoded = stringify(original);
                const decoded = parse(encoded);
                console.log(decoded.name);
                console.log(decoded.value);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("test\n123\n", output);
    }
}

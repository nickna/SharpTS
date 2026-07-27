using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Tests for the built-in 'querystring' module across interpreter and compiled modes.
/// </summary>
public class QuerystringModuleTests
{
    [Theory, ModeData]
    public void Querystring_Parse_ParsesSimpleString(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("bar\nqux\n", output);
    }

    [Theory, ModeData]
    public void Querystring_Parse_HandlesUrlEncoding(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("John Doe\nNew York\n", output);
    }

    [Theory, ModeData]
    public void Querystring_Parse_HandlesPlusAsSpace(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { parse } from 'querystring';
                const result = parse('name=John+Doe');
                console.log(result.name);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("John Doe\n", output);
    }

    [Theory, ModeData]
    public void Querystring_Parse_HandlesEmptyValue(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\nvalue\n", output);
    }

    [Theory, ModeData]
    public void Querystring_Parse_CustomSeparator(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("bar\nqux\n", output);
    }

    [Theory, ModeData]
    public void Querystring_Stringify_CreatesQueryString(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Querystring_Stringify_EncodesSpecialChars(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { stringify } from 'querystring';
                const str = stringify({ name: 'hello world' });
                console.log(str);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("hello%20world", output);
    }

    [Theory, ModeData]
    public void Querystring_Escape_EncodesString(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { escape } from 'querystring';
                console.log(escape('hello world'));
                console.log(escape('a=b&c=d'));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("hello%20world", output);
        Assert.Contains("%26", output);
    }

    [Theory, ModeData]
    public void Querystring_Unescape_DecodesString(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { unescape } from 'querystring';
                console.log(unescape('hello%20world'));
                console.log(unescape('hello+world'));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("hello world\nhello world\n", output);
    }

    [Theory, ModeData]
    public void Querystring_DecodeAlias_WorksLikeParse(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { decode } from 'querystring';
                const result = decode('foo=bar');
                console.log(result.foo);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("bar\n", output);
    }

    [Theory, ModeData]
    public void Querystring_EncodeAlias_WorksLikeStringify(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { encode } from 'querystring';
                const str = encode({ key: 'value' });
                console.log(str);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("key=value\n", output);
    }

    [Theory, ModeData]
    public void Querystring_NamespaceImport_Works(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("1\nb=2\n", output);
    }

    [Theory, ModeData]
    public void Querystring_RoundTrip_PreservesData(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("test\n123\n", output);
    }
}

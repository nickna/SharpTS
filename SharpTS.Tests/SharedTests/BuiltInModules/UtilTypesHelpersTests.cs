using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Tests for the util module completeness additions (issue #1101): expanded
/// util.types.*, MIMEType/MIMEParams, abort helpers, parseEnv/getCallSites, and
/// the deprecated legacy aliases. Pure-TS facade, so interpreter == compiled.
/// </summary>
public class UtilTypesHelpersTests
{
    [Theory, ModeData]
    public void Types_ExpandedPredicates(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { types } from 'util';
                console.log(types.isAnyArrayBuffer(new ArrayBuffer(8)));
                console.log(types.isArrayBufferView(new Int8Array(4)));
                console.log(types.isUint8Array(new Uint8Array(4)));
                console.log(types.isDataView(new DataView(new ArrayBuffer(8))));
                console.log(types.isNumberObject(5));
                console.log(types.isProxy(new Proxy({}, {})));
                console.log(types.isModuleNamespaceObject({}));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        // Boxed-primitive/proxy/module-namespace predicates are honestly false
        // (no distinct representation / transparent in SharpTS).
        Assert.Equal("true\ntrue\ntrue\ntrue\nfalse\nfalse\nfalse\n", output);
    }

    [Theory, ModeData]
    public void MIMEType_Parses(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { MIMEType } from 'util';
                const m = new MIMEType('text/html;charset=utf-8');
                console.log(m.type);
                console.log(m.subtype);
                console.log(m.essence);
                console.log(m.params.get('charset'));
                console.log(m.params.has('charset'));
                console.log(m.toString());
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("text\nhtml\ntext/html\nutf-8\ntrue\ntext/html;charset=utf-8\n", output);
    }

    [Theory, ModeData]
    public void MIMEParams_SetGetDelete(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { MIMEType } from 'util';
                const m = new MIMEType('application/json');
                m.params.set('a', '1');
                m.params.set('b', '2');
                console.log(m.params.get('a'));
                m.params.delete('a');
                console.log(m.params.get('a'));
                console.log(m.params.has('b'));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("1\nnull\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Aborted_ResolvesOnAbort(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { aborted } from 'util';
                async function main(): Promise<void> {
                    const ac = new AbortController();
                    setTimeout(() => ac.abort(), 0);
                    await aborted(ac.signal, {});
                    console.log('resolved');
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("resolved\n", output);
    }

    [Theory, ModeData]
    public void Aborted_AlreadyAborted_ResolvesImmediately(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { aborted } from 'util';
                async function main(): Promise<void> {
                    const ac = new AbortController();
                    ac.abort();
                    await aborted(ac.signal);
                    console.log('resolved');
                }
                main();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("resolved\n", output);
    }

    [Theory, ModeData]
    public void ParseEnv_ParsesDotenv(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { parseEnv } from 'util';
                const env = parseEnv('FOO=bar\n# comment\nexport BAZ="qux"\nEMPTY=\n');
                console.log(env.FOO);
                console.log(env.BAZ);
                console.log(env.EMPTY === '');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("bar\nqux\ntrue\n", output);
    }

    [Theory, ModeData]
    public void LegacyAliases_Work(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as util from 'util';
                console.log(util.isArray([]));
                console.log(util.isBoolean(true));
                console.log(util.isNull(null));
                console.log(util.isNullOrUndefined(undefined));
                console.log(util.isNumber(1));
                console.log(util.isString('x'));
                console.log(util.isObject({}));
                console.log(util.isPrimitive('p'));
                console.log(util.isError(new Error('e')));
                console.log(util.isBuffer(Buffer.alloc(1)));
                console.log(JSON.stringify(util._extend({ a: 1 }, { b: 2 })));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\n{\"a\":1,\"b\":2}\n", output);
    }

    [Theory, ModeData]
    public void GetCallSites_ReturnsArray(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { getCallSites } from 'util';
                console.log(Array.isArray(getCallSites()));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }
}

using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Tests that CJS require() works for built-in modules in both interpreter and compiled modes.
/// These validate the EmitCjsBuiltInModuleObject code path in compiled mode,
/// which creates namespace objects for built-in modules accessed via require().
/// </summary>
public class CjsBuiltInModuleRequireTests
{
    [Theory, ModeData]
    public void Cjs_Require_Path_Join(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.cjs"] = """
                const path = require('path');
                console.log(typeof path.join);
                const result = path.join('foo', 'bar');
                console.log(result.includes('foo') && result.includes('bar'));
                """
        };
        var output = TestHarness.RunModules(files, "main.cjs", mode);
        Assert.Equal("function\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Cjs_Require_Buffer_Base64Helpers(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.cjs"] = """
                const buffer = require('node:buffer');
                const { atob: decode, btoa: encode } = buffer;
                console.log(typeof buffer.Buffer, typeof decode, typeof encode);
                console.log(decode === globalThis.atob, encode === globalThis.btoa);
                globalThis.atob = () => 'replacement';
                console.log(require('buffer').atob === decode, atob('YQ'));
                console.log(buffer.atob('YQ'), encode('ab'));
                console.log(decode(buffer.btoa('z')));
                try { decode('!'); }
                catch (error) { console.log(error.name, error.code); }
                """
        };
        Assert.Equal("function function function\ntrue true\ntrue replacement\na YWI=\nz\nInvalidCharacterError 5\n",
            TestHarness.RunModules(files, "main.cjs", mode));
    }

    [Theory, ModeData]
    public void Cjs_Require_Buffer_ValidationTranscodeAndAllocationHelpers(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.cjs"] = """
                const buffer = require('node:buffer');
                const { isUtf8, isAscii, transcode, SlowBuffer } = buffer;
                console.log(typeof isUtf8, typeof isAscii, typeof transcode, typeof SlowBuffer);
                const text = buffer.Buffer.from('héllo');
                console.log(buffer.isUtf8(text), isUtf8(buffer.Buffer.from([255])), isAscii(text));
                console.log(isAscii(buffer.Buffer.from('hello')), transcode(text, 'utf8', 'latin1').toString('hex'));
                const result = SlowBuffer(3);
                const { isBuffer } = buffer.Buffer;
                console.log(buffer.Buffer.from === Buffer.from, buffer.Buffer.alloc === Buffer.alloc,
                    buffer.Buffer.byteLength === Buffer.byteLength);
                console.log(typeof isBuffer, isBuffer.name, isBuffer.length);
                console.log(isBuffer(result), isBuffer(new Uint8Array(3)), isBuffer(null), result.length);
                console.log(isBuffer === Buffer.isBuffer, isBuffer === buffer.Buffer['isBuffer']);
                console.log(isBuffer(), isBuffer(undefined), isBuffer(result, 'ignored'));
                """
        };
        Assert.Equal("function function function function\ntrue false false\ntrue 68e96c6c6f\ntrue true true\nfunction isBuffer 1\n" +
                     "true false false 3\ntrue true\nfalse false true\n",
            TestHarness.RunModules(files, "main.cjs", mode));
    }

    [Theory, ModeData]
    public void Cjs_Require_Buffer_ValidatorsRejectNonBinaryInput(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.cjs"] = """
                const { isUtf8, isAscii } = require('node:buffer');
                for (const validate of [isUtf8, isAscii]) {
                    for (const args of [[], ['ascii'], [[65]], [new DataView(new ArrayBuffer(1))], [null]]) {
                        try { console.log(validate(...args)); }
                        catch (error) { console.log(error instanceof TypeError, error.name, error.code); }
                    }
                }
                """
        };
        const string expected = "true TypeError ERR_INVALID_ARG_TYPE\n";
        Assert.Equal(string.Concat(Enumerable.Repeat(expected, 10)), TestHarness.RunModules(files, "main.cjs", mode));
    }

    [Theory, ModeData]
    public void Cjs_Require_Buffer_TranscodeRejectsUnsupportedSources(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.cjs"] = """
                const { transcode } = require('node:buffer');
                for (const source of ['A', [65], new Int8Array([65]), new Uint16Array([65]),
                    new Uint8ClampedArray([65]), new ArrayBuffer(1), null, undefined]) {
                    try { console.log(transcode(source, 'utf8', 'latin1')); }
                    catch (error) { console.log(error instanceof TypeError, error.code); }
                }
                """
        };
        Assert.Equal(string.Concat(Enumerable.Repeat("true ERR_INVALID_ARG_TYPE\n", 8)),
            TestHarness.RunModules(files, "main.cjs", mode));
    }

    [Theory, ModeData]
    public void Cjs_Require_Assert_Ok(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.cjs"] = """
                const assert = require('assert');
                assert.ok(true);
                console.log('passed');
                """
        };
        var output = TestHarness.RunModules(files, "main.cjs", mode);
        Assert.Equal("passed\n", output);
    }

    [Theory, ModeData]
    public void Cjs_Require_Tty_Isatty(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.cjs"] = """
                const tty = require('tty');
                console.log(typeof tty.isatty);
                console.log(tty.isatty(999));
                """
        };
        var output = TestHarness.RunModules(files, "main.cjs", mode);
        Assert.Equal("function\nfalse\n", output);
    }

    /// <summary>
    /// Tests require() of multiple built-in modules in the same CJS file.
    /// </summary>
    [Theory, ModeData]
    public void Cjs_Require_MultipleModules(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.cjs"] = """
                const path = require('path');
                const tty = require('tty');
                console.log(typeof path.join);
                console.log(typeof tty.isatty);
                """
        };
        var output = TestHarness.RunModules(files, "main.cjs", mode);
        Assert.Equal("function\nfunction\n", output);
    }

    /// <summary>
    /// Regression: stdlib ESM modules required from a CJS caller.
    /// </summary>
    /// <remarks>
    /// The 'os' module migrated to stdlib/node/os.ts (embedded stdlib, ESM). CJS require()
    /// of ESM-in-assembly modules needs special handling: interpreter falls back to
    /// ExportsAsObject() when DefaultExport is null (named-exports-only modules have no
    /// default); compiled mode materializes a namespace from the module's export static
    /// fields. Both paths landed with the path migration; this test pins that os's
    /// equivalent shape also works.
    /// </remarks>
    [Theory, ModeData]
    public void Cjs_Require_Os_Platform(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.cjs"] = """
                const os = require('os');
                console.log(typeof os.platform);
                console.log(typeof os.EOL);
                """
        };
        var output = TestHarness.RunModules(files, "main.cjs", mode);
        Assert.Equal("function\nstring\n", output);
    }

    /// <summary>
    /// Regression: querystring (also an embedded stdlib ESM module) via require().
    /// </summary>
    [Theory, ModeData]
    public void Cjs_Require_Querystring_Parse(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.cjs"] = """
                const qs = require('querystring');
                console.log(typeof qs.parse);
                const parsed = qs.parse('a=1&b=2');
                console.log(parsed.a);
                console.log(parsed.b);
                """
        };
        var output = TestHarness.RunModules(files, "main.cjs", mode);
        Assert.Equal("function\n1\n2\n", output);
    }

    /// <summary>
    /// Regression (#1210 interpreter, #1217 compiled): require() of a stdlib facade with
    /// primitive:* imports from an ESM entry, without any ESM import of that module anywhere
    /// in the program.
    /// </summary>
    /// <remarks>
    /// A CJS entry pre-loads its require() graph via CollectCjsRequireDependencies, so the
    /// facade's primitive deps get executed by the static InterpretModules walk — which is why
    /// the main.cjs tests above never hit this. An ESM entry's require() used to be fully
    /// lazy: interpreted, the facade loaded but its `import ... from 'primitive:fs'` failed
    /// with "Module 'primitive:fs' not loaded" (#1210, fixed in BindModuleImports); compiled,
    /// the facade was never bundled into the assembly at all, so the require() lowering threw
    /// MODULE_NOT_FOUND at startup (#1217, fixed by running the require-literal walk over
    /// ESM bodies scoped to stdlib specifiers).
    /// </remarks>
    [Theory, ModeData]
    public void Cjs_Require_PrimitiveSeamFacade_FromEsmEntry(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as path from 'path';
                const fs = require('fs');
                console.log(typeof fs.existsSync);
                const zlib = require('zlib');
                console.log(zlib.gunzipSync(zlib.gzipSync('roundtrip')).toString());
                const os = require('os');
                console.log(typeof os.platform);
                console.log(typeof path.join);
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("function\nroundtrip\nfunction\nfunction\n", output);
    }

    /// <summary>
    /// Regression (#1217): the require() call sits inside a function body in an ESM module —
    /// the discovery walk must find nested require literals, not just top-level statements.
    /// </summary>
    [Theory, ModeData]
    public void Cjs_Require_StdlibFacade_InsideFunction_FromEsmEntry(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as path from 'path';
                function lazyOs(): any {
                    return require('os');
                }
                console.log(typeof lazyOs().platform);
                console.log(typeof path.join);
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("function\nfunction\n", output);
    }

    /// <summary>
    /// Regression (#1210): dynamic import() of a stdlib facade never statically imported hits
    /// the same lazy path — the facade's primitive:* deps must execute on demand.
    /// </summary>
    [Theory, InterpretedOnlyData]
    public void DynamicImport_PrimitiveSeamFacade_WithoutStaticImport(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as path from 'path';
                async function run(): Promise<void> {
                    const fs = await import('fs');
                    console.log(typeof fs.existsSync);
                    const zlib = await import('zlib');
                    console.log(zlib.gunzipSync(zlib.gzipSync('dyn')).toString());
                }
                run();
                """
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("function\ndyn\n", output);
    }
}

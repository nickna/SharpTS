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

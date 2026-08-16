using Xunit;

namespace SharpTS.Tests.IntegrationTests;

/// <summary>
/// End-to-end tests for CommonJS support in compiled (AOT) mode. Each test writes .cjs/.ts
/// files to a temp directory, compiles them with --compile, then executes the resulting DLL
/// and verifies output.
/// </summary>
public class CliCommonJsCompiledTests
{
    private static (int ExitCode, string StdOut) CompileAndRun(
        TempTestDirectory tempDir, string entryFile)
    {
        var compile = CliTestHelper.RunCli($"-c \"{entryFile}\"", tempDir.Path);
        if (compile.ExitCode != 0)
        {
            return (compile.ExitCode, compile.StandardOutput + compile.StandardError);
        }

        // Compute DLL path from the entry file's basename.
        var dllName = Path.GetFileNameWithoutExtension(entryFile) + ".dll";
        var dllPath = tempDir.GetPath(dllName);
        Assert.True(File.Exists(dllPath), $"Expected output DLL at {dllPath}");

        var psi = new System.Diagnostics.ProcessStartInfo("dotnet", $"\"{dllPath}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = tempDir.Path
        };

        using var process = System.Diagnostics.Process.Start(psi)!;
        var stdOut = process.StandardOutput.ReadToEnd();
        var stdErr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, stdOut + stdErr);
    }

    [Fact]
    public void Compiled_CjsFile_ModuleExports()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var entry = tempDir.CreateFile("bare.cjs", """
            console.log("hello");
            module.exports = 42;
            console.log("exports =", module.exports);
            """);

        var (exit, output) = CompileAndRun(tempDir, entry);
        Assert.Equal(0, exit);
        Assert.Contains("hello", output);
        Assert.Contains("exports = 42", output);
    }

    [Fact]
    public void Compiled_CjsFile_ExportsShorthand()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var entry = tempDir.CreateFile("shorthand.cjs", """
            exports.x = 10;
            exports.y = 20;
            console.log(exports.x, exports.y);
            """);

        var (exit, output) = CompileAndRun(tempDir, entry);
        Assert.Equal(0, exit);
        Assert.Contains("10 20", output);
    }

    [Fact]
    public void Compiled_CjsFile_RequireOtherCjs()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        tempDir.CreateFile("util.cjs", """
            exports.add = function (a, b) { return a + b; };
            exports.greet = function (name) { return "Hello, " + name + "!"; };
            """);
        var entry = tempDir.CreateFile("main.cjs", """
            const util = require("./util.cjs");
            console.log(util.add(2, 3));
            console.log(util.greet("compiled"));
            """);

        var (exit, output) = CompileAndRun(tempDir, entry);
        Assert.Equal(0, exit);
        Assert.Contains("5", output);
        Assert.Contains("Hello, compiled!", output);
    }

    /// <summary>
    /// Regression: a CJS module whose exported function captures top-level
    /// consts must see those consts at runtime. This pattern is common in
    /// real packages (e.g. <c>ms</c> defines <c>s, m, h, d, w, y</c> at module
    /// scope and references them from the exported function).
    ///
    /// <para>The original bug: <c>EmitCommonJsModuleInit</c> built its
    /// <c>CompilationContext</c> without setting <c>_modules.CurrentPath</c>
    /// first, so the per-module captured-var field lookups fell through to the
    /// SingleFile bucket (empty) and returned <c>null</c>. The emitted IL then
    /// loaded <c>null</c> for each constant, producing <c>0</c> / <c>NaN</c> /
    /// <c>Infinity</c> instead of the real values.</para>
    /// </summary>
    [Fact]
    public void Compiled_CjsFile_TopLevelConstsCapturedByExportedFunction()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        tempDir.CreateFile("units.cjs", """
            const s = 1000;
            const m = s * 60;
            const h = m * 60;
            const d = h * 24;
            function toMs(val) {
                if (val === 'd') return d;
                if (val === 'h') return h;
                if (val === 'm') return m;
                if (val === 's') return s;
                return 0;
            }
            module.exports = toMs;
            """);
        var entry = tempDir.CreateFile("main.cjs", """
            const toMs = require("./units.cjs");
            console.log(toMs('s'));
            console.log(toMs('m'));
            console.log(toMs('h'));
            console.log(toMs('d'));
            """);

        var (exit, output) = CompileAndRun(tempDir, entry);
        Assert.Equal(0, exit);
        // Expected values: 1_000, 60_000, 3_600_000, 86_400_000.
        // The pre-fix output was 0\n0\n0\n0 because the captured fields were null.
        Assert.Contains("1000", output);
        Assert.Contains("60000", output);
        Assert.Contains("3600000", output);
        Assert.Contains("86400000", output);
    }

    /// <summary>
    /// Regression for #541: a generator method that captures a <c>require()</c>'d import
    /// whose binding name collides with a same-named top-level function in ANOTHER module
    /// must resolve to the import, not the cross-module function. This is the yaml
    /// composer shape (<c>const composeDoc = require('./compose-doc.js')</c> referenced as
    /// <c>composeDoc.composeDoc(...)</c> inside <c>*next()</c>).
    ///
    /// <para>The captured variable is dropped from the per-module static-var map (captured
    /// vars live on the entry-point display class), so the generator's live read fell through
    /// to the global Functions registry and picked up the wrong module's function, throwing
    /// "object is not a function". A captured variable binding must shadow a cross-module
    /// function of the same name.</para>
    /// </summary>
    [Fact]
    public void Compiled_Generator_CapturedImportShadowsCrossModuleFunction()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        tempDir.CreateFile("compose-doc.cjs", """
            function composeDoc(opts, token) { return { v: token }; }
            module.exports = { composeDoc };
            """);
        var entry = tempDir.CreateFile("composer.cjs", """
            const composeDoc = require("./compose-doc.cjs");
            class Composer {
              *next(token) { yield composeDoc.composeDoc(this.opts, token).v; }
              *compose(tokens) { for (const t of tokens) yield* this.next(t); }
            }
            const out = [];
            for (const v of new Composer().compose([1, 2, 3])) out.push(v);
            console.log(out.join(","));
            """);

        var (exit, output) = CompileAndRun(tempDir, entry);
        Assert.Equal(0, exit);
        Assert.Contains("1,2,3", output);
    }

    [Fact]
    public void Compiled_CjsFile_CircularRequire_PartialExportsVisible()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        tempDir.CreateFile("a.cjs", """
            exports.name = "a";
            const b = require("./b.cjs");
            exports.greet = function () { return "from a, friend " + b.name; };
            console.log("a done");
            """);
        var entry = tempDir.CreateFile("b.cjs", """
            exports.name = "b";
            const a = require("./a.cjs");
            console.log("b sees a.name=" + a.name + " a.greet=" + typeof a.greet);
            """);

        var (exit, output) = CompileAndRun(tempDir, entry);
        Assert.Equal(0, exit);
        // b is the entry; b loads a; a re-requires b (already in progress, returns partial),
        // a finishes setting greet, a prints "a done", then b prints — at which point a is fully
        // initialized so a.greet is a function.
        Assert.Contains("a done", output);
        Assert.Contains("b sees a.name=a a.greet=function", output);
    }

    [Fact]
    public void Compiled_CjsFile_OptionalDep_ThrowsModuleNotFound()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var entry = tempDir.CreateFile("optional.cjs", """
            let optional = null;
            try {
              optional = require("./does-not-exist.cjs");
            } catch (e) {
              console.log("caught:", typeof e.message);
            }
            console.log("optional:", optional);
            """);

        var (exit, output) = CompileAndRun(tempDir, entry);
        Assert.Equal(0, exit);
        Assert.Contains("caught: string", output);
        Assert.Contains("optional: null", output);
    }

    [Fact]
    public void Compiled_EsmFile_DefaultImportsCjs()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        tempDir.CreateFile("util.cjs", """
            exports.add = function (a, b) { return a + b; };
            exports.greet = function (name) { return "Hello, " + name + "!"; };
            """);
        var entry = tempDir.CreateFile("main.ts", """
            import util from "./util.cjs";
            console.log(util.add(7, 8));
            console.log(util.greet("ts"));
            """);

        var (exit, output) = CompileAndRun(tempDir, entry);
        Assert.Equal(0, exit);
        Assert.Contains("15", output);
        Assert.Contains("Hello, ts!", output);
    }

    [Fact]
    public void Compiled_EsmFile_NamedImportsFromCjs()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        tempDir.CreateFile("util.cjs", """
            exports.add = function (a, b) { return a + b; };
            exports.mul = function (a, b) { return a * b; };
            """);
        var entry = tempDir.CreateFile("main.ts", """
            import { add, mul } from "./util.cjs";
            console.log(add(2, 3), mul(2, 3));
            """);

        var (exit, output) = CompileAndRun(tempDir, entry);
        Assert.Equal(0, exit);
        Assert.Contains("5 6", output);
    }

    [Fact]
    public void Compiled_EsmFile_NamespaceImportFromCjs()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        tempDir.CreateFile("util.cjs", """
            exports.add = function (a, b) { return a + b; };
            """);
        var entry = tempDir.CreateFile("main.ts", """
            import * as util from "./util.cjs";
            console.log(util.add(7, 8));
            """);

        var (exit, output) = CompileAndRun(tempDir, entry);
        Assert.Equal(0, exit);
        Assert.Contains("15", output);
    }
}

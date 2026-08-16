using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace SharpTS.Tests.IntegrationTests;

/// <summary>
/// Shared fixture that installs npm packages once for the entire test class.
/// The temp directory persists across all tests and is cleaned up after the last test.
/// </summary>
public class NpmFixture : IDisposable
{
    public string PackageDir { get; }
    public bool NpmAvailable { get; private set; }
    public string? SkipReason { get; private set; }

    /// <summary>Pinned package versions for reproducibility.</summary>
    private static readonly (string Name, string Version)[] Packages =
    [
        ("ms", "2.1.3"),
        ("uuid", "9.0.1"),
        ("debug", "4.3.4"),
        ("semver", "7.6.0"),
        ("minimatch", "9.0.4"),
        ("yaml", "2.4.1"),
        ("lodash", "4.17.21"),
    ];

    public NpmFixture()
    {
        PackageDir = Path.Combine(Path.GetTempPath(), "sharpts_npm_smoke");
        NpmAvailable = IsNpmOnPath();

        if (!NpmAvailable)
        {
            SkipReason = "npm is not available on PATH";
            return;
        }

        // Reuse existing install if the marker file exists (avoids repeated downloads).
        var marker = Path.Combine(PackageDir, ".sharpts_npm_installed");
        if (File.Exists(marker)) return;

        try
        {
            Directory.CreateDirectory(PackageDir);

            // Initialize package.json so npm install works.
            RunProcess("npm", "init -y", PackageDir, timeoutMs: 120_000);

            // Install all packages in one shot.
            var specs = string.Join(" ", Packages.Select(p => $"{p.Name}@{p.Version}"));
            var result = RunProcess("npm", $"install --save {specs}", PackageDir, timeoutMs: 240_000);
            if (result.ExitCode != 0)
                throw new InvalidOperationException(
                    $"npm install failed (exit {result.ExitCode}):\n{result.StdErr}");

            File.WriteAllText(marker, DateTime.UtcNow.ToString("O"));
        }
        catch (Exception ex)
        {
            // Degrade to skip rather than cascade-failing every test in the class.
            // Matches the documented contract: "tests skip gracefully otherwise".
            NpmAvailable = false;
            SkipReason = $"npm setup failed: {ex.GetType().Name}: {ex.Message}";
        }
    }

    public void Dispose()
    {
        // Intentionally leave the directory for caching across test runs.
        // CI can wipe temp if needed.
    }

    private static bool IsNpmOnPath()
    {
        try
        {
            var result = RunProcess("npm", "--version", Path.GetTempPath(), timeoutMs: 30_000);
            return result.ExitCode == 0;
        }
        catch { return false; }
    }

    internal static (int ExitCode, string StdOut, string StdErr) RunProcess(
        string fileName, string arguments, string workingDir, int timeoutMs = 60_000)
    {
        // On Windows, .cmd/.bat scripts (like npm.cmd) require cmd.exe to execute
        // when UseShellExecute is false.
        string actualFile = fileName;
        string actualArgs = arguments;
        if (OperatingSystem.IsWindows() && fileName == "npm")
        {
            actualFile = "cmd.exe";
            actualArgs = $"/c npm {arguments}";
        }

        var psi = new ProcessStartInfo(actualFile, actualArgs)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDir
        };

        using var process = Process.Start(psi)!;
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(timeoutMs))
        {
            process.Kill();
            throw new TimeoutException($"{fileName} {arguments} exceeded {timeoutMs}ms");
        }

        return (process.ExitCode, stdoutTask.Result, stderrTask.Result);
    }
}

/// <summary>
/// xUnit collection that shares the <see cref="NpmFixture"/> across the npm-dependent
/// test class so <c>npm init</c>/<c>npm install</c> only runs once. Parallelization
/// is left at xUnit's default (collections run in parallel against each other) —
/// the fixture catches setup failures and degrades to skip rather than blocking
/// other tests.
/// </summary>
[CollectionDefinition("Npm")]
public class NpmCollection : ICollectionFixture<NpmFixture> { }

/// <summary>
/// Smoke tests that validate SharpTS against real npm packages.
/// Requires npm on PATH; tests skip gracefully otherwise.
/// Filter: dotnet test --filter "Category=npm"
/// </summary>
[Trait("Category", "npm")]
[Collection("Npm")]
public class RealPackageSmokeTests
{
    private readonly NpmFixture _npm;
    private readonly ITestOutputHelper _output;

    public RealPackageSmokeTests(NpmFixture npm, ITestOutputHelper output)
    {
        _npm = npm;
        _output = output;
    }

    private void SkipIfNoNpm()
    {
        Skip.If(!_npm.NpmAvailable, _npm.SkipReason ?? "npm is not available");
    }

    private CliTestHelper.CliResult RunInterpreter(string scriptPath)
    {
        var result = CliTestHelper.RunCli($"\"{scriptPath}\"", _npm.PackageDir, TimeSpan.FromSeconds(60));
        _output.WriteLine($"[interpreter] exit={result.ExitCode}");
        _output.WriteLine($"[interpreter] stdout:\n{result.StandardOutput}");
        if (!string.IsNullOrEmpty(result.StandardError))
            _output.WriteLine($"[interpreter] stderr:\n{result.StandardError}");
        return result;
    }

    private (int ExitCode, string Output) CompileAndRun(string scriptPath)
    {
        var compile = CliTestHelper.RunCli($"-c \"{scriptPath}\"", _npm.PackageDir, TimeSpan.FromSeconds(60));
        _output.WriteLine($"[compile] exit={compile.ExitCode}");
        if (compile.ExitCode != 0)
        {
            var msg = compile.StandardOutput + compile.StandardError;
            _output.WriteLine($"[compile] output:\n{msg}");
            return (compile.ExitCode, msg);
        }

        var dllName = Path.GetFileNameWithoutExtension(scriptPath) + ".dll";
        var dllPath = Path.Combine(_npm.PackageDir, dllName);
        if (!File.Exists(dllPath))
            return (-1, $"DLL not found at {dllPath}");

        var (exitCode, stdOut, stdErr) = NpmFixture.RunProcess("dotnet", $"\"{dllPath}\"", _npm.PackageDir);
        var output = stdOut + stdErr;
        _output.WriteLine($"[run] exit={exitCode}");
        _output.WriteLine($"[run] output:\n{output}");
        return (exitCode, CliTestHelper.NormalizeOutput(output));
    }

    private string CreateScript(string name, string content)
    {
        var path = Path.Combine(_npm.PackageDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    // ──────────────────────────────────────────────────────────────
    // ms — tiny duration parser (~150 LOC, zero deps)
    // ──────────────────────────────────────────────────────────────

    [SkippableFact]
    public void Ms_Interpreter()
    {
        SkipIfNoNpm();
        var script = CreateScript("test_ms.cjs", """
            const ms = require('ms');
            console.log(ms('2 days'));
            console.log(ms('1h'));
            console.log(ms(60000));
            """);

        var result = RunInterpreter(script);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("172800000", result.StandardOutput);
        Assert.Contains("3600000", result.StandardOutput);
        Assert.Contains("1m", result.StandardOutput);
    }

    [SkippableFact]
    public void Ms_Compiled()
    {
        SkipIfNoNpm();
        var script = CreateScript("test_ms_c.cjs", """
            const ms = require('ms');
            console.log(ms('2 days'));
            console.log(ms('1h'));
            console.log(ms(60000));
            """);

        var (exit, output) = CompileAndRun(script);
        Assert.Equal(0, exit);
        Assert.Contains("172800000", output);
        Assert.Contains("3600000", output);
        Assert.Contains("1m", output);
    }

    // ──────────────────────────────────────────────────────────────
    // uuid — UUID generation, tests crypto interop
    // ──────────────────────────────────────────────────────────────

    [SkippableFact]
    public void Uuid_Interpreter()
    {
        SkipIfNoNpm();
        var script = CreateScript("test_uuid.cjs", """
            const { v4 } = require('uuid');
            const id = v4();
            console.log(typeof id);
            console.log(id.length);
            """);

        var result = RunInterpreter(script);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("string", result.StandardOutput);
        Assert.Contains("36", result.StandardOutput);
    }

    [SkippableFact]
    public void Uuid_Compiled()
    {
        SkipIfNoNpm();
        var script = CreateScript("test_uuid_c.cjs", """
            const { v4 } = require('uuid');
            const id = v4();
            console.log(typeof id);
            console.log(id.length);
            """);

        var (exit, output) = CompileAndRun(script);
        Assert.Equal(0, exit);
        Assert.Contains("string", output);
        Assert.Contains("36", output);
    }

    // The .ts (ESM) variants exercise a separate code path: named imports against a CJS
    // module whose exports are accessor properties (Babel's transpiled `Object.defineProperty
    // (exports, "v4", { get: ... })`). The interpreter previously read these via a direct
    // _fields lookup that bypassed getters and bound v4 to undefined; covered by issue #55.

    [SkippableFact]
    public void Uuid_Esm_Interpreter()
    {
        SkipIfNoNpm();
        var script = CreateScript("test_uuid_esm.ts", """
            import { v4, validate, NIL } from 'uuid';
            const id = v4();
            console.log(typeof id);
            console.log(id.length);
            console.log(validate(id));
            console.log(NIL);
            """);

        var result = RunInterpreter(script);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("string", result.StandardOutput);
        Assert.Contains("36", result.StandardOutput);
        Assert.Contains("true", result.StandardOutput);
        Assert.Contains("00000000-0000-0000-0000-000000000000", result.StandardOutput);
    }

    [SkippableFact]
    public void Uuid_Esm_Compiled()
    {
        SkipIfNoNpm();
        var script = CreateScript("test_uuid_esm_c.ts", """
            import { v4, validate, NIL } from 'uuid';
            const id = v4();
            console.log(typeof id);
            console.log(id.length);
            console.log(validate(id));
            console.log(NIL);
            """);

        var (exit, output) = CompileAndRun(script);
        Assert.Equal(0, exit);
        Assert.Contains("string", output);
        Assert.Contains("36", output);
        Assert.Contains("true", output);
        Assert.Contains("00000000-0000-0000-0000-000000000000", output);
    }

    // ──────────────────────────────────────────────────────────────
    // debug — logging utility (depends on ms)
    // ──────────────────────────────────────────────────────────────

    [SkippableFact]
    public void Debug_Interpreter()
    {
        SkipIfNoNpm();
        var script = CreateScript("test_debug.cjs", """
            const debug = require('debug');
            const log = debug('test');
            console.log(typeof debug);
            console.log(typeof log);
            """);

        var result = RunInterpreter(script);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("function", result.StandardOutput);
    }

    [SkippableFact]
    public void Debug_Compiled()
    {
        SkipIfNoNpm();
        var script = CreateScript("test_debug_c.cjs", """
            const debug = require('debug');
            const log = debug('test');
            console.log(typeof debug);
            console.log(typeof log);
            """);

        var (exit, output) = CompileAndRun(script);
        Assert.Equal(0, exit);
        Assert.Contains("function", output);
    }

    // ──────────────────────────────────────────────────────────────
    // semver — semantic version parsing
    // ──────────────────────────────────────────────────────────────

    [SkippableFact]
    public void Semver_Interpreter()
    {
        SkipIfNoNpm();
        var script = CreateScript("test_semver.cjs", """
            const semver = require('semver');
            console.log(semver.valid('1.2.3'));
            console.log(semver.gt('1.2.3', '1.2.0'));
            console.log(semver.satisfies('1.2.3', '>=1.0.0'));
            """);

        var result = RunInterpreter(script);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("1.2.3", result.StandardOutput);
        Assert.Contains("true", result.StandardOutput);
    }

    [SkippableFact]
    public void Semver_Compiled()
    {
        SkipIfNoNpm();
        var script = CreateScript("test_semver_c.cjs", """
            const semver = require('semver');
            console.log(semver.valid('1.2.3'));
            console.log(semver.gt('1.2.3', '1.2.0'));
            console.log(semver.satisfies('1.2.3', '>=1.0.0'));
            """);

        var (exit, output) = CompileAndRun(script);
        Assert.Equal(0, exit);
        Assert.Contains("1.2.3", output);
        Assert.Contains("true", output);
    }

    // ──────────────────────────────────────────────────────────────
    // minimatch — glob pattern matcher
    // ──────────────────────────────────────────────────────────────

    [SkippableFact]
    public void Minimatch_Interpreter()
    {
        SkipIfNoNpm();
        var script = CreateScript("test_minimatch.cjs", """
            const { minimatch } = require('minimatch');
            console.log(minimatch('foo.js', '*.js'));
            console.log(minimatch('bar.txt', '*.js'));
            """);

        var result = RunInterpreter(script);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("true", result.StandardOutput);
        Assert.Contains("false", result.StandardOutput);
    }

    [SkippableFact]
    public void Minimatch_Compiled()
    {
        SkipIfNoNpm();
        var script = CreateScript("test_minimatch_c.cjs", """
            const { minimatch } = require('minimatch');
            console.log(minimatch('foo.js', '*.js'));
            console.log(minimatch('bar.txt', '*.js'));
            """);

        var (exit, output) = CompileAndRun(script);
        Assert.Equal(0, exit);
        Assert.Contains("true", output);
        Assert.Contains("false", output);
    }

    // ──────────────────────────────────────────────────────────────
    // yaml — YAML parser
    // ──────────────────────────────────────────────────────────────

    [SkippableFact]
    public void Yaml_Interpreter()
    {
        SkipIfNoNpm();
        var script = CreateScript("test_yaml.cjs", """
            const YAML = require('yaml');
            const obj = YAML.parse('a: 1\nb: 2');
            console.log(obj.a);
            console.log(obj.b);
            console.log(typeof YAML.stringify);
            """);

        var result = RunInterpreter(script);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("1", result.StandardOutput);
        Assert.Contains("2", result.StandardOutput);
        Assert.Contains("function", result.StandardOutput);
    }

    [SkippableFact]
    public void Yaml_Compiled()
    {
        SkipIfNoNpm();
        var script = CreateScript("test_yaml_c.cjs", """
            const YAML = require('yaml');
            const obj = YAML.parse('a: 1\nb: 2');
            console.log(obj.a);
            console.log(obj.b);
            console.log(typeof YAML.stringify);
            """);

        var (exit, output) = CompileAndRun(script);
        Assert.Equal(0, exit);
        Assert.Contains("1", output);
        Assert.Contains("2", output);
        Assert.Contains("function", output);
    }

    // ──────────────────────────────────────────────────────────────
    // lodash — utility kitchen sink
    // ──────────────────────────────────────────────────────────────

    [SkippableFact]
    public void Lodash_Interpreter()
    {
        SkipIfNoNpm();
        var script = CreateScript("test_lodash.cjs", """
            const _ = require('lodash');
            console.log(typeof _);
            console.log(_.chunk([1, 2, 3, 4], 2));
            console.log(_.flatten([[1, 2], [3, 4]]));
            """);

        var result = RunInterpreter(script);
        Assert.Equal(0, result.ExitCode);
        // `typeof _` is the first output line. Assert on the first line specifically,
        // not Contains — lodash's own module init can emit debug strings containing
        // the word "function" (e.g. from leftover instrumentation), masking a bug
        // where require('lodash') returns the empty module-init dict instead of the
        // lodash function.
        Assert.Equal("function", result.StandardOutput.Split('\n')[0].Trim());
    }

    // Pre-#260, compiled lodash "loaded" only because non-callable dispatches
    // silently evaluated to null: global/globalThis compile to null in value
    // position, so runInContext ran with a null context and every native ref
    // was undefined (which is also why chunk/flatten returned wrong values).
    // With the #260 TypeError, init failed honestly at lodash's
    // `funcToString.call(Object)`. #271 gave globalThis a real value
    // representation, so root/context detection now resolves real constructors —
    // init gets past `funcToString.call(Object)`. It still throws later because
    // value-form built-in singleton methods (e.g. `context.Math.max`) are not
    // populated in compiled mode (the Math/JSON singleton dicts hold no method
    // wrappers). #276 populated those wrappers and got init past `context.Math.max`,
    // but compiled init still failed inside lodash's `getNative` helper
    // ("object is not a function"). #302's original hypothesis (a value-form
    // dispatch gap) was DISPROVEN while triaging: the true root cause (#307) was a
    // closure bug — inner function declarations nested in an arrow/function-
    // expression captured enclosing locals by snapshot (at hoist time) instead of
    // by live reference, so `getValue` / `Object` / `symToStringTag` read back
    // null (`typeof null === "object"` → "object is not a function"). #307's
    // $arrowScopeDC live-reference fix got init past that.
    [SkippableFact]
    public void Lodash_Compiled()
    {
        SkipIfNoNpm();
        var script = CreateScript("test_lodash_c.cjs", """
            const _ = require('lodash');
            console.log(typeof _);
            console.log(_.chunk([1, 2, 3, 4], 2));
            console.log(_.flatten([[1, 2], [3, 4]]));
            """);

        var (exit, output) = CompileAndRun(script);
        Assert.Equal(0, exit);
        // See Lodash_Interpreter above for rationale.
        var lines = output.Split('\n').Select(l => l.Trim()).ToArray();
        Assert.Equal("function", lines[0]);
        Assert.Equal("[[1, 2], [3, 4]]", lines[1]);
        Assert.Equal("[1, 2, 3, 4]", lines[2]);
    }
}

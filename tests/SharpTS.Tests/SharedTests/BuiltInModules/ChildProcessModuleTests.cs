using System.Runtime.InteropServices;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Tests for the built-in 'child_process' module.
/// Tests synchronous methods (execSync, spawnSync, execFileSync) that work in both interpreter and compiled modes.
/// </summary>
public class ChildProcessModuleTests
{
    [Theory, ModeData]
    public void ExecSync_EchoCommand_ReturnsOutput(ExecutionMode mode)
    {
        // Use a simple echo command that works on all platforms
        var echoCommand = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "echo hello"
            : "echo hello";

        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import * as childProcess from 'child_process';
                const result = childProcess.execSync('{{echoCommand}}');
                console.log(typeof result === 'string');
                console.log(result.trim() === 'hello');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void ExecSync_WithEnvironment_PassesEnvVars(ExecutionMode mode)
    {
        // Test that environment variables are passed through
        var envVarEcho = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "echo %TEST_VAR%"
            : "echo $TEST_VAR";

        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import * as childProcess from 'child_process';
                const result = childProcess.execSync('{{envVarEcho}}', { env: { TEST_VAR: 'test_value' } });
                console.log(typeof result === 'string');
                console.log(result.trim() === 'test_value');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void SpawnSync_ReturnsStatusObject(ExecutionMode mode)
    {
        // spawnSync should return an object with status, stdout, stderr
        var command = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "cmd.exe"
            : "/bin/sh";
        var args = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "['/c', 'echo', 'hello']"
            : "['-c', 'echo hello']";

        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import * as childProcess from 'child_process';
                const result = childProcess.spawnSync('{{command}}', {{args}});
                console.log(typeof result === 'object');
                console.log('stdout' in result);
                console.log('stderr' in result);
                console.log('status' in result);
                console.log(result.status === 0);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void SpawnSync_WithArgs_PassesArguments(ExecutionMode mode)
    {
        // spawnSync should pass arguments correctly
        var command = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "cmd.exe"
            : "/bin/echo";
        var args = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "['/c', 'echo', 'test_arg']"
            : "['test_arg']";

        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import * as childProcess from 'child_process';
                const result = childProcess.spawnSync('{{command}}', {{args}});
                console.log(result.status === 0);
                console.log(result.stdout.trim() === 'test_arg');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void ExecFileSync_RunsFileDirectly(ExecutionMode mode)
    {
        // execFileSync should execute a file without a shell
        var command = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "cmd.exe"
            : "/bin/echo";
        var args = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "['/c', 'echo', 'filehello']"
            : "['filehello']";

        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import { execFileSync } from 'child_process';
                const result = execFileSync('{{command}}', {{args}});
                console.log(typeof result === 'string');
                console.log(result.trim() === 'filehello');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void ExecFileSync_ThrowsOnNonZeroExit(ExecutionMode mode)
    {
        // execFileSync should throw on non-zero exit code
        var command = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "cmd.exe"
            : "/bin/sh";
        var args = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "['/c', 'exit', '1']"
            : "['-c', 'exit 1']";

        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import { execFileSync } from 'child_process';
                let threw = false;
                try {
                    execFileSync('{{command}}', {{args}});
                } catch (e) {
                    threw = true;
                }
                console.log(threw);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void ExecSync_ThrowsOnNonZeroExit(ExecutionMode mode)
    {
        // execSync should throw on non-zero exit code
        var exitCmd = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "exit /b 42"
            : "exit 42";

        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import { execSync } from 'child_process';
                let threw = false;
                try {
                    execSync('{{exitCmd}}');
                } catch (e) {
                    threw = true;
                }
                console.log(threw);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void SpawnSync_NonZeroExit_ReturnsStatus(ExecutionMode mode)
    {
        // spawnSync should NOT throw but return non-zero status
        var command = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "cmd.exe"
            : "/bin/sh";
        var args = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "['/c', 'exit', '42']"
            : "['-c', 'exit 42']";

        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import { spawnSync } from 'child_process';
                const result = spawnSync('{{command}}', {{args}});
                console.log(result.status === 42);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void SpawnSync_StdoutCapture(ExecutionMode mode)
    {
        // spawnSync should capture stdout content
        var command = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "cmd.exe"
            : "/bin/sh";
        var args = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "['/c', 'echo', 'captured_output']"
            : "['-c', 'echo captured_output']";

        var files = new Dictionary<string, string>
        {
            ["main.ts"] = $$"""
                import { spawnSync } from 'child_process';
                const result = spawnSync('{{command}}', {{args}});
                console.log(result.stdout.trim());
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("captured_output\n", output);
    }

    [Theory, ModeData]
    public void NamedImport_AllMethods(ExecutionMode mode)
    {
        // All methods should be importable via named imports
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { execSync, spawnSync, exec, spawn, execFileSync, execFile, fork } from 'child_process';
                console.log(typeof execSync === 'function');
                console.log(typeof spawnSync === 'function');
                console.log(typeof exec === 'function');
                console.log(typeof spawn === 'function');
                console.log(typeof execFileSync === 'function');
                console.log(typeof execFile === 'function');
                console.log(typeof fork === 'function');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\n", output);
    }
}

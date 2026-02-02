using System.Runtime.InteropServices;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Tests for the built-in 'child_process' module.
/// Tests synchronous methods (execSync, spawnSync) that work in both interpreter and compiled modes.
/// Note: Environment variable passing in execSync is interpreter-only due to compiler limitations.
/// </summary>
public class ChildProcessModuleTests
{
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void ExecSync_WithEnvironment_PassesEnvVars(ExecutionMode mode)
    {
        // Test that environment variables are passed through
        // Interpreter-only: compiled mode doesn't extract env from options
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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
}

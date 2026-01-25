using System.Runtime.InteropServices;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests.BuiltInModules;

/// <summary>
/// Tests for the built-in 'child_process' module.
/// Uses interpreter mode since child_process isn't fully supported in compiled mode.
/// </summary>
public class ChildProcessModuleTests
{
    [Fact]
    public void ExecSync_EchoCommand_ReturnsOutput()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void ExecSync_WithEnvironment_PassesEnvVars()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void SpawnSync_ReturnsStatusObject()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\ntrue\ntrue\n", output);
    }

    [Fact]
    public void SpawnSync_WithArgs_PassesArguments()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }
}

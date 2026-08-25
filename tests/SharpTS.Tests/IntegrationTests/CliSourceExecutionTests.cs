using System.Diagnostics;
using SharpTS.Runtime;
using Xunit;

namespace SharpTS.Tests.IntegrationTests;

/// <summary>
/// End-to-end deployment coverage for the managed source-execution bridge.
/// </summary>
public class CliSourceExecutionTests
{
    [Fact]
    public void Compile_CopiesManagedClosure_AndRunsOutsideCompilerProcess()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var entryPath = tempDir.CreateFile("worker.ts", """
            import * as execution from "sharpts:execution";
            const run = execution.runSourceJson;
            const result = JSON.parse(
                run("console.log(40 + 2);", "compile", 1024)
            );
            console.log(result.Success);
            console.log(result.Output.trim());
            """);
        var outputPath = tempDir.GetPath("worker.dll");

        var compile = CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{entryPath}\" -o \"{outputPath}\"",
            tempDir.Path,
            TimeSpan.FromSeconds(60));

        Assert.Equal(0, compile.ExitCode);
        Assert.Contains("Copied SharpTS runtime", compile.StandardOutput);
        Assert.True(File.Exists(tempDir.GetPath("SharpTS.dll")));
        Assert.True(File.Exists(tempDir.GetPath("SharpTS.deps.json")));
        Assert.True(File.Exists(tempDir.GetPath("SharpTS.runtimeconfig.json")));
        // A representative transitive dependency proves this is the managed closure,
        // not only the bridge assembly.
        Assert.True(File.Exists(tempDir.GetPath("NuGet.Protocol.dll")));

        var run = RunDll(outputPath, tempDir.Path);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("true\n42\n", CliTestHelper.NormalizeOutput(run.Output));
    }

    private static (int ExitCode, string Output) RunDll(string dllPath, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo("dotnet", $"\"{dllPath}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory
        };

        using var process = Process.Start(startInfo)!;
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60_000))
        {
            ProcessTreeTermination.Terminate(process);
            throw new TimeoutException("Compiled source-execution host did not exit within 60 seconds.");
        }

        return (process.ExitCode, stdoutTask.Result + stderrTask.Result);
    }
}

using System.Diagnostics;
using SharpTS.Runtime;
using Xunit;

namespace SharpTS.Tests.IntegrationTests;

/// <summary>
/// End-to-end deployment coverage for workers created by compiled output.
/// </summary>
public class CliWorkerThreadsTests
{
    [Fact]
    public void Compile_CopiesManagedClosure_AndRunsCompiledWorker()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        tempDir.CreateFile("cpu-worker.ts", """
            import { parentPort } from "worker_threads";
            let checksum: number = 0;
            for (let i: number = 0; i < 1000; i++) checksum = checksum + i;
            parentPort!.postMessage(checksum);
            """);
        var entryPath = tempDir.CreateFile("main.ts", """
            import { Worker } from "worker_threads";
            const worker = new Worker(__dirname + "/cpu-worker.ts");
            worker.on("message", value => console.log(value));
            """);
        var outputPath = tempDir.GetPath("main.dll");

        var compile = CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{entryPath}\" -o \"{outputPath}\"",
            tempDir.Path,
            TimeSpan.FromSeconds(60));

        Assert.Equal(0, compile.ExitCode);
        Assert.Contains("Copied SharpTS runtime", compile.StandardOutput);
        Assert.True(File.Exists(tempDir.GetPath("SharpTS.dll")));
        Assert.True(File.Exists(tempDir.GetPath("SharpTS.Hosting.Abstractions.dll")));

        var run = RunDll(outputPath, tempDir.Path);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("499500\n", CliTestHelper.NormalizeOutput(run.Output));
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
            throw new TimeoutException("Compiled worker host did not exit within 60 seconds.");
        }

        return (process.ExitCode, stdoutTask.Result + stderrTask.Result);
    }
}

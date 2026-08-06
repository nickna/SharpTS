using System.Diagnostics;
using Xunit;

namespace SharpTS.Tests.IntegrationTests;

public class CliShebangTests
{
    private const string Script = "#!/usr/bin/env sharpts\nconsole.log(\"Hello from SharpTS\");";

    [Fact]
    public void Execute_ShebangScript()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("hello.ts", Script);

        var result = CliTestHelper.RunCli($"--no-tsconfig \"{scriptPath}\"", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("Hello from SharpTS\n", result.StandardOutput);
    }

    [Fact]
    public void Compile_ShebangScript_ProducesRunnableAssembly()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("hello.ts", Script);

        var compile = CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{scriptPath}\"",
            tempDir.Path);

        Assert.Equal(0, compile.ExitCode);
        var outputPath = tempDir.GetPath("hello.dll");
        Assert.True(File.Exists(outputPath));

        var run = RunDll(outputPath, tempDir.Path);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("Hello from SharpTS\n", run.StandardOutput);
        Assert.Empty(run.StandardError);
    }

    private static CliTestHelper.CliResult RunDll(string dllPath, string workingDirectory)
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
        if (!process.WaitForExit((int)CliTestHelper.DefaultTimeout.TotalMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("Compiled shebang script did not exit within 30 seconds.");
        }

        return new CliTestHelper.CliResult(
            process.ExitCode,
            CliTestHelper.NormalizeOutput(stdoutTask.Result),
            CliTestHelper.NormalizeOutput(stderrTask.Result));
    }
}

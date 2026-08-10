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

    [SkippableFact]
    public void ExecutableTextStatsExample_RunsThroughShebangWithArguments()
    {
        Skip.If(OperatingSystem.IsWindows(),
            "Unix shebang execution is not supported on Windows");
        if (OperatingSystem.IsWindows()) return; // Satisfy platform analysis after the dynamic skip.

        var scriptPath = FindRepositoryFile("Examples", "text-stats.ts");
        Assert.True(
            (File.GetUnixFileMode(scriptPath) & UnixFileMode.UserExecute) != 0,
            "Examples/text-stats.ts must be committed with its executable bit set");

        using var tempDir = CliTestHelper.CreateTempDirectory();
        var inputPath = tempDir.CreateFile(
            "sample.txt",
            "Hello hello world.\nSharpTS makes scripts useful; world world!\n");
        var launcherDir = System.IO.Path.Combine(tempDir.Path, "bin");
        Directory.CreateDirectory(launcherDir);
        var launcherPath = System.IO.Path.Combine(launcherDir, "sharpts");
        File.WriteAllText(
            launcherPath,
            "#!/bin/sh\nexec dotnet \"$SHARPTS_TEST_DLL\" \"$@\"\n");
        File.SetUnixFileMode(
            launcherPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var result = RunExecutableExample(
            scriptPath,
            tempDir.Path,
            launcherDir,
            inputPath,
            "--top", "2",
            "--min-length", "4");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.StandardError);
        Assert.Contains("Lines:        2", result.StandardOutput);
        Assert.Contains("Words:        9", result.StandardOutput);
        Assert.Contains("Unique words: 6", result.StandardOutput);
        Assert.Contains("1. world - 3", result.StandardOutput);
        Assert.Contains("2. hello - 2", result.StandardOutput);

        var help = RunExecutableExample(
            scriptPath,
            tempDir.Path,
            launcherDir,
            "--help");
        Assert.Equal(0, help.ExitCode);
        Assert.Contains("Usage: ./Examples/text-stats.ts", help.StandardOutput);

        var missing = RunExecutableExample(
            scriptPath,
            tempDir.Path,
            launcherDir,
            System.IO.Path.Combine(tempDir.Path, "missing.txt"));
        Assert.Equal(1, missing.ExitCode);
        Assert.Contains("Error: File not found", missing.StandardError);
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var parts = new string[segments.Length + 1];
            parts[0] = directory.FullName;
            Array.Copy(segments, 0, parts, 1, segments.Length);
            var candidate = System.IO.Path.Combine(parts);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "Could not locate repository file: " + System.IO.Path.Combine(segments));
    }

    private static CliTestHelper.CliResult RunExecutableExample(
        string scriptPath,
        string workingDirectory,
        string launcherDirectory,
        params string[] arguments)
    {
        var sharpTsDll = System.IO.Path.Combine(AppContext.BaseDirectory, "SharpTS.dll");
        Assert.True(File.Exists(sharpTsDll), "SharpTS.dll was not found beside the test assembly");

        var startInfo = new ProcessStartInfo(scriptPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        startInfo.Environment["SHARPTS_TEST_DLL"] = sharpTsDll;
        startInfo.Environment["PATH"] = launcherDirectory + System.IO.Path.PathSeparator
            + startInfo.Environment["PATH"];

        using var process = Process.Start(startInfo)!;
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit((int)CliTestHelper.DefaultTimeout.TotalMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("Executable shebang example did not exit within 30 seconds.");
        }

        return new CliTestHelper.CliResult(
            process.ExitCode,
            CliTestHelper.NormalizeOutput(stdoutTask.Result),
            CliTestHelper.NormalizeOutput(stderrTask.Result));
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

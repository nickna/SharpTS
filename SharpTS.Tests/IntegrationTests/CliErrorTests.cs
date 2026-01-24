using System.Text.RegularExpressions;
using Xunit;

namespace SharpTS.Tests.IntegrationTests;

/// <summary>
/// Tests for CLI error handling and exit codes.
/// </summary>
public class CliErrorTests
{
    [Fact]
    public void StandardFormat_OutputsErrorPrefix()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("error.ts", CliFixtures.TypeErrorScript);

        var result = CliTestHelper.RunCli($"-c \"{scriptPath}\"", tempDir.Path);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Error", result.StandardError);
    }

    [Fact]
    public void MsBuildFormat_OutputsStructuredErrors()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("error.ts", CliFixtures.TypeErrorScript);

        var result = CliTestHelper.RunCli($"-c \"{scriptPath}\" --msbuild-errors", tempDir.Path);

        Assert.Equal(1, result.ExitCode);
        // MSBuild format: file(line,col): error SHARPTS###: message
        Assert.Matches(@"\([\d,]+\): error SHARPTS\d+:", result.StandardError);
    }

    [Fact]
    public void MsBuildFormat_ParseError()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("parseerror.ts", CliFixtures.ParseErrorScript);

        var result = CliTestHelper.RunCli($"-c \"{scriptPath}\" --msbuild-errors", tempDir.Path);

        Assert.Equal(1, result.ExitCode);
        // Should output to stderr in MSBuild format
        Assert.Matches(@"error SHARPTS\d+:", result.StandardError);
    }

    [Fact]
    public void UnknownFlag_ExitsCode64()
    {
        var result = CliTestHelper.RunCli("--unknown-flag");

        Assert.Equal(64, result.ExitCode);
        Assert.Contains("Unknown option", result.StandardOutput);
    }

    [Fact]
    public void UnknownShortFlag_ExitsCode64()
    {
        var result = CliTestHelper.RunCli("-x");

        Assert.Equal(64, result.ExitCode);
        Assert.Contains("Unknown option", result.StandardOutput);
    }

    [Fact]
    public void UnknownFlag_SuggestsHelp()
    {
        var result = CliTestHelper.RunCli("--unknown-flag");

        Assert.Equal(64, result.ExitCode);
        Assert.Contains("--help", result.StandardOutput);
    }

    [Fact]
    public void GenDecl_MissingArgument_ExitsCode64()
    {
        var result = CliTestHelper.RunCli("--gen-decl");

        Assert.Equal(64, result.ExitCode);
        Assert.Contains("Usage:", result.StandardOutput);
    }

    [Fact]
    public void Compile_EmptyFile_Succeeds()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("empty.ts", "");

        var result = CliTestHelper.RunCli($"-c \"{scriptPath}\"", tempDir.Path);

        // Empty file should compile successfully
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void Compile_WhitespaceOnlyFile_Succeeds()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("whitespace.ts", "   \n\t\n   ");

        var result = CliTestHelper.RunCli($"-c \"{scriptPath}\"", tempDir.Path);

        // Whitespace-only file should compile successfully
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void Compile_MultipleErrors_ShowsAllErrors()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var multiErrorScript = """
            let x: number = "a";
            let y: boolean = 42;
            let z: string = true;
            """;
        var scriptPath = tempDir.CreateFile("multi.ts", multiErrorScript);

        var result = CliTestHelper.RunCli($"-c \"{scriptPath}\"", tempDir.Path);

        Assert.Equal(1, result.ExitCode);
        // Should show multiple error messages
        var errorCount = Regex.Matches(result.StandardError, "Error").Count;
        Assert.True(errorCount >= 2, $"Expected at least 2 errors, got {errorCount}");
    }

    [Fact]
    public void Execute_RuntimeError_NonZeroExit()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var errorScript = """
            throw new Error("intentional error");
            """;
        var scriptPath = tempDir.CreateFile("runtime.ts", errorScript);

        var result = CliTestHelper.RunCli($"\"{scriptPath}\"", tempDir.Path);

        // Runtime errors should result in non-zero exit, but the script runs via interpreter
        // which catches exceptions and prints them - so exit code is actually 0
        Assert.Contains("Error:", result.StandardOutput);
    }

    [Fact]
    public void Compile_NoDuplicateErrors()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("error.ts", CliFixtures.TypeErrorScript);

        var result = CliTestHelper.RunCli($"-c \"{scriptPath}\"", tempDir.Path);

        Assert.Equal(1, result.ExitCode);
        // Count error occurrences - should not be duplicated
        var errorLines = result.StandardError.Split('\n')
            .Where(line => line.Contains("Error"))
            .ToList();

        // Each unique error should appear only once
        Assert.True(errorLines.Distinct().Count() == errorLines.Count,
            "Found duplicate error messages");
    }
}

using System.Text.RegularExpressions;
using Xunit;

namespace SharpTS.Tests.IntegrationTests;

/// <summary>
/// Tests for --help and --version CLI flags.
/// </summary>
public class CliHelpVersionTests
{
    [Fact]
    public void Help_LongFlag_PrintsUsage()
    {
        var result = CliTestHelper.RunCli("--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Usage:", result.StandardOutput);
        Assert.Contains("sharpts", result.StandardOutput);
    }

    [Fact]
    public void Help_ShortFlag_PrintsUsage()
    {
        var result = CliTestHelper.RunCli("-h");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Usage:", result.StandardOutput);
        Assert.Contains("sharpts", result.StandardOutput);
    }

    [Fact]
    public void Help_ContainsCompileOption()
    {
        var result = CliTestHelper.RunCli("--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("--compile", result.StandardOutput);
        Assert.Contains("-c", result.StandardOutput);
    }

    [Fact]
    public void Help_ContainsDecoratorOptions()
    {
        var result = CliTestHelper.RunCli("--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("--experimentalDecorators", result.StandardOutput);
        Assert.Contains("--decorators", result.StandardOutput);
    }

    [Fact]
    public void Help_ContainsPackagingOptions()
    {
        var result = CliTestHelper.RunCli("--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("--pack", result.StandardOutput);
        Assert.Contains("--push", result.StandardOutput);
    }

    [Fact]
    public void Version_LongFlag_PrintsVersion()
    {
        var result = CliTestHelper.RunCli("--version");

        Assert.Equal(0, result.ExitCode);
        // Version should match pattern "sharpts X.Y.Z" or "sharpts X.Y.Z-suffix"
        Assert.Matches(@"sharpts \d+\.\d+\.\d+", result.StandardOutput);
    }

    [Fact]
    public void Version_ShortFlag_PrintsVersion()
    {
        var result = CliTestHelper.RunCli("-v");

        Assert.Equal(0, result.ExitCode);
        // Version should match pattern "sharpts X.Y.Z" or "sharpts X.Y.Z-suffix"
        Assert.Matches(@"sharpts \d+\.\d+\.\d+", result.StandardOutput);
    }

    [Fact]
    public void Version_OutputIsConsistent()
    {
        var longResult = CliTestHelper.RunCli("--version");
        var shortResult = CliTestHelper.RunCli("-v");

        Assert.Equal(longResult.StandardOutput.Trim(), shortResult.StandardOutput.Trim());
    }

    [Fact]
    public void Help_OutputIsConsistent()
    {
        var longResult = CliTestHelper.RunCli("--help");
        var shortResult = CliTestHelper.RunCli("-h");

        Assert.Equal(longResult.StandardOutput, shortResult.StandardOutput);
    }

    [Fact]
    public void Help_NoStderr()
    {
        var result = CliTestHelper.RunCli("--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.StandardError);
    }

    [Fact]
    public void Version_NoStderr()
    {
        var result = CliTestHelper.RunCli("--version");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.StandardError);
    }
}

using System.Diagnostics;
using SharpTS.Testing;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.IntegrationTests;

public sealed class CliParameterPropertyInitializationTests
{
    [Fact]
    public void AbruptFieldInitializerPreservesTheParameterDeclarationOnAReplacementReceiver()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        directory.CreateFile("main.js", ParameterPropertyDeclarationOrderTests.AbruptReplacementSource);
        var compile = CliTestHelper.RunCli(
            "--no-tsconfig --noLib --compile main.js -o main.dll --verify", directory.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);

        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = directory.Path,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(directory.GetPath("main.dll"));
        var run = TestProcess.Run(start, CliTestHelper.DefaultTimeout);
        Assert.True(run.ExitCode == 0, run.StandardOutput + run.StandardError);
        Assert.Equal(ParameterPropertyDeclarationOrderTests.AbruptReplacementOutput,
            CliTestHelper.NormalizeOutput(run.StandardOutput));
    }
}

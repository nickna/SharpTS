using System.Diagnostics;
using SharpTS.Testing;
using SharpTS.Tests.IntegrationTests;
using Xunit;

namespace SharpTS.Tests.CliTests;

[Collection("ExternalProcessTests")]
public sealed class ManagedOutputNameTests
{
    [Theory]
    [InlineData("System.Console")]
    [InlineData("system.console")]
    [InlineData("System.Private.CoreLib")]
    public void FrameworkOutputNamesAreRejectedBeforeReplacingAnExistingFile(string name)
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string source = directory.CreateFile("main.ts", "console.log(42);");
        string output = directory.CreateFile(name + ".dll", "preserve");
        var result = CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{source}\" -o \"{output}\" --verify", directory.Path);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("would shadow a framework assembly", result.StandardError);
        Assert.Equal("preserve", File.ReadAllText(output));
        Assert.False(File.Exists(Path.ChangeExtension(output, ".runtimeconfig.json")));
    }

    [Fact]
    public async Task SimilarNonFrameworkOutputRetainsEntryPointArgumentsAndExitCode()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string source = directory.CreateFile("main.ts", """
            console.log(42);
            console.log(process.argv.slice(2).join('|'));
            process.exitCode = 7;
            """);
        string output = directory.GetPath("System.Console.Application.dll");
        var compile = CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{source}\" -o \"{output}\" --verify", directory.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed", compile.StandardOutput);
        var start = new ProcessStartInfo("dotnet");
        start.ArgumentList.Add(output);
        start.ArgumentList.Add("first");
        start.ArgumentList.Add("two words");
        var run = await TestProcess.RunAsync(start, TimeSpan.FromSeconds(30));
        Assert.Equal(7, run.ExitCode);
        Assert.Equal("42\nfirst|two words\n", CliTestHelper.NormalizeOutput(run.StandardOutput));
    }
}

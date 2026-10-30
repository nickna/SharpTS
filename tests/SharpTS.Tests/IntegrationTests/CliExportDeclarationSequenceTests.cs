using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.IntegrationTests;

public sealed class CliExportDeclarationSequenceTests
{
    [Theory, MemberData(nameof(ExportDeclarationSequenceTests.Cases), MemberType = typeof(ExportDeclarationSequenceTests))]
    public void CheckedModuleGraphsAcceptAllSourceBindings(string name, string producer, string consumer, string expected)
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        directory.CreateFile("producer.ts", producer);
        var entry = directory.CreateFile("main.ts", consumer);
        var run = CliTestHelper.RunCli($"--no-tsconfig \"{entry}\"", directory.Path);
        Assert.True(run.ExitCode == 0, run.StandardOutput + run.StandardError);
        Assert.Equal(expected, run.StandardOutput);
        Assert.Empty(run.StandardError);

        var image = directory.GetPath(name + ".dll");
        var compile = CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{entry}\" --target dll -o \"{image}\" --verify", directory.Path, TimeSpan.FromSeconds(60));
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        Assert.Empty(compile.StandardError);
    }
}

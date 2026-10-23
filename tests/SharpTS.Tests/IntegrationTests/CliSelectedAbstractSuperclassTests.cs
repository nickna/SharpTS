using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.IntegrationTests;

public sealed class CliSelectedAbstractSuperclassTests
{
    [Theory, MemberData(nameof(SelectedAbstractSuperclassTests.Cases), MemberType = typeof(SelectedAbstractSuperclassTests))]
    public void CheckedSelectedAbstractSuperclassesCompile(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.NotEmpty(expected);
        using var directory = CliTestHelper.CreateTempDirectory();
        var sourcePath = directory.CreateFile("main.ts", source);
        var output = directory.GetPath("selected-abstract-super-" + name + ".dll");
        var compile = CliTestHelper.RunCli(
            $"--no-tsconfig --strict --noLib --compile \"{sourcePath}\" -o \"{output}\" --standalone --verify",
            directory.Path, TimeSpan.FromSeconds(60));
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        Assert.Empty(compile.StandardError);
        Assert.True(File.Exists(output));
    }
}

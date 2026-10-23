using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.IntegrationTests;

public sealed class CliArrayPrototypeOwnDescriptorTests
{
    [Theory, MemberData(nameof(ArrayPrototypeOwnDescriptorTests.Cases), MemberType = typeof(ArrayPrototypeOwnDescriptorTests))]
    public void CheckedInheritedIndexPushCompiles(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.NotEmpty(expected);
        using var directory = CliTestHelper.CreateTempDirectory();
        var sourcePath = directory.CreateFile("main.ts", source);
        var output = directory.GetPath("array-prototype-own-descriptor-" + name + ".dll");
        var compile = CliTestHelper.RunCli(
            $"--no-tsconfig --strict --noLib --compile \"{sourcePath}\" -o \"{output}\" --standalone --verify",
            directory.Path, TimeSpan.FromSeconds(60));
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        Assert.Empty(compile.StandardError);
        Assert.True(File.Exists(output));
    }

    [Theory, MemberData(nameof(ArrayPrototypeOwnDescriptorTests.Cases), MemberType = typeof(ArrayPrototypeOwnDescriptorTests))]
    public void CheckedInheritedIndexPushExecutes(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        using var directory = CliTestHelper.CreateTempDirectory();
        var sourcePath = directory.CreateFile("main.ts", source);
        var result = CliTestHelper.RunCli(
            $"--no-tsconfig --strict --noLib \"{sourcePath}\"",
            directory.Path, TimeSpan.FromSeconds(30));
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        Assert.Empty(result.StandardError);
        Assert.Equal(expected, result.StandardOutput.Replace("\r\n", "\n"));
    }
}

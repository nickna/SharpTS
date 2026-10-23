using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.IntegrationTests;

public sealed class CliComputedObjectBindingTests
{
    [Theory, MemberData(nameof(ComputedObjectBindingTests.Cases), MemberType = typeof(ComputedObjectBindingTests))]
    public void CheckedComputedObjectBindingsCompile(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.NotEmpty(expected);
        using var directory = CliTestHelper.CreateTempDirectory();
        var sourcePath = directory.CreateFile("main.ts", source);
        var output = directory.GetPath("computed-object-binding-" + name + ".dll");
        var compile = CliTestHelper.RunCli(
            $"--no-tsconfig --strict --noLib --compile \"{sourcePath}\" -o \"{output}\" --standalone --verify",
            directory.Path, TimeSpan.FromSeconds(60));
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        Assert.Empty(compile.StandardError);
        Assert.True(File.Exists(output));
    }
}

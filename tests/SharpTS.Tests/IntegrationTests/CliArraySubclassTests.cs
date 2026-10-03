using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.IntegrationTests;

public sealed class CliArraySubclassTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeclarationLibrariesPreserveArraySubclassRuntime(bool noLib)
    {
        const string source = """
            class Typed extends Array<number> {}
            class Plain extends Array {}
            const typed = new Typed(3);
            typed[1] = 7;
            const plain: any = new Plain("a", "b");
            console.log(typed.length, 0 in typed, typed[1]);
            console.log(plain.length, plain[0], plain[1]);
            console.log(typed instanceof Typed, typed instanceof Array, Array.isArray(typed));
            """;
        using var directory = CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        var result = CliTestHelper.RunCli(
            $"--no-tsconfig{(noLib ? " --noLib" : "")} \"{path}\"", directory.Path);
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        Assert.Equal("3 false 7\n2 a b\ntrue true true\n", result.StandardOutput);
        Assert.Equal(result.StandardOutput, TestHarness.RunInterpreted(source));
        Assert.Equal(result.StandardOutput, TestHarness.RunCompiled(source));
    }

    [Theory]
    [InlineData("function check() { const Array = 1; class Values extends Array {} }", "Superclass must be a class")]
    [InlineData("class Array {} class Values extends Array<number> {}", "Cannot use type arguments")]
    [InlineData("class Values extends Array<number, string> {}", "No base constructor")]
    public void LoadedArrayBridgePreservesShadowingAndTypeArgumentChecks(string source, string diagnostic)
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        var result = CliTestHelper.RunCli($"--no-tsconfig --compile \"{path}\"", directory.Path);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(diagnostic, result.StandardOutput + result.StandardError);
    }
}

using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.IntegrationTests;

public sealed class CliNativeClassExpressionConstructorTests
{
    [Theory, MemberData(nameof(NativeClassExpressionConstructorTests.Cases), MemberType = typeof(NativeClassExpressionConstructorTests))]
    public void NoLibClassExpressionsInheritNativeConstructorArguments(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        using var directory = CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        var result = CliTestHelper.RunCli($"--no-tsconfig --noLib \"{path}\"", directory.Path);
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        Assert.Empty(result.StandardError);
        Assert.Equal(expected, result.StandardOutput);
    }
}

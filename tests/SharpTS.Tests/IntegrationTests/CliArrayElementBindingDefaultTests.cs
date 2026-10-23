using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.IntegrationTests;

public sealed class CliArrayElementBindingDefaultTests
{
    [Theory, MemberData(nameof(ArrayElementBindingDefaultTests.Cases), MemberType = typeof(ArrayElementBindingDefaultTests))]
    public void CheckedArrayElementsApplyNestedBindingDefaultsOnce(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        using var directory = CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        var result = CliTestHelper.RunCli(
            $"--no-tsconfig --strict --noLib \"{path}\"", directory.Path, TimeSpan.FromSeconds(60));
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        Assert.Empty(result.StandardError);
        Assert.Equal(expected, result.StandardOutput);
    }
}

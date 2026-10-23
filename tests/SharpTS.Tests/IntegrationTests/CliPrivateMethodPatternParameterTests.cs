using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.IntegrationTests;

public sealed class CliPrivateMethodPatternParameterTests
{
    [Theory, MemberData(nameof(PrivateMethodPatternParameterTests.Cases), MemberType = typeof(PrivateMethodPatternParameterTests))]
    public void CheckedPrivateMethodPatternsBindBeforeLaterDefaults(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        using var directory = CliTestHelper.CreateTempDirectory();
        var sourcePath = directory.CreateFile("main.ts", source);
        var result = CliTestHelper.RunCli(
            $"--no-tsconfig --strict --noLib \"{sourcePath}\"", directory.Path);
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        Assert.Equal(expected, result.StandardOutput);
        Assert.Empty(result.StandardError);
    }
}

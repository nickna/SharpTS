using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.IntegrationTests;

public sealed class CliPrimitiveAwaitSchedulingTests
{
    [Theory, MemberData(nameof(PrimitiveAwaitSchedulingTests.Cases), MemberType = typeof(PrimitiveAwaitSchedulingTests))]
    public void CheckedInterpreterResumesPrimitiveAwaitInPromiseOrder(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        using var directory = CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        var result = CliTestHelper.RunCli($"--no-tsconfig --strict --noLib \"{path}\"", directory.Path);
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        Assert.Equal(expected, result.StandardOutput);
        Assert.Empty(result.StandardError);
    }
}

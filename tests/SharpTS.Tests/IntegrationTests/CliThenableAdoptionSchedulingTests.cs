using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.IntegrationTests;

public sealed class CliThenableAdoptionSchedulingTests
{
    [Theory, MemberData(nameof(ThenableAdoptionSchedulingTests.Cases), MemberType = typeof(ThenableAdoptionSchedulingTests))]
    public void CheckedInterpreterDrainsNestedThenableJobsBeforeTimers(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        using var directory = CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        var result = CliTestHelper.RunCli($"--no-tsconfig --strict \"{path}\"", directory.Path);
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        Assert.Equal(expected, result.StandardOutput);
        Assert.Empty(result.StandardError);
    }
}

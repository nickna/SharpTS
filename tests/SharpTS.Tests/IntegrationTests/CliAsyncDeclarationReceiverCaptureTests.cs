using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.IntegrationTests;

public sealed class CliAsyncDeclarationReceiverCaptureTests
{
    [Theory, MemberData(nameof(AsyncDeclarationReceiverCaptureTests.Cases), MemberType = typeof(AsyncDeclarationReceiverCaptureTests))]
    public void CheckedInterpreterPreservesAsyncDeclarationCaptures(
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

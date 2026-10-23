using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.IntegrationTests;

public sealed class CliHoistedLocalFunctionVarPreservationTests
{
    [Theory, MemberData(nameof(HoistedLocalFunctionVarPreservationTests.Cases), MemberType = typeof(HoistedLocalFunctionVarPreservationTests))]
    public void CheckedInitializerFreeVarsPreserveHoistedLocalFunctions(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        using var directory = CliTestHelper.CreateTempDirectory();
        var sourcePath = directory.CreateFile("main.ts", source);
        var check = CliTestHelper.RunCli(
            $"--no-tsconfig --noLib --noEmit \"{sourcePath}\"", directory.Path);
        Assert.True(check.ExitCode == 0, check.StandardOutput + check.StandardError);
        Assert.Empty(check.StandardError);

        var result = CliTestHelper.RunCli(
            $"--no-tsconfig --noLib \"{sourcePath}\"", directory.Path);
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        Assert.Equal(expected, result.StandardOutput);
        Assert.Empty(result.StandardError);
    }
}

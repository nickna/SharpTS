using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, MemberData(nameof(ArrayPrototypeLengthRead3315Tests.LibraryModes), MemberType = typeof(ArrayPrototypeLengthRead3315Tests))]
    public void Isolated_Issue3315ArrayPrototypeLength_VerifiesBothLibraryProfiles(
        string name, string source, string expected, bool noLib)
    {
        Assert.NotEmpty(name);
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var input = directory.CreateFile("main.ts", source);
        var output = directory.GetPath("prototype-length.dll");
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig{(noLib ? " --noLib" : "")} --compile \"{input}\" -o \"{output}\" --standalone --verify",
            directory.Path, TimeSpan.FromSeconds(60));
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        Assert.Equal(expected, ExecuteCompiledDllIsolated(output, timeoutMs: 30000,
            verifyStandardError: error => Assert.Empty(error), standardInput: ""));
    }
}

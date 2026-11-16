using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.IntegrationTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, DeploymentData(nameof(CliPromiseSubclassTests.LibraryModes), MemberType = typeof(CliPromiseSubclassTests))]
    public void Isolated_Issue2169DefaultLibraryPromiseHeritage_VerifiesBeforeExecution(
        string name, string source, string expected, bool noLib)
    {
        Assert.NotEmpty(name);
        using var directory = CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        var output = directory.GetPath("promise-heritage.dll");
        var compile = CliTestHelper.RunCli(
            $"--no-tsconfig{(noLib ? " --noLib" : "")} --compile \"{path}\" -o \"{output}\" --standalone --verify",
            directory.Path, TimeSpan.FromSeconds(60));
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        Assert.DoesNotContain("SharpTS", GetAssemblyReferences(output));
        Assert.False(File.Exists(directory.GetPath("SharpTS.dll")));
        Assert.Equal(expected, ExecuteCompiledDllIsolated(output, timeoutMs: 30000,
            verifyStandardError: error => Assert.Empty(error), standardInput: ""));
    }
}
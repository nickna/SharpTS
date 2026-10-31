using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, MemberData(nameof(ReflectArrayLikeArgumentListTests.Cases),
        MemberType = typeof(ReflectArrayLikeArgumentListTests))]
    public void Isolated_Issue3110ReflectArrayLikeArguments_VerifyBeforeExecution(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        var output = directory.GetPath("reflect-array-like-" + name + ".dll");
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{path}\" -o \"{output}\" --standalone --verify",
            directory.Path, TimeSpan.FromSeconds(60));
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        Assert.DoesNotContain("SharpTS", GetAssemblyReferences(output));
        Assert.Equal(expected, ExecuteCompiledDllIsolated(output, timeoutMs: 30000,
            verifyStandardError: error => Assert.Empty(error), standardInput: ""));
    }
}

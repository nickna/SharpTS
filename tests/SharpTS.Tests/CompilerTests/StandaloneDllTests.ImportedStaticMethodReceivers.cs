using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, DeploymentData(nameof(ImportedStaticMethodReceiverTests.Cases),
        MemberType = typeof(ImportedStaticMethodReceiverTests))]
    public void Isolated_Issue2242ImportedStaticMethods_VerifyBeforeExecution(
        string name, string entry, string expected)
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        foreach (var (file, source) in ImportedStaticMethodReceiverTests.Files(entry))
            directory.CreateFile(file, source);
        var path = directory.GetPath("main.ts");
        var output = directory.GetPath(name + ".dll");
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{path}\" -o \"{output}\" --standalone --verify",
            directory.Path, TimeSpan.FromSeconds(60));
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        Assert.DoesNotContain("SharpTS", GetAssemblyReferences(output));
        Assert.False(File.Exists(directory.GetPath("SharpTS.dll")));
        Assert.Equal(expected, ExecuteCompiledDllIsolated(output, timeoutMs: 30000,
            verifyStandardError: error => Assert.Empty(error), standardInput: ""));
    }
}

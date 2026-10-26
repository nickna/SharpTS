using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Fact]
    public void Isolated_Issue2980EvalPrimitiveThrows_VerifyBeforeExecution()
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var source = directory.CreateFile("main.ts", EvalPrimitiveThrowTests.DeploymentSource);
        var output = directory.GetPath("eval-primitive-throws.dll");
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{source}\" -o \"{output}\" --verify", directory.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        Assert.DoesNotContain("SharpTS", GetAssemblyReferences(output));
        Assert.True(File.Exists(directory.GetPath("SharpTS.dll")));
        Assert.Equal("7\nundefined\ntrue\n", ExecuteCompiledDllIsolated(output, timeoutMs: 10000,
            verifyStandardError: error => Assert.Empty(error)));
    }
}

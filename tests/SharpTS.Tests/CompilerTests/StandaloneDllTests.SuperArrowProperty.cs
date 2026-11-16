using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Fact]
    public void Saved_Issue2085GenericSuperArrow_VerifyBeforeExecution()
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", SuperArrowPropertyTests.GenericSource);
        var output = directory.GetPath("generic.dll");
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{path}\" -o \"{output}\" --standalone --verify",
            directory.Path, TimeSpan.FromSeconds(60));
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        Assert.DoesNotContain("SharpTS", GetAssemblyReferences(output));
        Assert.Equal(SuperArrowPropertyTests.GenericExpected, ExecuteCompiledDllIsolated(output, timeoutMs: 30000,
            verifyStandardError: error => Assert.Empty(error), standardInput: ""));
    }

    [Theory]
    [DeploymentData(nameof(SuperArrowPropertyTests.Cases), MemberType = typeof(SuperArrowPropertyTests))]
    public void Saved_Issue2085SuperArrow_VerifyBeforeExecution(string name, string source, string expected)
    {
        // The source is JavaScript: undeclared prototype properties intentionally
        // bypass TypeScript checking, while saved IL must verify before execution.
        var bytes = TestHarness.CompileUncheckedAndVerify(source);
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var output = directory.GetPath(name + ".dll");
        File.WriteAllBytes(output, bytes);
        directory.CreateFile(name + ".runtimeconfig.json", """
            { "runtimeOptions": { "tfm": "net10.0", "framework": {
                "name": "Microsoft.NETCore.App", "version": "10.0.0"
            } } }
            """);
        Assert.DoesNotContain("SharpTS", GetAssemblyReferences(output));
        Assert.False(File.Exists(directory.GetPath("SharpTS.dll")));
        Assert.Equal(expected, ExecuteCompiledDllIsolated(output, timeoutMs: 30000,
            verifyStandardError: error => Assert.Empty(error), standardInput: ""));
    }
}

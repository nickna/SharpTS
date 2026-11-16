using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Fact]
    public void Saved_Issue2099DeclaredAccessorBarriers_VerifyBeforeExecution()
    {
        var bytes = TestHarness.CompileUncheckedAndVerify(LiveSuperMethodCallTests.AccessorBarrierSource);
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var output = directory.GetPath("accessor-barriers.dll");
        File.WriteAllBytes(output, bytes);
        directory.CreateFile("accessor-barriers.runtimeconfig.json", """
            { "runtimeOptions": { "tfm": "net10.0", "framework": {
                "name": "Microsoft.NETCore.App", "version": "10.0.0"
            } } }
            """);
        Assert.DoesNotContain("SharpTS", GetAssemblyReferences(output));
        Assert.False(File.Exists(directory.GetPath("SharpTS.dll")));
        Assert.Equal(LiveSuperMethodCallTests.AccessorBarrierExpected,
            ExecuteCompiledDllIsolated(output, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error), standardInput: ""));
    }

    [Theory, DeploymentData(nameof(LiveSuperMethodCallTests.Cases), MemberType = typeof(LiveSuperMethodCallTests))]
    public void Isolated_Issue2099SuperCalls_ReadLivePropertiesBeforeArguments(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        var output = directory.GetPath("live-super-method-calls.dll");
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

using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, DeploymentData(nameof(InheritedProxySymbolReadTests.Cases), MemberType = typeof(InheritedProxySymbolReadTests))]
    public void Saved_Issue2131InheritedProxySymbolReads_UseDeclaredOptionalRuntime(
        string name, string source, string expected)
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        var output = directory.GetPath("inherited-proxy-symbol-" + name + ".dll");
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{path}\" -o \"{output}\" --standalone --verify",
            directory.Path, TimeSpan.FromSeconds(60));
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        Assert.Contains("Note: output uses features needing the SharpTS runtime (Proxy)",
            compile.StandardOutput + compile.StandardError);
        Assert.DoesNotContain("SharpTS", GetAssemblyReferences(output));
        var runtime = directory.GetPath("SharpTS.dll");
        Assert.False(File.Exists(runtime));
        // Proxy's declared optional dependency is required to execute this saved output.
        File.Copy(typeof(TypeChecker).Assembly.Location, runtime);
        Assert.Equal(File.ReadAllBytes(typeof(TypeChecker).Assembly.Location), File.ReadAllBytes(runtime));
        Assert.Equal(expected, ExecuteCompiledDllIsolated(output, timeoutMs: 30000,
            verifyStandardError: error => Assert.Empty(error), standardInput: ""));
    }
}

using System.Security.Cryptography;
using SharpTS.Tests.SharedTests;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, MemberData(nameof(ClassPrototypeProxyEnumerationTests.SavedCases), MemberType = typeof(ClassPrototypeProxyEnumerationTests))]
    public void Saved_Issue2676ClassPrototypeProxyEnumeration_DeploysMatchingManagedRuntime(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = directory.CreateFile("main.ts", source);
        var output = directory.GetPath("class-prototype-proxy-" + name + ".dll");
        // Proxy uses the managed bridge. This deliberately deploys its runtime rather than claiming portability.
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --noLib --compile \"{sourcePath}\" -o \"{output}\" --verify",
            directory.Path, TimeSpan.FromSeconds(60));

        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        Assert.All(GetAssemblyReferences(output), reference => Assert.True(
            reference == "SharpTS" || reference.StartsWith("System.", StringComparison.Ordinal),
            "Unexpected managed-Proxy program reference: " + reference));
        var runtime = directory.GetPath("SharpTS.dll");
        Assert.True(File.Exists(runtime));
        var expectedRuntimeHash = SHA256.HashData(File.ReadAllBytes(typeof(TypeChecker).Assembly.Location));
        Assert.Equal(expectedRuntimeHash, SHA256.HashData(File.ReadAllBytes(runtime)));
        Assert.Equal(expected, ExecuteCompiledDllIsolated(output, timeoutMs: 30000,
            verifyStandardError: error => Assert.Empty(error), standardInput: ""));
        Assert.Equal(expectedRuntimeHash, SHA256.HashData(File.ReadAllBytes(runtime)));
    }
}

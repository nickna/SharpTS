using System.Security.Cryptography;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory]
    [MemberData(nameof(SharedTests.WeakMapAwaitTests.Cases), MemberType = typeof(SharedTests.WeakMapAwaitTests))]
    public void Isolated_WeakMapAwait_PreservesReceiverAndArgumentSlots(
        string name, string source, string expected)
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        var output = directory.GetPath(name + ".dll");
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --strict --noLib --compile \"{path}\" -o \"{output}\" --standalone --verify",
            directory.Path, TimeSpan.FromSeconds(60));
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Empty(compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        Assert.DoesNotContain("[IL Error]", compile.StandardOutput);
        Assert.DoesNotContain("omitted", compile.StandardOutput, StringComparison.OrdinalIgnoreCase);
        var references = GetAssemblyReferences(output);
        Assert.NotEmpty(references);
        Assert.All(references, reference => Assert.True(
            reference == "System" || reference.StartsWith("System.", StringComparison.Ordinal), reference));
        Assert.False(File.Exists(directory.GetPath("SharpTS.dll")));
        var before = SHA256.HashData(File.ReadAllBytes(output));
        Assert.Equal(expected, ExecuteCompiledDllIsolated(output, timeoutMs: 30000,
            verifyStandardError: error => Assert.Empty(error), standardInput: ""));
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(output)));
        Assert.False(File.Exists(directory.GetPath("SharpTS.dll")));
    }
}

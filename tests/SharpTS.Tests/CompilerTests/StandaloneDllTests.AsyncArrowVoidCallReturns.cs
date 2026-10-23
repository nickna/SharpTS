using System.Security.Cryptography;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, MemberData(nameof(AsyncArrowVoidCallReturnTests.Cases), MemberType = typeof(AsyncArrowVoidCallReturnTests))]
    public void Isolated_Issue2810AsyncArrowVoidCallReturns_VerifyBeforeExecution(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = directory.CreateFile("main.ts", source);
        var output = directory.GetPath("async-arrow-void-call-returns-" + name + ".dll");
        var admission = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --strict --noLib --noEmit \"{sourcePath}\"",
            directory.Path, TimeSpan.FromSeconds(60));
        Assert.True(admission.ExitCode == 0, admission.StandardOutput + admission.StandardError);
        Assert.Empty(admission.StandardOutput);
        Assert.Empty(admission.StandardError);

        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --strict --noLib --compile \"{sourcePath}\" -o \"{output}\" --standalone --verify",
            directory.Path, TimeSpan.FromSeconds(60));

        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Empty(compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        Assert.DoesNotContain("omitted", compile.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("skipped", compile.StandardOutput, StringComparison.OrdinalIgnoreCase);
        var references = GetAssemblyReferences(output);
        Assert.NotEmpty(references);
        Assert.All(references, reference => Assert.StartsWith("System.", reference));
        Assert.False(File.Exists(directory.GetPath("SharpTS.dll")));
        var verifiedHash = SHA256.HashData(File.ReadAllBytes(output));

        Assert.Equal(expected, ExecuteCompiledDllIsolated(output, timeoutMs: 20000,
            verifyStandardError: error => Assert.Empty(error), standardInput: ""));

        Assert.Equal(verifiedHash, SHA256.HashData(File.ReadAllBytes(output)));
        Assert.False(File.Exists(directory.GetPath("SharpTS.dll")));
    }
}

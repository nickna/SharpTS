using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, MemberData(nameof(ComputedDeclareFieldErasureTests.SavedCases), MemberType = typeof(ComputedDeclareFieldErasureTests))]
    public void Isolated_Issue2713ComputedDeclareFields_VerifyBeforeExecution(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        if (name == "ordinary-computed-key-and-field-initializer-order-survive-erasure")
        {
            using var restoredDirectory = IntegrationTests.CliTestHelper.CreateTempDirectory();
            var restoredSource = restoredDirectory.CreateFile("main.ts", source);
            var restoredOutput = restoredDirectory.GetPath("computed-declare-field-" + name + ".dll");
            var admission = IntegrationTests.CliTestHelper.RunCli(
                $"--no-tsconfig --strict --noLib --noEmit \"{restoredSource}\"",
                restoredDirectory.Path, TimeSpan.FromSeconds(60));
            Assert.True(admission.ExitCode == 0, admission.StandardOutput + admission.StandardError);
            Assert.Empty(admission.StandardOutput);
            Assert.Empty(admission.StandardError);

            var restoredCompile = IntegrationTests.CliTestHelper.RunCli(
                $"--no-tsconfig --strict --noLib --compile \"{restoredSource}\" -o \"{restoredOutput}\" --standalone --verify",
                restoredDirectory.Path, TimeSpan.FromSeconds(60));
            Assert.True(restoredCompile.ExitCode == 0, restoredCompile.StandardOutput + restoredCompile.StandardError);
            Assert.Empty(restoredCompile.StandardError);
            Assert.Contains("IL verification passed.", restoredCompile.StandardOutput);
            Assert.DoesNotContain("omitted", restoredCompile.StandardOutput, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("skipped", restoredCompile.StandardOutput, StringComparison.OrdinalIgnoreCase);
            var references = GetAssemblyReferences(restoredOutput);
            Assert.NotEmpty(references);
            Assert.All(references, reference => Assert.StartsWith("System.", reference));
            Assert.False(File.Exists(restoredDirectory.GetPath("SharpTS.dll")));
            var verifiedHash = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(restoredOutput));

            Assert.Equal(expected, ExecuteCompiledDllIsolated(restoredOutput, timeoutMs: 20000,
                verifyStandardError: error => Assert.Empty(error), standardInput: ""));

            Assert.Equal(verifiedHash, System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(restoredOutput)));
            Assert.False(File.Exists(restoredDirectory.GetPath("SharpTS.dll")));
            return;
        }

        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = directory.CreateFile("main.ts", source);
        var output = directory.GetPath("computed-declare-field-" + name + ".dll");
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --noLib --compile \"{sourcePath}\" -o \"{output}\" --standalone --verify",
            directory.Path, TimeSpan.FromSeconds(60));

        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        Assert.All(GetAssemblyReferences(output), reference => Assert.StartsWith("System.", reference));
        Assert.False(File.Exists(directory.GetPath("SharpTS.dll")));
        Assert.Equal(expected, ExecuteCompiledDllIsolated(output, timeoutMs: 10000,
            verifyStandardError: error => Assert.Empty(error), standardInput: ""));
    }
}

using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, MemberData(nameof(PromiseClassExpressionSpeciesTests.SavedCases), MemberType = typeof(PromiseClassExpressionSpeciesTests))]
    public void Isolated_Issue2709PromiseClassExpressionSpecies_VerifyBeforeExecution(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        if (name == "repeated-expression-definitions-retain-captures-and-nominal-child-species")
        {
            using var restoredDirectory = IntegrationTests.CliTestHelper.CreateTempDirectory();
            var restoredSource = restoredDirectory.CreateFile("main.ts", source);
            var restoredOutput = restoredDirectory.GetPath("promise-class-expression-species-" + name + ".dll");
            var restoredCompile = IntegrationTests.CliTestHelper.RunCli(
                $"--no-tsconfig --strict --noLib --compile \"{restoredSource}\" -o \"{restoredOutput}\" --standalone --verify",
                restoredDirectory.Path, TimeSpan.FromSeconds(60));

            Assert.True(restoredCompile.ExitCode == 0, restoredCompile.StandardOutput + restoredCompile.StandardError);
            Assert.Empty(restoredCompile.StandardError);
            Assert.Contains("IL verification passed.", restoredCompile.StandardOutput);
            Assert.DoesNotContain("omitted", restoredCompile.StandardOutput, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("skipped", restoredCompile.StandardOutput, StringComparison.OrdinalIgnoreCase);
            var restoredReferences = GetAssemblyReferences(restoredOutput);
            Assert.NotEmpty(restoredReferences);
            Assert.All(restoredReferences, reference => Assert.StartsWith("System.", reference));
            Assert.False(File.Exists(restoredDirectory.GetPath("SharpTS.dll")));
            var restoredVerifiedHash = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(restoredOutput));

            Assert.Equal(expected, ExecuteCompiledDllIsolated(restoredOutput, timeoutMs: 20000,
                verifyStandardError: error => Assert.Empty(error), standardInput: ""));

            Assert.Equal(restoredVerifiedHash, System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(restoredOutput)));
            Assert.False(File.Exists(restoredDirectory.GetPath("SharpTS.dll")));
            return;
        }

        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = directory.CreateFile("main.ts", source);
        var output = directory.GetPath("promise-class-expression-species-" + name + ".dll");
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

using System.Security.Cryptography;
using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, MemberData(nameof(ClassConstructorOwnKeyMembershipTests.Cases), MemberType = typeof(ClassConstructorOwnKeyMembershipTests))]
    public void Isolated_Issue2633ConstructorOwnKeyMembership_VerifyBeforeExecution(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = directory.CreateFile("main.ts", source);
        var output = directory.GetPath("constructor-own-key-" + name + ".dll");
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --strict --noLib --compile \"{sourcePath}\" -o \"{output}\" --standalone --verify",
            directory.Path, TimeSpan.FromSeconds(60));

        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Empty(compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        Assert.DoesNotMatch(@"(?i)(verification.*(?:omission|skipped)|(?:omission|skipped).*verification)",
            compile.StandardOutput + compile.StandardError);
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

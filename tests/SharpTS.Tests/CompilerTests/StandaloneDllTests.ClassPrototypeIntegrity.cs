using System.Security.Cryptography;
using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, MemberData(nameof(ClassPrototypeIntegrityTests.SavedCases), MemberType = typeof(ClassPrototypeIntegrityTests))]
    public void Isolated_Issue2715ClassPrototypeIntegrity_VerifyBeforeExecution(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        bool fullIntegrityCase = name is "own-named-data-flags-writes-and-absence"
            or "strict-present-deletion-and-absent-deletion-after-integrity"
            or "declared-method-accessor-and-constructor-integrity"
            or "symbol-data-declared-method-accessor-and-carrier-boundaries";
        if (fullIntegrityCase)
        {
            source = source.ReplaceLineEndings("\n") + "\n";
            Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
                .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
        }
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = directory.CreateFile("main.ts", source);
        var output = directory.GetPath("class-prototype-integrity-" + name + ".dll");
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig{(fullIntegrityCase ? " --strict" : "")} --noLib --compile \"{sourcePath}\" -o \"{output}\" --standalone --verify",
            directory.Path, TimeSpan.FromSeconds(60));

        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        if (fullIntegrityCase)
        {
            Assert.Empty(compile.StandardError);
            Assert.DoesNotMatch(@"(?i)(verification.*(?:omission|skipped)|(?:omission|skipped).*verification)",
                compile.StandardOutput + compile.StandardError);
            Assert.NotEmpty(GetAssemblyReferences(output));
        }
        Assert.All(GetAssemblyReferences(output), reference => Assert.StartsWith("System.", reference));
        Assert.False(File.Exists(directory.GetPath("SharpTS.dll")));
        var verifiedHash = fullIntegrityCase ? SHA256.HashData(File.ReadAllBytes(output)) : Array.Empty<byte>();
        Assert.Equal(expected, ExecuteCompiledDllIsolated(output, timeoutMs: fullIntegrityCase ? 20000 : 10000,
            verifyStandardError: error => Assert.Empty(error), standardInput: ""));
        if (fullIntegrityCase)
        {
            Assert.Equal(verifiedHash, SHA256.HashData(File.ReadAllBytes(output)));
            Assert.False(File.Exists(directory.GetPath("SharpTS.dll")));
        }
    }
}

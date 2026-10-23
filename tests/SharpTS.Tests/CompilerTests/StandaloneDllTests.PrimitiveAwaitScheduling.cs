using System.Security.Cryptography;
using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, MemberData(nameof(PrimitiveAwaitSchedulingTests.CompiledCases), MemberType = typeof(PrimitiveAwaitSchedulingTests))]
    public void Isolated_Issue2831PrimitiveAwaitScheduling_VerifyBeforeExecution(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        new TypeChecker(TypeCheckerOptions.Strict).Check(TestHarness.ParseOrThrow(source));
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = directory.CreateFile("main.ts", source);
        var output = directory.GetPath("primitive-await-" + name + ".dll");
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --strict --noLib --compile \"{sourcePath}\" -o \"{output}\" --standalone --verify",
            directory.Path, TimeSpan.FromSeconds(60));

        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Empty(compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        Assert.DoesNotContain("[IL Error]", compile.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IL verification found", compile.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(@"(?i)(verification.*(?:omitted|omission|skipped)|(?:omitted|omission|skipped).*verification)",
            compile.StandardOutput + compile.StandardError);
        var references = GetAssemblyReferences(output);
        Assert.NotEmpty(references);
        Assert.All(references, reference => Assert.StartsWith("System.", reference));
        Assert.False(File.Exists(directory.GetPath("SharpTS.dll")));
        var peHash = SHA256.HashData(File.ReadAllBytes(output));
        var actual = ExecuteCompiledDllIsolated(output, timeoutMs: 20000,
            verifyStandardError: error => Assert.Empty(error), standardInput: "");
        Assert.Equal(peHash, SHA256.HashData(File.ReadAllBytes(output)));
        Assert.False(File.Exists(directory.GetPath("SharpTS.dll")));
        Assert.Equal(expected, actual);
    }
}

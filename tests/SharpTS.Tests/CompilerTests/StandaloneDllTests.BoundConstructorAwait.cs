using System.Security.Cryptography;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory]
    [MemberData(nameof(SharedTests.BoundConstructorAwaitTests.Cases), MemberType = typeof(SharedTests.BoundConstructorAwaitTests))]
    public void Isolated_BoundConstructorAwait_PreservesLiveCalleeAndEvaluationOrder(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        var output = directory.GetPath(name + ".dll");
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --strict --noLib --compile \"{path}\" -o \"{output}\" --standalone --verify",
            directory.Path, TimeSpan.FromSeconds(60));
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Empty(compile.StandardError);
        Assert.True(File.Exists(output));
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        Assert.DoesNotContain("[IL Error]", compile.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IL verification found", compile.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(@"(?i)(verification.*(?:omitted|omission|skipped)|(?:omitted|omission|skipped).*verification)",
            compile.StandardOutput + compile.StandardError);
        var references = GetAssemblyReferences(output);
        Assert.NotEmpty(references);
        Assert.All(references, reference => Assert.True(
            reference == "System" || reference.StartsWith("System.", StringComparison.Ordinal), reference));
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "SharpTS*.dll"));
        var before = SHA256.HashData(File.ReadAllBytes(output));
        var actual = ExecuteCompiledDllIsolated(output, timeoutMs: 30000,
            verifyStandardError: error => Assert.Empty(error), standardInput: "");
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(output)));
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "SharpTS*.dll"));
        Assert.Equal(expected, actual);
    }
}

using System.Security.Cryptography;
using SharpTS.Tests.SharedTests.BuiltInModules;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, MemberData(nameof(ReadableStreamAsyncPullLivenessTests.Names), MemberType = typeof(ReadableStreamAsyncPullLivenessTests))]
    public void Isolated_ReadableStreamAsyncPull_ReturnsWithoutBlockingJobs(string name)
    {
        var (source, expected) = ReadableStreamAsyncPullLivenessTests.GetCase(name);
        ReadableStreamAsyncPullLivenessTests.AssertStrictSource(source);
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var entryPath = directory.CreateFile("main.ts", source);
        var sourceHash = SHA256.HashData(File.ReadAllBytes(entryPath));
        var output = directory.GetPath(name + ".dll");
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --strict --noLib --compile \"{entryPath}\" -o \"{output}\" --standalone --verify",
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
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "SharpTS*.dll", SearchOption.AllDirectories));
        Assert.Equal(sourceHash, SHA256.HashData(File.ReadAllBytes(entryPath)));
        var peHash = SHA256.HashData(File.ReadAllBytes(output));
        var actual = ExecuteCompiledDllIsolated(output, timeoutMs: 20000,
            verifyStandardError: error => Assert.Empty(error), standardInput: "");
        Assert.Equal(peHash, SHA256.HashData(File.ReadAllBytes(output)));
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "SharpTS*.dll", SearchOption.AllDirectories));
        Assert.Equal(sourceHash, SHA256.HashData(File.ReadAllBytes(entryPath)));
        Assert.Equal(expected, actual);
    }
}

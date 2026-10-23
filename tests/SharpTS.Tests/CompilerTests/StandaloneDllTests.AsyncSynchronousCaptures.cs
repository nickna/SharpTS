using System.Security.Cryptography;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, MemberData(nameof(AsyncSynchronousCaptureLivenessTests.Names), MemberType = typeof(AsyncSynchronousCaptureLivenessTests))]
    public void Isolated_AsyncSynchronousCaptures_RetainSourcesAcrossSuspension(
        string name)
    {
        Assert.NotEmpty(name);
        var (files, expected) = AsyncSynchronousCaptureLivenessTests.GetCase(name);
        AsyncSynchronousCaptureLivenessTests.AssertStrictModules(files);
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourceHashes = new Dictionary<string, byte[]>();
        foreach (var (path, source) in files)
        {
            var actualPath = directory.CreateFile(path, source);
            sourceHashes[actualPath] = SHA256.HashData(File.ReadAllBytes(actualPath));
        }
        var entryPath = directory.GetPath("main.ts");
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
        foreach (var (path, hash) in sourceHashes)
            Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(path)));
        var peHash = SHA256.HashData(File.ReadAllBytes(output));
        var actual = ExecuteCompiledDllIsolated(output, timeoutMs: 20000,
            verifyStandardError: error => Assert.Empty(error), standardInput: "");
        Assert.Equal(peHash, SHA256.HashData(File.ReadAllBytes(output)));
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "SharpTS*.dll", SearchOption.AllDirectories));
        foreach (var (path, hash) in sourceHashes)
            Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(path)));
        Assert.Equal(expected, actual);
    }
}

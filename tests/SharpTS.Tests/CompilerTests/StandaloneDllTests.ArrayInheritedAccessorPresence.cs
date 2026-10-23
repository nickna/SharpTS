using System.Security.Cryptography;
using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, MemberData(nameof(ArrayInheritedAccessorPresenceTests.CompiledCases), MemberType = typeof(ArrayInheritedAccessorPresenceTests))]
    public void Isolated_Issue2723ArrayInheritedAccessorPresence_VerifyBeforeExecution(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        var fullOwnDataCase = name == "own-index-and-writable-inherited-data-preserve-attributes";
        if (fullOwnDataCase)
        {
            source = source.ReplaceLineEndings("\n") + "\n";
            Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
                .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
        }
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = directory.CreateFile("main.ts", source);
        var output = directory.GetPath("array-inherited-accessor-presence-" + name + ".dll");
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig{(fullOwnDataCase ? " --strict" : "")} --noLib --compile \"{sourcePath}\" -o \"{output}\" --standalone --verify",
            directory.Path, TimeSpan.FromSeconds(60));

        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(output);
        if (fullOwnDataCase)
        {
            Assert.Empty(compile.StandardError);
            Assert.DoesNotMatch(@"(?i)(verification.*(?:omission|skipped)|(?:omission|skipped).*verification)",
                compile.StandardOutput + compile.StandardError);
            Assert.NotEmpty(references);
        }
        Assert.All(references, reference => Assert.StartsWith("System.", reference));
        Assert.False(File.Exists(directory.GetPath("SharpTS.dll")));
        var peHash = fullOwnDataCase ? SHA256.HashData(File.ReadAllBytes(output)) : Array.Empty<byte>();
        var actual = ExecuteCompiledDllIsolated(output, timeoutMs: fullOwnDataCase ? 20000 : 10000,
            verifyStandardError: error => Assert.Empty(error), standardInput: "");
        if (fullOwnDataCase)
        {
            Assert.Equal(peHash, SHA256.HashData(File.ReadAllBytes(output)));
            Assert.False(File.Exists(directory.GetPath("SharpTS.dll")));
        }
        Assert.Equal(expected, actual);
    }
}

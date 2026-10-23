using System.Security.Cryptography;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Fact]
    public void Isolated_Issue2886ArrayBufferPrototype_VerifyBeforeExecution()
    {
        var source = """
            const constructor: any = ArrayBuffer;
            console.log(typeof constructor.prototype);
            """.ReplaceLineEndings("\n") + "\n";
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);

        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = directory.CreateFile("main.ts", source);
        var output = directory.GetPath("arraybuffer-prototype.dll");
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
        Assert.Equal("object\n", actual);
    }
}

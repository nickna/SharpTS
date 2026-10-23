using System.Security.Cryptography;
using SharpTS.Compilation;
using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, MemberData(nameof(ProxyObjectAssignReceiverTests.Cases), MemberType = typeof(ProxyObjectAssignReceiverTests))]
    public void Isolated_ProxyObjectAssignReceiver_VerifyBeforeExecution(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = directory.CreateFile("main.ts", source);
        var output = directory.GetPath("proxy-object-assign-receiver-" + name + ".dll");
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --strict --noLib --compile \"{sourcePath}\" -o \"{output}\" --verify",
            directory.Path, TimeSpan.FromSeconds(60));

        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Empty(compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        Assert.DoesNotMatch(@"(?i)(verification.*(?:omission|skipped)|(?:omission|skipped).*verification)",
            compile.StandardOutput + compile.StandardError);
        var references = GetAssemblyReferences(output);
        Assert.NotEmpty(references);
        Assert.All(references, reference => Assert.StartsWith("System.", reference));
        // Proxy construction intentionally uses the declared late-bound SharpTS runtime dependency.
        // Normal deployment copies that runtime; the guest still has only System hard references.
        var runtimePath = directory.GetPath("SharpTS.dll");
        Assert.True(File.Exists(runtimePath));
        var runtimeHash = SHA256.HashData(File.ReadAllBytes(typeof(RuntimeEmitter).Assembly.Location));
        Assert.Equal(runtimeHash, SHA256.HashData(File.ReadAllBytes(runtimePath)));
        var verifiedHash = SHA256.HashData(File.ReadAllBytes(output));
        var actual = ExecuteCompiledDllIsolated(output, timeoutMs: 20000,
            verifyStandardError: error => Assert.Empty(error), standardInput: "");
        Assert.Equal(verifiedHash, SHA256.HashData(File.ReadAllBytes(output)));
        Assert.Equal(runtimeHash, SHA256.HashData(File.ReadAllBytes(runtimePath)));
        Assert.Equal(expected, actual);
    }
}

using System.Security.Cryptography;
using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, MemberData(nameof(ComputedDeclarationMetadataFieldTests.SavedCases), MemberType = typeof(ComputedDeclarationMetadataFieldTests))]
    public void Isolated_Issue2719ComputedDeclarationMetadataFields_VerifyBeforeExecution(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        bool fullKeyInventoryCase = name is "field-descriptors-own-key-order-and-partial-redefinition"
            or "generic-and-derived-declarations-retain-own-metadata-fields";
        if (fullKeyInventoryCase)
        {
            source = source.ReplaceLineEndings("\n") + "\n";
            Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
                .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
        }
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = directory.CreateFile("main.ts", source);
        var output = directory.GetPath("computed-declaration-metadata-field-" + name + ".dll");
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig{(fullKeyInventoryCase ? " --strict" : "")} --noLib --compile \"{sourcePath}\" -o \"{output}\" --standalone --verify",
            directory.Path, TimeSpan.FromSeconds(60));

        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        if (fullKeyInventoryCase)
        {
            Assert.Empty(compile.StandardError);
            Assert.DoesNotMatch(@"(?i)(verification.*(?:omission|skipped)|(?:omission|skipped).*verification)",
                compile.StandardOutput + compile.StandardError);
            Assert.NotEmpty(GetAssemblyReferences(output));
        }
        Assert.All(GetAssemblyReferences(output), reference => Assert.StartsWith("System.", reference));
        Assert.False(File.Exists(directory.GetPath("SharpTS.dll")));
        var verifiedHash = fullKeyInventoryCase ? SHA256.HashData(File.ReadAllBytes(output)) : Array.Empty<byte>();
        Assert.Equal(expected, ExecuteCompiledDllIsolated(output, timeoutMs: fullKeyInventoryCase ? 20000 : 10000,
            verifyStandardError: error => Assert.Empty(error), standardInput: ""));
        if (fullKeyInventoryCase)
        {
            Assert.Equal(verifiedHash, SHA256.HashData(File.ReadAllBytes(output)));
            Assert.False(File.Exists(directory.GetPath("SharpTS.dll")));
        }
    }
}

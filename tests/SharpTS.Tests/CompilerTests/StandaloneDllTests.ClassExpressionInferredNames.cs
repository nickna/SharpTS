using System.Security.Cryptography;
using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, MemberData(nameof(ClassExpressionInferredNameTests.SavedCases), MemberType = typeof(ClassExpressionInferredNameTests))]
    public void Isolated_Issue2718ClassExpressionInferredNames_VerifyBeforeExecution(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        bool fullNameInferenceCase = name is "variable-kinds-parentheses-and-name-descriptor-views"
            or "nested-and-duplicate-var-initializers-in-sync-and-async-bodies";
        if (fullNameInferenceCase)
        {
            source = source.ReplaceLineEndings("\n") + "\n";
            Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
                .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
        }
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = directory.CreateFile("main.ts", source);
        var output = directory.GetPath("class-expression-inferred-name-" + name + ".dll");
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig{(fullNameInferenceCase ? " --strict" : "")} --noLib --compile \"{sourcePath}\" -o \"{output}\" --standalone --verify",
            directory.Path, TimeSpan.FromSeconds(60));

        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        if (fullNameInferenceCase)
        {
            Assert.Empty(compile.StandardError);
            Assert.DoesNotMatch(@"(?i)(verification.*(?:omission|skipped)|(?:omission|skipped).*verification)",
                compile.StandardOutput + compile.StandardError);
            Assert.NotEmpty(GetAssemblyReferences(output));
        }
        Assert.All(GetAssemblyReferences(output), reference => Assert.StartsWith("System.", reference));
        Assert.False(File.Exists(directory.GetPath("SharpTS.dll")));
        var verifiedHash = fullNameInferenceCase ? SHA256.HashData(File.ReadAllBytes(output)) : Array.Empty<byte>();
        Assert.Equal(expected, ExecuteCompiledDllIsolated(output, timeoutMs: fullNameInferenceCase ? 20000 : 10000,
            verifyStandardError: error => Assert.Empty(error), standardInput: ""));
        if (fullNameInferenceCase)
        {
            Assert.Equal(verifiedHash, SHA256.HashData(File.ReadAllBytes(output)));
            Assert.False(File.Exists(directory.GetPath("SharpTS.dll")));
        }
    }
}

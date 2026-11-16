using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Fact]
    public void Saved_Issue2096ForwardClassCapture_VerifyBeforeExecution()
    {
        var bytes = TestHarness.CompileUncheckedAndVerify(RepeatedLocalClassDeclarationTests.ForwardCaptureSource);
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var output = directory.GetPath("forward-class-capture.dll");
        File.WriteAllBytes(output, bytes);
        directory.CreateFile("forward-class-capture.runtimeconfig.json", """
            { "runtimeOptions": { "tfm": "net10.0", "framework": {
                "name": "Microsoft.NETCore.App", "version": "10.0.0"
            } } }
            """);
        Assert.DoesNotContain("SharpTS", GetAssemblyReferences(output));
        Assert.False(File.Exists(directory.GetPath("SharpTS.dll")));
        Assert.Equal(RepeatedLocalClassDeclarationTests.ForwardCaptureExpected,
            ExecuteCompiledDllIsolated(output, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error), standardInput: ""));
    }

    [Theory]
    [DeploymentData(nameof(RepeatedLocalClassDeclarationTests.Cases), MemberType = typeof(RepeatedLocalClassDeclarationTests))]
    public void Saved_Issue2096RepeatedLocalClasses_VerifyBeforeExecution(
        string name, string source, string expected)
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        AssertSavedRepeatedLocalClass(directory.Path, path,
            directory.GetPath("repeated-local-class-" + name + ".dll"), expected);
    }

    [Fact]
    public void Saved_Issue2096ImportedFactory_VerifyBeforeExecution()
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        foreach (var (file, source) in RepeatedLocalClassDeclarationTests.ModuleFiles())
            directory.CreateFile(file, source);
        AssertSavedRepeatedLocalClass(directory.Path, directory.GetPath("main.ts"),
            directory.GetPath("repeated-local-class-module.dll"), RepeatedLocalClassDeclarationTests.ModuleExpected);
    }

    private static void AssertSavedRepeatedLocalClass(
        string directory, string source, string output, string expected)
    {
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{source}\" -o \"{output}\" --standalone --verify",
            directory, TimeSpan.FromSeconds(60));
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        Assert.DoesNotContain("SharpTS", GetAssemblyReferences(output));
        Assert.False(File.Exists(Path.Combine(directory, "SharpTS.dll")));
        Assert.Equal(expected, ExecuteCompiledDllIsolated(output, timeoutMs: 30000,
            verifyStandardError: error => Assert.Empty(error), standardInput: ""));
    }
}

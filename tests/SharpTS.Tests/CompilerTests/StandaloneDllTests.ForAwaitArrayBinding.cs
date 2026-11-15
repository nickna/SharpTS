using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Fact]
    public void Isolated_ForAwaitArrayBinding_GeneratorReturnClosesSuspendedHeads()
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.js", ForAwaitArrayBindingCompletionTests.GeneratorReturnSource);
        var output = directory.GetPath("binding-return.dll");
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --noLib --compile \"{path}\" -o \"{output}\" --standalone --verify",
            directory.Path, TimeSpan.FromSeconds(60));
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        Assert.DoesNotContain("SharpTS", GetAssemblyReferences(output));
        Assert.False(File.Exists(directory.GetPath("SharpTS.dll")));
        Assert.Equal(ForAwaitArrayBindingCompletionTests.GeneratorReturnOutput,
            ExecuteCompiledDllIsolated(output, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error), standardInput: ""));
    }

    [Fact]
    public void Isolated_ForAwaitArrayBinding_ClosesPartialAndEmptyHeads()
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.js", ForAwaitArrayBindingTests.PartialSource);
        var output = directory.GetPath("binding.dll");
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --noLib --compile \"{path}\" -o \"{output}\" --standalone --verify",
            directory.Path, TimeSpan.FromSeconds(60));
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        Assert.DoesNotContain("SharpTS", GetAssemblyReferences(output));
        Assert.False(File.Exists(directory.GetPath("SharpTS.dll")));
        Assert.Equal(ForAwaitArrayBindingTests.PartialOutput,
            ExecuteCompiledDllIsolated(output, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error), standardInput: ""));
    }
}

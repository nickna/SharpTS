using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Fact]
    public void Isolated_NestedClosuresKeepTopLevelBlockShadows()
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var source = directory.CreateFile("main.ts", MutableFunctionClosureTests.NestedBlockShadowSource);
        var output = directory.GetPath("nested-block-shadow.dll");
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{source}\" -o \"{output}\" --standalone --verify",
            directory.Path, TimeSpan.FromSeconds(60));
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        Assert.All(GetAssemblyReferences(output), reference => Assert.StartsWith("System.", reference));
        Assert.False(File.Exists(directory.GetPath("SharpTS.dll")));
        Assert.Equal("2\n10\n10\n2\n", ExecuteCompiledDllIsolated(output, timeoutMs: 10000,
            verifyStandardError: error => Assert.Empty(error), standardInput: ""));
    }

    [Theory, SerializableMemberData(nameof(ClassStaticBlockMutableFunctionTests.CapturedNameCollisionCases),
        MemberType = typeof(ClassStaticBlockMutableFunctionTests))]
    public void Isolated_MutableFunctionBindingsIgnoreCapturesInOtherModules(
        string name, Dictionary<string, string> files, string expected)
    {
        Assert.NotEmpty(name);
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        foreach (var (path, source) in files)
            directory.CreateFile(path, source);
        var output = directory.GetPath("mutable-function-collision.dll");
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --strict --noLib --compile \"{directory.GetPath("main.ts")}\" -o \"{output}\" --standalone --verify",
            directory.Path, TimeSpan.FromSeconds(60));

        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        Assert.All(GetAssemblyReferences(output), reference => Assert.StartsWith("System.", reference));
        Assert.False(File.Exists(directory.GetPath("SharpTS.dll")));
        Assert.Equal(expected, ExecuteCompiledDllIsolated(output, timeoutMs: 10000,
            verifyStandardError: error => Assert.Empty(error), standardInput: ""));
    }
}

using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, SerializableMemberData(nameof(ForwardedIntrinsicReturnTests.SavedCases),
        MemberType = typeof(ForwardedIntrinsicReturnTests))]
    public void Isolated_ForwardedIntrinsics_VerifyBeforeExecution(string name, Dictionary<string, string> files)
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        foreach (var (file, source) in files) directory.CreateFile(file, source);
        var output = directory.GetPath("forwarded-intrinsic-" + name + ".dll");
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{directory.GetPath("main.ts")}\" -o \"{output}\" --standalone --verify",
            directory.Path, TimeSpan.FromSeconds(60));
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        Assert.All(GetAssemblyReferences(output), reference => Assert.StartsWith("System.", reference));
        Assert.False(File.Exists(directory.GetPath("SharpTS.dll")));
        Assert.Equal("null\n", ExecuteCompiledDllIsolated(output, timeoutMs: 30000,
            verifyStandardError: error => Assert.Empty(error), standardInput: ""));
    }
}

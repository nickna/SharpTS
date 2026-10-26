using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    public static IEnumerable<object[]> SwitchDefaultOrderingPrograms() => SwitchDefaultOrderingTests.Programs().Where(row =>
        (string)row[0] is "case-effects-stop-after-match" or "awaited-tests-and-bodies" or "yielded-tests-and-bodies"
            or "generator-case-destructuring-spill");

    [Theory, MemberData(nameof(SwitchDefaultOrderingPrograms))]
    public void Isolated_SwitchDefaultOrdering_VerifyBeforeExecution(string name, string source, string expected)
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var input = directory.CreateFile(name + ".ts", source);
        var output = directory.GetPath(name + ".dll");
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{input}\" -o \"{output}\" --verify", directory.Path);
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        Assert.DoesNotContain("SharpTS", GetAssemblyReferences(output));
        Assert.Equal(expected, ExecuteCompiledDllIsolated(output, timeoutMs: 10000,
            verifyStandardError: error => Assert.Empty(error)));
    }
}

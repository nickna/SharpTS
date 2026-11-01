using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, SerializableMemberData(nameof(AsyncLoopBodyCellCaptureTests.Cases), MemberType = typeof(AsyncLoopBodyCellCaptureTests))]
    public void Isolated_Issue2404AsyncLoopBodyCells_VerifyBeforeExecution(
        string name, Dictionary<string, string> files, string expected)
        => AssertStandaloneAsyncLoopBodyCells(name, files, expected, debug: false);

    [Fact]
    public void Isolated_Issue2404AsyncLoopBodyCells_DebugShadowKeepsIncomingCell()
    {
        var row = AsyncLoopBodyCellCaptureTests.Cases().Single(row =>
            (string)row[0] == "own-parameter-and-block-shadow");
        AssertStandaloneAsyncLoopBodyCells((string)row[0], (Dictionary<string, string>)row[1],
            (string)row[2], debug: true);
    }

    private static void AssertStandaloneAsyncLoopBodyCells(
        string name, Dictionary<string, string> files, string expected, bool debug)
    {
        Assert.NotEmpty(name);
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        foreach (var (path, source) in files)
            directory.CreateFile(path, source);
        var output = directory.GetPath("async-body-cells-" + name + (debug ? "-debug" : "") + ".dll");
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --compile \"{directory.GetPath("main.ts")}\" -o \"{output}\" --standalone --verify" + (debug ? " --debug" : ""),
            directory.Path, TimeSpan.FromSeconds(60));

        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        Assert.All(GetAssemblyReferences(output), reference => Assert.StartsWith("System.", reference));
        Assert.False(File.Exists(directory.GetPath("SharpTS.dll")));
        if (debug) Assert.True(File.Exists(Path.ChangeExtension(output, ".pdb")));
        Assert.Equal(expected, ExecuteCompiledDllIsolated(output, timeoutMs: 30000,
            verifyStandardError: error => Assert.Empty(error), standardInput: ""));
    }
}

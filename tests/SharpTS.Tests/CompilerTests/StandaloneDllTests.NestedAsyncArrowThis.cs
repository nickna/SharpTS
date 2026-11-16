using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.IntegrationTests;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    public static IEnumerable<object[]> NestedAsyncArrowThisDeployments() =>
        NestedAsyncArrowThisTests.Cases().SelectMany(row =>
            new[] { false, true }.Select(standalone => new object[] { row[0], standalone }));

    [Theory]
    [DeploymentData(nameof(NestedAsyncArrowThisDeployments))]
    public void Isolated_Issue2385NestedAsyncArrowThis_VerifiesBeforeExecution(string name, bool standalone)
    {
        var row = NestedAsyncArrowThisTests.Cases().Single(row => (string)row[0] == name);
        using var directory = CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", (string)row[1]);
        var output = directory.GetPath("nested-async-arrow-this-" + name + ".dll");
        var compile = CliTestHelper.RunCli(
            $"--no-tsconfig --noLib --compile \"{path}\" -o \"{output}\" --verify{(standalone ? " --standalone" : "")}",
            directory.Path, TimeSpan.FromSeconds(60));
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        var references = GetAssemblyReferences(output);
        if (standalone)
        {
            Assert.DoesNotContain("SharpTS", references);
            Assert.False(File.Exists(directory.GetPath("SharpTS.dll")));
        }
        else if (references.Contains("SharpTS"))
            Assert.True(File.Exists(directory.GetPath("SharpTS.dll")));

        var result = CaptureBoundedProcess(output, hidden: true);
        Assert.True(result.ExitCode == 0, result.Stderr);
        Assert.Equal((string)row[2], result.Stdout);
        Assert.Empty(result.Stderr);
    }
}

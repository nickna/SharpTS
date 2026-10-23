using SharpTS.Tests.TypeCheckerTests;
using Xunit;

namespace SharpTS.Tests.IntegrationTests;

public sealed class CliReadonlyTupleCompoundTests
{
    [Theory, MemberData(nameof(ReadonlyTupleCompoundAssignmentTests.CliCases), MemberType = typeof(ReadonlyTupleCompoundAssignmentTests))]
    public void NoEmitChecksReadonlyTupleCompounds(string name, string source, string expected, string collection)
    {
        Assert.NotEmpty(name);
        Assert.Contains(collection, new[] { "Default", "Strict" });
        using var directory = CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        var filesBefore = Directory.GetFiles(directory.Path, "*", SearchOption.AllDirectories)
            .OrderBy(file => file, StringComparer.Ordinal).ToArray();
        var options = collection == "Strict" ? "--strict --noLib " : "";
        var result = CliTestHelper.RunCli($"--no-tsconfig {options}--noEmit \"{path}\"",
            directory.Path, TimeSpan.FromSeconds(30));
        Assert.Equal(filesBefore, Directory.GetFiles(directory.Path, "*", SearchOption.AllDirectories)
            .OrderBy(file => file, StringComparer.Ordinal).ToArray());
        Assert.Empty(result.StandardError);
        if (expected.Length == 0)
        {
            Assert.True(result.ExitCode == 0, result.StandardOutput);
            Assert.Empty(result.StandardOutput);
        }
        else
        {
            Assert.Equal("TS2540:2", expected);
            Assert.True(result.ExitCode == 1, result.StandardOutput);
            var error = Assert.Single(result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries));
            Assert.StartsWith("Error: Type Error at ", error);
            Assert.Contains("main.ts:2:", error);
            Assert.Contains("read-only", error, StringComparison.OrdinalIgnoreCase);
        }
    }
}

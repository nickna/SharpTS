using SharpTS.Tests.TypeCheckerTests;
using Xunit;

namespace SharpTS.Tests.IntegrationTests;

public sealed class CliExplicitGenericCallArgumentTests
{
    [Theory, MemberData(nameof(ExplicitGenericCallArgumentDiagnosticTests.Cases), MemberType = typeof(ExplicitGenericCallArgumentDiagnosticTests))]
    public void StrictNoEmitChecksExplicitGenericArguments(
        string name, string source, string expectedCode, int expectedLine)
    {
        Assert.NotEmpty(name);
        using var directory = CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        var filesBefore = Directory.GetFiles(directory.Path, "*", SearchOption.AllDirectories)
            .OrderBy(file => file, StringComparer.Ordinal).ToArray();
        var result = CliTestHelper.RunCli($"--no-tsconfig --strict --noLib --noEmit \"{path}\"",
            directory.Path, TimeSpan.FromSeconds(30));
        Assert.Equal(filesBefore, Directory.GetFiles(directory.Path, "*", SearchOption.AllDirectories)
            .OrderBy(file => file, StringComparer.Ordinal).ToArray());
        Assert.Empty(result.StandardError);
        if (expectedCode.Length == 0)
        {
            Assert.True(result.ExitCode == 0, result.StandardOutput);
            Assert.Empty(result.StandardOutput);
        }
        else
        {
            Assert.True(result.ExitCode == 1, result.StandardOutput);
            var error = Assert.Single(result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries));
            Assert.StartsWith("Error: Type Error at ", error);
            Assert.Contains("main.ts:" + expectedLine + ":", error);
            Assert.Contains("wrong", error, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("number", error, StringComparison.OrdinalIgnoreCase);
        }
    }
}

using SharpTS.Tests.TypeCheckerTests;
using Xunit;

namespace SharpTS.Tests.IntegrationTests;

public sealed class CliUnknownCatchInstanceofTests
{
    public static IEnumerable<object[]> Cases() => UnknownCatchInstanceofDiagnosticTests.Cases()
        .SelectMany(row => new[] { "Default", "StrictNoLib", "StrictLoaded" }
            .Select(collection => new object[] { row[0], row[1], collection }));

    [Theory, MemberData(nameof(Cases))]
    public void WholeGuardedCatchProgramIsAcceptedWithoutEmission(
        string name, string source, string collection)
    {
        Assert.Equal("public", name);
        Assert.True(collection is "Default" or "StrictNoLib" or "StrictLoaded");
        using var directory = CliTestHelper.CreateTempDirectory();
        var sourcePath = directory.CreateFile("main.ts", source);
        var before = Directory.GetFiles(directory.Path, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();
        var options = collection switch
        {
            "StrictNoLib" => "--strict --noLib ",
            "StrictLoaded" => "--strict ",
            _ => ""
        };
        var result = CliTestHelper.RunCli(
            $"--no-tsconfig {options}--noEmit \"{sourcePath}\"",
            directory.Path, TimeSpan.FromSeconds(20));
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        Assert.Empty(result.StandardError);
        Assert.Empty(result.StandardOutput);
        Assert.Equal(before, Directory.GetFiles(directory.Path, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal).ToArray());
    }
}

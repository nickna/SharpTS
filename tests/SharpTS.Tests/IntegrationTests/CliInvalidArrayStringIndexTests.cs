using SharpTS.Tests.TypeCheckerTests;
using Xunit;

namespace SharpTS.Tests.IntegrationTests;

public sealed class CliInvalidArrayStringIndexTests
{
    [Theory]
    [InlineData("Default")]
    [InlineData("Strict")]
    public void NoEmitRetainsCanonicalErrorsAndUsesArrayIndexDiagnostic(string collection)
    {
        Assert.Contains(collection, new[] { "Default", "Strict" });
        using var directory = CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", CanonicalArrayStringIndexReadDiagnosticTests.Source);
        var filesBefore = Directory.GetFiles(directory.Path, "*", SearchOption.AllDirectories)
            .OrderBy(file => file, StringComparer.Ordinal).ToArray();
        var options = collection == "Strict" ? "--strict --noLib " : "";
        var result = CliTestHelper.RunCli($"--no-tsconfig {options}--noEmit \"{path}\"",
            directory.Path, TimeSpan.FromSeconds(30));
        Assert.Equal(filesBefore, Directory.GetFiles(directory.Path, "*", SearchOption.AllDirectories)
            .OrderBy(file => file, StringComparer.Ordinal).ToArray());
        Assert.Empty(result.StandardError);
        Assert.True(result.ExitCode == 1, result.StandardOutput);
        var errors = result.StandardOutput.Replace("\r\n", "\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(5, errors.Length);
        var locations = new[] { 2, 4, 6, 9, 11 };
        for (var index = 0; index < locations.Length; index++)
        {
            Assert.StartsWith("Error: Type Error at ", errors[index]);
            Assert.Contains("main.ts:" + locations[index] + ":", errors[index]);
        }
        Assert.EndsWith("Element implicitly has an 'any' type because index expression is not of type 'number'.",
            errors[4]);
    }
}

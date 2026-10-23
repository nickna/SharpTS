using SharpTS.Tests.TypeCheckerTests;
using Xunit;

namespace SharpTS.Tests.IntegrationTests;

public sealed class CliComputedClassFieldKeyLegalityTests
{
    [Theory, MemberData(nameof(ComputedClassFieldKeyLegalityTests.ProfileCases),
        MemberType = typeof(ComputedClassFieldKeyLegalityTests))]
    public void CheckedAdmissionRejectsNonUniqueSymbolFieldsWithoutEmission(
        string name, string source, bool strict)
    {
        Assert.NotEmpty(name);
        using var directory = CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile(name + ".ts", source);
        var flags = strict ? "--strict " : "";
        var result = CliTestHelper.RunCli(
            $"--no-tsconfig {flags}--noLib --noEmit \"{path}\"",
            directory.Path, TimeSpan.FromSeconds(20));

        Assert.Equal(1, result.ExitCode);
        Assert.Equal($"Error: Type Error at {path}:1:1: {ComputedClassFieldKeyLegalityTests.ComputedNameMessage}\n",
            result.StandardOutput);
        Assert.Empty(result.StandardError);
        Assert.Equal(source, File.ReadAllText(path));
        Assert.Equal(new[] { path }, Directory.GetFileSystemEntries(directory.Path));
    }
}

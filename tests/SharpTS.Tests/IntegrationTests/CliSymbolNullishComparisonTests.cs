using SharpTS.Tests.TypeCheckerTests;
using Xunit;

namespace SharpTS.Tests.IntegrationTests;

public sealed class CliSymbolNullishComparisonTests
{
    [Theory, MemberData(nameof(SymbolNullishComparisonDiagnosticTests.Cases), MemberType = typeof(SymbolNullishComparisonDiagnosticTests))]
    public void StrictNoEmitAcceptsNullishSymbolComparisons(string name, string source)
    {
        Assert.NotEmpty(name);
        using var directory = CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        var filesBefore = Directory.GetFiles(directory.Path, "*", SearchOption.AllDirectories)
            .OrderBy(file => file, StringComparer.Ordinal).ToArray();
        // These declaration-only programs must never execute or produce an assembly.
        var result = CliTestHelper.RunCli($"--no-tsconfig --strict --noLib --noEmit \"{path}\"",
            directory.Path, TimeSpan.FromSeconds(30));
        var filesAfter = Directory.GetFiles(directory.Path, "*", SearchOption.AllDirectories)
            .OrderBy(file => file, StringComparer.Ordinal).ToArray();
        Assert.Equal(filesBefore, filesAfter);
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        Assert.Empty(result.StandardOutput);
        Assert.Empty(result.StandardError);
    }
}

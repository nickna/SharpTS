using SharpTS.Tests.TypeCheckerTests;
using Xunit;

namespace SharpTS.Tests.IntegrationTests;

public sealed class CliGenericInstantiationExpressionTests
{
    [Fact]
    public void StrictNoEmitAcceptsPublicInstantiation()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", GenericInstantiationDiagnosticTests.PublicSource);
        var filesBefore = Directory.GetFiles(directory.Path, "*", SearchOption.AllDirectories)
            .OrderBy(file => file, StringComparer.Ordinal).ToArray();
        var result = CliTestHelper.RunCli($"--no-tsconfig --strict --noLib --noEmit \"{path}\"",
            directory.Path, TimeSpan.FromSeconds(30));
        Assert.Equal(filesBefore, Directory.GetFiles(directory.Path, "*", SearchOption.AllDirectories)
            .OrderBy(file => file, StringComparer.Ordinal).ToArray());
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        Assert.Empty(result.StandardOutput);
        Assert.Empty(result.StandardError);
    }
}

using SharpTS.Testing;
using Xunit;

namespace SharpTS.Tests.IntegrationTests;

public class CliLineDirectiveTests
{
    [Theory]
    [InlineData("ts-ignore")]
    [InlineData("ts-expect-error")]
    public void ModuleCheckingSuppressesTheNextLineSemanticError(string directive)
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        directory.CreateFile("dependency.ts", $"// @{directive}\nconsole.log(Math.abs('not a number'));\nexport const value = 1;");
        string path = directory.CreateFile("main.ts", "import { value } from './dependency'; console.log(value);");
        var result = CliTestHelper.RunCli($"--no-tsconfig --noEmit \"{path}\"", directory.Path);
        Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
    }

    [Fact]
    public void UnusedExpectationIsReportedAtItsSource()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string path = directory.CreateFile("main.ts", "// @ts-expect-error\nconsole.log(Math.abs(1));");
        var result = CliTestHelper.RunCli($"--no-tsconfig --noEmit \"{path}\"", directory.Path);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Unused '@ts-expect-error' directive.", result.StandardOutput + result.StandardError);
        Assert.Contains("main.ts:1", result.StandardOutput + result.StandardError);
    }

    [Fact]
    public void SuppressedErrorsDoNotExhaustTheDiagnosticBudget()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string suppressed = string.Concat(Enumerable.Repeat("// @ts-ignore\nconsole.log(Math.abs('ignored'));\n", 12));
        string path = directory.CreateFile("main.ts", suppressed + "const unsuppressed: number = 'still wrong';");
        var result = CliTestHelper.RunCli($"--no-tsconfig --noEmit \"{path}\"", directory.Path);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("main.ts:25", result.StandardOutput + result.StandardError);
        Assert.Contains("still wrong", result.StandardOutput + result.StandardError);
        Assert.DoesNotContain("expected type 'number'", result.StandardOutput + result.StandardError);
    }
}

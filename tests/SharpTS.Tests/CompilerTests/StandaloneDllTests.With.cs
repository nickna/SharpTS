using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory]
    [InlineData(WithStatementTests.StandaloneSource, WithStatementTests.StandaloneExpected)]
    [InlineData(WithStatementTests.CapturedCallSuspensionSource, "0 bc 5\n7\n")]
    [InlineData(WithStatementTests.DirectEvalFallbackSource, "7\n1\n")]
    [InlineData(WithStatementTests.AmbiguousMethodGuardSource, WithStatementTests.AmbiguousMethodGuardExpected)]
    [InlineData(WithStatementTests.ParentOperandSource, WithStatementTests.ParentOperandExpected)]
    [InlineData(WithStatementTests.SuspendedLookupSource, WithStatementTests.SuspendedLookupExpected)]
    [InlineData(MathFixedArityArgumentTests.Source, MathFixedArityArgumentTests.Expected)]
    public void Isolated_WithBindingsAndGeneratorSuspension(string input, string expected)
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var source = directory.CreateFile("main.js", input);
        var output = directory.GetPath("with-statements.dll");
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --noLib --compile \"{source}\" -o \"{output}\" --standalone --verify",
            directory.Path, TimeSpan.FromSeconds(60));
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        Assert.All(GetAssemblyReferences(output), reference => Assert.StartsWith("System.", reference));
        Assert.False(File.Exists(directory.GetPath("SharpTS.dll")));
        Assert.Equal(expected, ExecuteCompiledDllIsolated(output, timeoutMs: 10000,
            verifyStandardError: error => Assert.Empty(error), standardInput: ""));
    }
}

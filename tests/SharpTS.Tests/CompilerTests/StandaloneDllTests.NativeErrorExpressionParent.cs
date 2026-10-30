using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Isolated_NativeErrorExpressionParent_PreservesDeclarationHeritage(bool noLib)
    {
        const string source = """
            const C = class extends TypeError { constructor(message: string) { super(message); } };
            class D extends C {}
            const error: any = new D("own");
            console.log(error.message, error instanceof D, error instanceof TypeError);
            """;
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = directory.CreateFile("main.ts", source);
        var output = directory.GetPath("native-error-expression-parent.dll");
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig{(noLib ? " --noLib" : "")} --compile \"{sourcePath}\" -o \"{output}\" --standalone --verify",
            directory.Path, TimeSpan.FromSeconds(60));
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        Assert.All(GetAssemblyReferences(output), reference => Assert.StartsWith("System.", reference));
        Assert.False(File.Exists(directory.GetPath("SharpTS.dll")));
        Assert.Equal("own true true\n", ExecuteCompiledDllIsolated(output, timeoutMs: 10000,
            verifyStandardError: error => Assert.Empty(error), standardInput: ""));
    }
}

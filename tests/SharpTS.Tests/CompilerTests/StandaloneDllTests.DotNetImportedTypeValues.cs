using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, DeploymentData(nameof(DotNetImportedTypeValueTests.Cases), MemberType = typeof(DotNetImportedTypeValueTests))]
    public void Isolated_Issue2163ImportedClrTypeValues_VerifyBeforeExecution(
        string name, string source, string expected)
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        AssertStandaloneConstructorIdentity(directory.Path, path,
            directory.GetPath("imported-clr-value-" + name + ".dll"), expected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Isolated_Issue2163ModuleClrAliases_PreserveDeclarationIdentity(bool reverseOrder)
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        foreach (var (file, source) in DotNetImportedTypeValueTests.ModuleFiles(reverseOrder))
            directory.CreateFile(file, source);
        AssertStandaloneConstructorIdentity(directory.Path, directory.GetPath("main.ts"),
            directory.GetPath("module-clr-values.dll"), DotNetImportedTypeValueTests.ModuleExpected);
    }

    [Fact]
    public void Isolated_Issue2163DecoratedExternalTypeValue_VerifyBeforeExecution()
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", DotNetImportedTypeValueTests.DecoratedSource);
        var output = directory.GetPath("decorated-clr-value.dll");
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --experimentalDecorators --compile \"{path}\" -o \"{output}\" --standalone --verify",
            directory.Path, TimeSpan.FromSeconds(60));
        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        Assert.DoesNotContain("SharpTS", GetAssemblyReferences(output));
        Assert.False(File.Exists(directory.GetPath("SharpTS.dll")));
        Assert.Equal("1.2 function\nfunction true true\n", ExecuteCompiledDllIsolated(output, timeoutMs: 30000,
            verifyStandardError: error => Assert.Empty(error), standardInput: ""));
    }
}

using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, DeploymentData(nameof(ForOfBodyLexicalCellTests.Cases), MemberType = typeof(ForOfBodyLexicalCellTests))]
    public void Isolated_Issue2148ForOfBodyLexicalCells_ShareWithinEachActivation(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        AssertStandaloneConstructorIdentity(directory.Path, path,
            directory.GetPath("for-of-body-cells-" + name + ".dll"), expected);
    }
}

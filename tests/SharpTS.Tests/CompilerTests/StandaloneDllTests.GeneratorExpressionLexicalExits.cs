using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, DeploymentData(nameof(GeneratorExpressionLexicalExitTests.Cases), MemberType = typeof(GeneratorExpressionLexicalExitTests))]
    public void Isolated_Issue2149GeneratorExpressions_PreserveProtectedLexicalExits(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        AssertStandaloneConstructorIdentity(directory.Path, path,
            directory.GetPath("generator-expression-lexical-exit-" + name + ".dll"), expected);
    }
}

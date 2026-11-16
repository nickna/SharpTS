using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, DeploymentData(nameof(BlockGeneratorExpressionTests.Cases), MemberType = typeof(BlockGeneratorExpressionTests))]
    public void Isolated_Issue2149BlockGeneratorExpressions_PreserveScopeAndInvocation(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        AssertStandaloneConstructorIdentity(directory.Path, path,
            directory.GetPath("block-generator-expression-" + name + ".dll"), expected);
    }
}

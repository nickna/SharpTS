using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory]
    [DeploymentData(nameof(ClassExpressionConstructorRestTests.Cases), MemberType = typeof(ClassExpressionConstructorRestTests))]
    public void Isolated_Issue2125ExpressionConstructors_PackRestArguments(
        string name, string source, string expected)
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        AssertStandaloneConstructorIdentity(directory.Path, path,
            directory.GetPath("expression-rest-" + name + ".dll"), expected);
    }

    [Fact]
    public void Isolated_Issue2125ImportedExpressionConstructor_PreservesRestAndRawArguments()
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        directory.CreateFile("values.ts", ClassExpressionConstructorRestTests.ModuleClassSource);
        var path = directory.CreateFile("main.ts", ClassExpressionConstructorRestTests.ModuleMainSource);
        AssertStandaloneConstructorIdentity(directory.Path, path,
            directory.GetPath("expression-rest-module.dll"), ClassExpressionConstructorRestTests.ModuleExpected);
    }
}

using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, DeploymentData(nameof(OrdinaryConstructorStructuralTests.Cases),
        MemberType = typeof(OrdinaryConstructorStructuralTests))]
    public void Isolated_Issue2147OrdinaryConstructorStructure_PreservesSelectedValues(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        AssertStandaloneConstructorIdentity(directory.Path, path,
            directory.GetPath("ordinary-constructor-structure-" + name + ".dll"), expected);
    }
}

using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, DeploymentData(nameof(ExplicitSuperDefinitionArgumentsTests.Cases),
        MemberType = typeof(ExplicitSuperDefinitionArgumentsTests))]
    public void Isolated_Issue2143ExplicitSuper_SeparatesGuestArgumentsFromMetadata(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        AssertStandaloneConstructorIdentity(directory.Path, path,
            directory.GetPath("explicit-super-definition-" + name + ".dll"), expected);
    }
}

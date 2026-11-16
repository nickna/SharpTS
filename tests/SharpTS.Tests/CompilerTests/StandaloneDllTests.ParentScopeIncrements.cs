using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, DeploymentData(nameof(ParentScopeIncrementTests.Cases), MemberType = typeof(ParentScopeIncrementTests))]
    public void Isolated_Issue2495IncrementsAndDecrementsUpdateLiveParentScopeBindings(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        AssertStandaloneConstructorIdentity(directory.Path, path,
            directory.GetPath("parent-scope-increments-" + name + ".dll"), expected);
    }
}

using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, DeploymentData(nameof(ComputedConstructorMethodTests.Cases), MemberType = typeof(ComputedConstructorMethodTests))]
    public void Isolated_Issue2167ComputedConstructorMethods_KeepConstructionAndPrototypeValuesSeparate(
        string name, string source, string expected)
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        AssertStandaloneConstructorIdentity(directory.Path, path,
            directory.GetPath("computed-constructor-method-" + name + ".dll"), expected);
    }
}

using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, DeploymentData(nameof(AsyncArrowInstanceFieldTests.Cases),
        MemberType = typeof(AsyncArrowInstanceFieldTests))]
    public void Isolated_Issue2145AsyncArrowInstanceFields_AreCallable(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        AssertStandaloneConstructorIdentity(directory.Path, path,
            directory.GetPath("async-arrow-instance-fields-" + name + ".dll"), expected);
    }
}

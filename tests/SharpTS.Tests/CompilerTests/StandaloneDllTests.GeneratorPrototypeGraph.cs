using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, DeploymentData(nameof(GeneratorPrototypeGraphTests.Cases),
        MemberType = typeof(GeneratorPrototypeGraphTests))]
    public void Isolated_Issue2139GeneratorPrototypeGraph_VerifiesLiveParentsAndIntrinsicReads(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        AssertStandaloneConstructorIdentity(directory.Path, path,
            directory.GetPath("generator-prototype-graph-" + name + ".dll"), expected);
    }
}

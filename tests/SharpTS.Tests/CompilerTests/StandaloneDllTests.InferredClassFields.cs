using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, DeploymentData(nameof(InferredClassFieldRuntimeTests.Cases),
        MemberType = typeof(InferredClassFieldRuntimeTests))]
    public void Isolated_Issue2144InferredClassFields_PreserveEffectsAndValues(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        AssertStandaloneConstructorIdentity(directory.Path, path,
            directory.GetPath("inferred-class-fields-" + name + ".dll"), expected);
    }
}

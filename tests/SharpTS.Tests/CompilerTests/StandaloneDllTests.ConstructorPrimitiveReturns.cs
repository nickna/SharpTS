using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, DeploymentData(nameof(ConstructorPrimitiveReturnTests.Cases), MemberType = typeof(ConstructorPrimitiveReturnTests))]
    public void Isolated_Issue2141ConstructorPrimitiveReturns_PreserveInitializedReceiverAndReplacements(
        string name, string source, string expected)
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        AssertStandaloneConstructorIdentity(directory.Path, path,
            directory.GetPath("constructor-primitive-return-" + name + ".dll"), expected);
    }
}

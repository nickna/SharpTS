using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, DeploymentData(nameof(InnerFunctionPrivateContextTests.Cases), MemberType = typeof(InnerFunctionPrivateContextTests))]
    public void Isolated_Issue2487OrdinaryFunctions_PreservePrivateContext(
        string name, string source, string expected)
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        AssertStandaloneConstructorIdentity(directory.Path, path,
            directory.GetPath("inner-function-private-" + name + ".dll"), expected);
    }
}

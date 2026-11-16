using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, DeploymentData(nameof(StaticAccessorRuntimeSuperclassTests.Cases), MemberType = typeof(StaticAccessorRuntimeSuperclassTests))]
    public void Isolated_Issue2134StaticAccessorRuntimeSuperclasses_PreserveInitializationAndIdentity(
        string name, string source, string expected)
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        AssertStandaloneConstructorIdentity(directory.Path, path,
            directory.GetPath("static-accessor-runtime-superclass-" + name + ".dll"), expected);
    }
}

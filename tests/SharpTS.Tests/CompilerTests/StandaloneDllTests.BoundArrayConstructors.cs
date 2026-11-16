using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, DeploymentData(nameof(BoundArrayConstructorTests.Cases), MemberType = typeof(BoundArrayConstructorTests))]
    public void Isolated_Issue2130BoundArrayConstruction_PreservesGuestArgumentsAndErrors(
        string name, string source, string expected)
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        AssertStandaloneConstructorIdentity(directory.Path, path,
            directory.GetPath("bound-array-" + name + ".dll"), expected);
    }
}

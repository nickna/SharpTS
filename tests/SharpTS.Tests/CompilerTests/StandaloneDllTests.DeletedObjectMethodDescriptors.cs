using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, DeploymentData(nameof(DeletedObjectMethodDescriptorTests.Cases), MemberType = typeof(DeletedObjectMethodDescriptorTests))]
    public void Isolated_Issue2132DeletedObjectMethodDescriptors_PreserveUserDefinitionsAndDeletion(
        string name, string source, string expected)
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        AssertStandaloneConstructorIdentity(directory.Path, path,
            directory.GetPath("deleted-object-method-" + name + ".dll"), expected);
    }
}

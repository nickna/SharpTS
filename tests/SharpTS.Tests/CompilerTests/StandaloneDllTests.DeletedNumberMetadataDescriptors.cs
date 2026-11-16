using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, DeploymentData(nameof(DeletedNumberMetadataDescriptorTests.Cases), MemberType = typeof(DeletedNumberMetadataDescriptorTests))]
    public void Isolated_Issue2162DeletedNumberMetadataDescriptors_PreserveDeletionAndRestoration(
        string name, string source, string expected)
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        AssertStandaloneConstructorIdentity(directory.Path, path,
            directory.GetPath("deleted-number-metadata-" + name + ".dll"), expected);
    }
}

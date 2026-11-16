using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, DeploymentData(nameof(DeletedNumberMetadataLookupTests.Cases), MemberType = typeof(DeletedNumberMetadataLookupTests))]
    public void Isolated_Issue2164DeletedNumberMetadataLookup_PreservesLivePrototypeAndReceiver(
        string name, string source, string expected)
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        AssertStandaloneConstructorIdentity(directory.Path, path,
            directory.GetPath("deleted-number-lookup-" + name + ".dll"), expected);
    }
}

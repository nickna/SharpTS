using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory]
    [DeploymentData(nameof(CollectionConsumerIteratorTests.Cases), MemberType = typeof(CollectionConsumerIteratorTests))]
    public void Isolated_Issue2123CollectionConsumers_PreserveIteratorProtocolBoundaries(
        string name, string source, string expected)
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        AssertStandaloneConstructorIdentity(directory.Path, path,
            directory.GetPath("consumer-iterator-" + name + ".dll"), expected);
    }
}

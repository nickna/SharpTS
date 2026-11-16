using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, DeploymentData(nameof(ComputedStringMethodMetadataTests.Cases), MemberType = typeof(ComputedStringMethodMetadataTests))]
    public void Isolated_Issue2166ComputedStringMethods_PreserveCanonicalMetadataAndDescriptors(
        string name, string source, string expected)
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        AssertStandaloneConstructorIdentity(directory.Path, path,
            directory.GetPath("computed-string-methods-" + name + ".dll"), expected);
    }
}

using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, DeploymentData(nameof(GeneratorForOfAcquisitionTests.Cases), MemberType = typeof(GeneratorForOfAcquisitionTests))]
    public void Isolated_Issue2135GeneratorForOfAcquisition_PreservesProtocolsAndNativeValues(
        string name, string source, string expected)
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        AssertStandaloneConstructorIdentity(directory.Path, path,
            directory.GetPath("generator-forof-acquisition-" + name + ".dll"), expected);
    }
}

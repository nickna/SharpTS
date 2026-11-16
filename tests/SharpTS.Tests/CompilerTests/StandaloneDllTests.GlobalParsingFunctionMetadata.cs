using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, DeploymentData(nameof(GlobalParsingFunctionMetadataTests.Cases), MemberType = typeof(GlobalParsingFunctionMetadataTests))]
    public void Isolated_Issue2165ParsingFunctionMetadata_PreservesArityAndCalls(
        string name, string source, string expected)
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        AssertStandaloneConstructorIdentity(directory.Path, path,
            directory.GetPath("parsing-function-metadata-" + name + ".dll"), expected);
    }
}

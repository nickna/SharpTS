using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, DeploymentData(nameof(ExtractedPrivateCaptureScopeTests.Cases), MemberType = typeof(ExtractedPrivateCaptureScopeTests))]
    public void Isolated_Issue2151ExtractedPrivateMethods_PreserveCaptureScope(
        string name, string source, string expected)
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        AssertStandaloneConstructorIdentity(directory.Path, path,
            directory.GetPath("extracted-private-capture-" + name + ".dll"), expected);
    }
}

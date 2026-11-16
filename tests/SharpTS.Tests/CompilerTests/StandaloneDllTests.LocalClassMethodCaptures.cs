using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, DeploymentData(nameof(LocalClassMethodCaptureTests.Cases), MemberType = typeof(LocalClassMethodCaptureTests))]
    public void Isolated_Issue2152LocalClassMethods_RetainEnclosingCaptures(
        string name, string source, string expected)
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        AssertStandaloneConstructorIdentity(directory.Path, path,
            directory.GetPath("local-class-method-capture-" + name + ".dll"), expected);
    }
}

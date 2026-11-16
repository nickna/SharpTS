using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory]
    [DeploymentData(nameof(AnyStaticPrivateReceiverTests.Cases), MemberType = typeof(AnyStaticPrivateReceiverTests))]
    public void Isolated_Issue2127AnyStaticPrivateReceivers_UseLexicalTypesAndRuntimeBrands(
        string name, string source, string expected)
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        AssertStandaloneConstructorIdentity(directory.Path, path,
            directory.GetPath("any-static-private-" + name + ".dll"), expected);
    }
}

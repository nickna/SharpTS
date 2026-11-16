using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, DeploymentData(nameof(ArrayConstructorFunctionHelperTests.Cases), MemberType = typeof(ArrayConstructorFunctionHelperTests))]
    public void Isolated_Issue2128ArrayAliases_ReadCanonicalFunctionHelpersAndPreserveArrayCalls(
        string name, string source, string expected)
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        AssertStandaloneConstructorIdentity(directory.Path, path,
            directory.GetPath("array-function-helpers-" + name + ".dll"), expected);
    }
}

using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory, DeploymentData(nameof(ArrayConstructorApparentFunctionTests.Cases), MemberType = typeof(ArrayConstructorApparentFunctionTests))]
    public void Isolated_Issue2129ArrayFunctionMembers_CheckDefaultLibrariesAndExecute(
        string name, string source, string expected)
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        AssertStandaloneConstructorIdentity(directory.Path, path,
            directory.GetPath("array-apparent-function-" + name + ".dll"), expected);
    }
}

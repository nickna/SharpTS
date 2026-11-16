using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory]
    [DeploymentData(nameof(MathSumPreciseIteratorCloseTests.Cases), MemberType = typeof(MathSumPreciseIteratorCloseTests))]
    public void Isolated_Issue2124MathSumPrecise_ClosesRejectedIterator(
        string name, string source, string expected)
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        AssertStandaloneConstructorIdentity(directory.Path, path,
            directory.GetPath("sum-precise-close-" + name + ".dll"), expected);
    }
}

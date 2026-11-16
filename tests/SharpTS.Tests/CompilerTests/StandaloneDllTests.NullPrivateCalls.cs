using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Theory]
    [InlineData("issue", StaticPrivateMethodCallBrandTests.NullInstanceReceiverSource, "TypeError true 0\n")]
    [InlineData("async-reader", StaticPrivateMethodCallBrandTests.AsyncNullInstanceReceiverSource,
        "TypeError true 0 1\ntrue|1 1 2\ntrue 1 3\n")]
    [InlineData("generator-reader", StaticPrivateMethodCallBrandTests.GeneratorNullInstanceReceiverSource,
        "0\nTypeError true 0 1\n1 true 1 2\n")]
    [InlineData("async-generator-reader", StaticPrivateMethodCallBrandTests.AsyncGeneratorNullInstanceReceiverSource,
        "0\nTypeError true 0 1\ntrue|1 true 1 2\n")]
    [DeploymentData(nameof(StaticPrivateMethodCallBrandTests.TypedNullInstanceReceiverCases), MemberType = typeof(StaticPrivateMethodCallBrandTests))]
    public void Isolated_Issue2150NullPrivateCalls_RejectBeforeArguments(
        string name, string source, string expected)
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", source);
        AssertStandaloneConstructorIdentity(directory.Path, path,
            directory.GetPath("null-private-call-" + name + ".dll"), expected);
    }
}

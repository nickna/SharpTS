using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Fact]
    public void Isolated_Issue2260OrdinaryNestedFunctionsShareTheirMethodLocalBindings()
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", MethodLocalInnerFunctionCaptureTests.IssueSource);
        AssertStandaloneConstructorIdentity(directory.Path, path,
            directory.GetPath("method-local-inner-function-captures.dll"), "1 2 2\n");
    }
}

using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class FunctionHelperCanonicalIdentityTests
{
    [Theory, ModeData]
    public void PreserveIssue2624Behavior(ExecutionMode mode)
    {
        const string source = """
            function C() {}
            const target: any = C;
            console.log(target.call === target.call, target['call'] === Function.prototype.call);
            console.log(target.apply === target.apply, target['apply'] === Function.prototype.apply);
            console.log(target.bind === target.bind, target['bind'] === Function.prototype.bind);
            """;
        Assert.Equal("true true\ntrue true\ntrue true\n", TestHarness.Run(source, mode));
    }
}

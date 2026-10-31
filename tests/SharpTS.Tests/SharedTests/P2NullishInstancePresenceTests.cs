using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P2NullishInstancePresenceTests
{
    [Theory, ModeData]
    public void NullishInstanceFieldsRemainPresent(ExecutionMode mode)
    {
        const string source = """
            class Declared { value = undefined; nullable = null; }
            const Expression = class { value = undefined; nullable = null; };
            for (const value of [new Declared(), new Expression()]) {
                console.log(Object.hasOwn(value, "value"), "value" in value);
                console.log(Object.hasOwn(value, "nullable"), "nullable" in value);
                console.log("missing" in value, "constructor" in value);
            }
            """;
        Assert.Equal(string.Concat(Enumerable.Repeat("true true\ntrue true\nfalse true\n", 2)), TestHarness.Run(source, mode));
    }
}

using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class NamedFunctionDefaultSelfScopeTests
{
    [Theory, ModeData]
    public void DefaultClosureRetainsNamedFunctionSelfWhenBodyVarShadowsIt(ExecutionMode mode)
    {
        Assert.Equal("function body\nfunction body\n", TestHarness.RunUnchecked("""
            const value = function named(read = () => named) {
                var named = 'body';
                console.log(typeof read(), named);
            };
            value();
            value();
            """, mode));
    }
}

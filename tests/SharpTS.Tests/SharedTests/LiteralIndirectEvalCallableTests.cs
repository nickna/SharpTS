using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class LiteralIndirectEvalCallableTests
{
    [Theory, ModeData]
    public void IndirectIifesUseDiscoveredCallableIdentities(ExecutionMode mode)
    {
        const string source = """
            (0, eval)('(function(){ console.log(7); })()');
            const evaluate = eval;
            evaluate('(() => console.log(8))()');
            let order = 0;
            (console.log(++order), eval)('(function(){ return function(){ console.log(9); }; })()()');
            """;
        Assert.Equal("7\n8\n1\n9\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void IndirectAsyncIifeUsesSelectedPromiseRuntimeAndDiscoveredBody(ExecutionMode mode)
    {
        const string source = """
            (0, eval)('(async function(){ await 0; console.log(7); })()');
            """;
        Assert.Equal("7\n", TestHarness.RunUnchecked(source, mode));
    }
}

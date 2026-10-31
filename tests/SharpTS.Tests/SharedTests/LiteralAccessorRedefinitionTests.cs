using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class LiteralAccessorRedefinitionTests
{
    [Theory, ModeData]
    public void DefiningAnExistingAccessorDoesNotInvokeItsGetter(ExecutionMode mode)
    {
        const string source = """
            const state = { reads: 0 };
            const token: any = {}, target: any = { get value() { state.reads++; return 10; } };
            Object.defineProperty(target, "value", { get: function() { throw token; }, configurable: true });
            console.log("defined", state.reads);
            try { target.value; }
            catch (error: any) { console.log(error === token, state.reads); }
            """;
        Assert.Equal("defined 0\ntrue 0\n", TestHarness.Run(source, mode));
    }
}

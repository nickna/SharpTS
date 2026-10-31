using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P2NoncallableProxyApplyTests
{
    [Theory, ModeData]
    public void ApplyTrapCannotMakeNoncallableTargetCallable(ExecutionMode mode)
    {
        const string source = """
            let order = "";
            function argument() { order += "a"; return 7; }
            const proxy: any = new Proxy({}, {
                get apply() { order += "get"; return () => { order += "trap"; return 0; }; }
            });
            try { proxy(argument()); console.log("returned"); }
            catch (error) { console.log(error instanceof TypeError, order); }
            order = "";
            const owner = { value: proxy };
            try { owner.value(argument()); console.log("returned"); }
            catch (error) { console.log(error instanceof TypeError, order); }
            const callable: any = new Proxy((value: number) => value + 1, { apply(target: any, receiver: any, args: any[]) { return args[0] + 2; } });
            console.log(callable(3));
            """;
        Assert.Equal("true a\ntrue a\n5\n", TestHarness.Run(source, mode));
    }
}

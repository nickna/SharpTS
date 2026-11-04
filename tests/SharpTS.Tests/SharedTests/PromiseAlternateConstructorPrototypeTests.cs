using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class PromiseAlternateConstructorPrototypeTests
{
    [Theory, ModeData]
    public void AlternateNewTargetSuppliesPromisePrototype(ExecutionMode mode)
    {
        Assert.Equal("1 true\n", TestHarness.Run("""
            const P: any = Promise;
            const Alt: any = function Alternate() {};
            let calls = 0;
            const value: any = Reflect.construct(P, [(resolve: any) => { calls++; resolve(7); }], Alt);
            console.log(calls, Object.getPrototypeOf(value) === Alt.prototype);
            """, mode));
    }
}

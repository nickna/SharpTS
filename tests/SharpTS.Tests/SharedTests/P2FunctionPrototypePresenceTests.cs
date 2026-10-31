using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P2FunctionPrototypePresenceTests
{
    [Theory, ModeData]
    public void InheritedAccessorPresenceDoesNotReadGetter(ExecutionMode mode)
    {
        const string source = """
            const prototype: any = Function.prototype;
            let gets = 0;
            Object.defineProperty(prototype, "watched", { get() { gets++; return undefined; }, configurable: true });
            Object.defineProperty(prototype, "getterless", { get: undefined, configurable: true });
            function ordinary() {}
            for (const target of [Array, ordinary])
                console.log("watched" in target, "getterless" in target, "absent" in target, gets);
            """;
        Assert.Equal("true true false 0\ntrue true false 0\n", TestHarness.Run(source, mode));
    }
}

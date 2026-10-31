using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P2GetterlessPromiseAccessorTests
{
    [Theory, ModeData]
    public void GetterlessPromiseAccessorsMaskIntrinsicMembers(ExecutionMode mode)
    {
        const string source = """
            const promise: any = Promise.resolve(7);
            for (const name of ["then", "constructor"]) {
                Object.defineProperty(promise, name, { set(value: any) {}, configurable: true });
                console.log(promise[name] === undefined);
                Object.defineProperty(promise, name, { get: undefined, set: undefined, configurable: true });
                console.log(promise[name] === undefined);
            }
            """;
        Assert.Equal("true\ntrue\ntrue\ntrue\n", TestHarness.Run(source, mode));
    }
}

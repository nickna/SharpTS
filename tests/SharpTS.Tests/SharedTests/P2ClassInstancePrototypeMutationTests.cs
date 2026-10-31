using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P2ClassInstancePrototypeMutationTests
{
    [Theory, ModeData]
    public void ExtensibleClassInstancesAllowOrdinaryPrototypeChanges(ExecutionMode mode)
    {
        const string source = """
            class Owner { own = 1; method() { return 2; } }
            const value: any = new Owner(), parent: any = { slot: 7 };
            console.log(Object.isExtensible(value), Reflect.isExtensible(value));
            console.log(Object.setPrototypeOf(value, parent) === value, Object.getPrototypeOf(value) === parent, value.slot, value.own);
            console.log(value.method === undefined);
            Object.preventExtensions(value);
            console.log(Reflect.setPrototypeOf(value, parent), Reflect.setPrototypeOf(value, {}));
            """;
        Assert.Equal("true true\ntrue true 7 1\ntrue\ntrue false\n", TestHarness.Run(source, mode));
    }
}

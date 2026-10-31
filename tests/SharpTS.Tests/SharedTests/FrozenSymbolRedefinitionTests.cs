using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class FrozenSymbolRedefinitionTests
{
    [Theory, ModeData]
    public void FrozenSymbolDataCannotChangeValue(ExecutionMode mode)
    {
        const string source = """
            const key = Symbol("own");
            for (const explicit of [false, true]) {
                const object: any = {};
                if (explicit) Object.defineProperty(object, key, { value: 7, writable: true, configurable: true });
                else object[key] = 7;
                Object.freeze(object);
                console.log(Reflect.defineProperty(object, key, { value: 7 }));
                console.log(Reflect.defineProperty(object, key, { value: 8 }), object[key]);
                const descriptor: any = Object.getOwnPropertyDescriptor(object, key);
                console.log(descriptor.writable, descriptor.configurable);
            }
            """;
        Assert.Equal("true\nfalse 7\nfalse false\ntrue\nfalse 7\nfalse false\n", TestHarness.Run(source, mode));
    }
}

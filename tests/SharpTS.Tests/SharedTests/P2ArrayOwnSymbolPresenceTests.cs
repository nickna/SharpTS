using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P2ArrayOwnSymbolPresenceTests
{
    [Theory, ModeData]
    public void ArrayOwnSymbolPredicatesAgreeWithDescriptors(ExecutionMode mode)
    {
        const string source = """
            const array: any = [], data = Symbol("data"), getterless = Symbol("getterless"), absent = Symbol("absent");
            array[data] = 7;
            Object.defineProperty(array, getterless, { get: undefined, configurable: true });
            for (const key of [data, getterless, absent])
                console.log(Object.hasOwn(array, key), Object.prototype.hasOwnProperty.call(array, key),
                    Object.getOwnPropertyDescriptor(array, key) !== undefined);
            console.log(array[data], array[getterless] === undefined);
            """;
        Assert.Equal("true true true\ntrue true true\nfalse false false\n7 true\n", TestHarness.Run(source, mode));
    }
}

using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P2ArrayReflectedPrototypeTests
{
    [Theory, ModeData]
    public void ArrayConstructorReflectedPrototypeMatchesOrdinaryPrototype(ExecutionMode mode)
    {
        const string source = """
            console.log(Object.getPrototypeOf(Array) === Function.prototype, Reflect.getPrototypeOf(Array) === Function.prototype);
            const root = Object.create(null);
            console.log(Object.getPrototypeOf(root) === null, Reflect.getPrototypeOf(root) === null);
            const parent = {};
            Object.setPrototypeOf(Array, parent);
            console.log(Object.getPrototypeOf(Array) === parent, Reflect.getPrototypeOf(Array) === parent);
            """;
        Assert.Equal("true true\ntrue true\ntrue true\n", TestHarness.Run(source, mode));
    }
}

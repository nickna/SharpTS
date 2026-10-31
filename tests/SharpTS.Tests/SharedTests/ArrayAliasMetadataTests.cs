using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class ArrayAliasMetadataTests
{
    [Theory, ModeData]
    public void ArrayAliasExposesCanonicalLengthAndPrototype(ExecutionMode mode)
    {
        const string source = """
            const alias: any = Array;
            console.log(alias.length, typeof alias.prototype, alias.prototype === Array.prototype);
            console.log(Object.getOwnPropertyDescriptor(alias, "length")!.value);
            console.log(alias.prototype === Object.getPrototypeOf([]));
            """;
        Assert.Equal("1 object true\n1\ntrue\n", TestHarness.Run(source, mode));
    }
}

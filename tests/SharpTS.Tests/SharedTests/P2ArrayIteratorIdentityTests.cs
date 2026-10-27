using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P2ArrayIteratorIdentityTests
{
    [Theory, ModeData]
    public void ArrayIteratorReadsRetainCanonicalIdentityAndCallReceiver(ExecutionMode mode)
    {
        const string source = """
            class Values extends Array {}
            const values: any = new Values(3, 4), plain: any = [5, 6];
            const iterator = Symbol.iterator;
            console.log(values[iterator] === values[iterator], values[iterator] === Array.prototype[iterator]);
            console.log(plain[iterator] === plain[iterator], plain[iterator] === Array.prototype[iterator]);
            console.log(Array.prototype[iterator] === Array.prototype.values);
            console.log(values[iterator]().next().value, plain[iterator]().next().value);
            console.log(values[iterator].call(plain).next().value);
            console.log([...values].join(","), [...plain].join(","), Array.from(values).join(","));
            """;
        Assert.Equal("true true\ntrue true\ntrue\n3 5\n5\n3,4 5,6 3,4\n", TestHarness.Run(source, mode));
    }
}

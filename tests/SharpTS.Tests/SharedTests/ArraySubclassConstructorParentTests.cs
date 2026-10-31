using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ArraySubclassConstructorParentTests
{
    [Theory, ModeData]
    public void Issue2605Regression(ExecutionMode mode)
    {
        const string source = """
            class Values extends Array {}
            class Child extends Values {}
            const values: any = new Values();
            const parent: any = Object.getPrototypeOf(Values);
            console.log(parent === Array);
            console.log(values instanceof parent);
            console.log(parent.prototype === Array.prototype);
            console.log(Object.getPrototypeOf(Child) === Values);
            console.log(new Child() instanceof Object.getPrototypeOf(Child));
            """;
        Assert.Equal("true\ntrue\ntrue\ntrue\ntrue\n", TestHarness.Run(source, mode));
    }
}

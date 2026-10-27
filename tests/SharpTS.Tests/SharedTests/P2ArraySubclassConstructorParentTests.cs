using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P2ArraySubclassConstructorParentTests
{
    // The compiled constructor bridge identity is tracked separately in #2605.
    [Theory, InterpretedOnlyData]
    public void DirectArraySubclassExposesRealmArrayAsConstructorParent(ExecutionMode mode)
    {
        const string source = """
            class Values extends Array {}
            class Child extends Values {}
            const parent: any = Object.getPrototypeOf(Values);
            console.log(parent === Array, new Values() instanceof parent, parent.prototype === Array.prototype);
            console.log(Object.getPrototypeOf(Child) === Values, new Child() instanceof Object.getPrototypeOf(Child));
            """;
        Assert.Equal("true true true\ntrue true\n", TestHarness.Run(source, mode));
    }
}

using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class ArraySubclassReparentedReadTests
{
    [Theory, ModeData]
    public void PreserveIssue2606Behavior(ExecutionMode mode)
    {
        const string source = """
            class Values extends Array {}
            const values: any = new Values(7, 8), custom: any = { marker: "custom" };
            Object.setPrototypeOf(Values.prototype, custom);
            console.log(Object.getPrototypeOf(values) === Values.prototype,
                Object.getPrototypeOf(Values.prototype) === custom);
            console.log(typeof values.push, typeof Reflect.get(values, "push"), "push" in values);
            console.log(values.marker, values.length, values[0], Array.isArray(values));
            """;
        Assert.Equal("true true\nundefined undefined false\ncustom 2 7 true\n", TestHarness.Run(source, mode));
    }
}

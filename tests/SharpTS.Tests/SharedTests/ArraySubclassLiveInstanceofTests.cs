using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class ArraySubclassLiveInstanceofTests
{
    [Theory, ModeData]
    public void PreserveIssue2603Behavior(ExecutionMode mode)
    {
        const string source = """
            class Parent extends Array {}
            class Child extends Parent {}
            const values: any = new Child();
            const original = Object.getPrototypeOf(Child.prototype);
            Object.setPrototypeOf(Child.prototype, null);
            console.log(values instanceof Child, values instanceof Parent,
                values instanceof Array, values instanceof Object, Array.isArray(values));
            Object.setPrototypeOf(Child.prototype, original);
            console.log(values instanceof Child, values instanceof Parent,
                values instanceof Array, values instanceof Object);
            """;
        Assert.Equal("true false false false true\ntrue true true true\n", TestHarness.Run(source, mode));
    }
}

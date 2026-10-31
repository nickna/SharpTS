using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class GenericConstructorParentIdentityTests
{
    [Theory, ModeData]
    public void Issue2636Regression(ExecutionMode mode)
    {
        const string source = """
            class Parent<T> {}
            class Child<T> extends Parent<T> {}
            console.log(Object.getPrototypeOf(Child) === Parent);
            """;
        Assert.Equal("true\n", TestHarness.Run(source, mode));
    }
}

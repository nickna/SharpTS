using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class GenericParentAccessorDescriptorTests
{
    [Theory, ModeData]
    public void Issue2275Regression(ExecutionMode mode)
    {
        const string source = """
            class Base<T> { static set value(next: number) {} }
            class Derived extends Base<number> {}
            const parentConstructor: any = Object.getPrototypeOf(Derived);
            const sourceDescriptor: any = Object.getOwnPropertyDescriptor(Base, "value");
            const parentDescriptor: any = Object.getOwnPropertyDescriptor(parentConstructor, "value");
            console.log(typeof sourceDescriptor?.set, typeof parentDescriptor?.set, parentDescriptor === undefined);
            """;
        Assert.Equal("function function false\n", TestHarness.Run(source, mode));
    }
}
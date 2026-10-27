using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class InheritedInstanceFieldPresenceTests
{
    [Theory, ModeData]
    public void Issue2374Regression(ExecutionMode mode)
    {
        const string source = """
            class A { stored = 'own'; }
            class B extends A {}
            const instance: any = new B();
            console.log(instance.stored, Object.hasOwn(instance, 'stored'), Reflect.get(instance, 'stored'));
            """;
        Assert.Equal("own true own\n", TestHarness.Run(source, mode));
    }
}

using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class InheritedStaticNewThisTests
{
    [Theory, ModeData]
    public void PreserveIssue2535Behavior(ExecutionMode mode)
    {
        const string source = """
            class Base {
                static create(): Base { return new this(); }
                read(): string { return 'base'; }
            }
            class Child extends Base {
                read(): string { return 'child'; }
            }
            const item = Child.create();
            console.log(item.read(), item instanceof Child, item instanceof Base);
            """;
        Assert.Equal("child true true\n", TestHarness.Run(source, mode));
    }
}

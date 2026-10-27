using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P2StaticMethodAssignmentFlagsTests
{
    [Theory, ModeData]
    public void StaticMethodReplacementPreservesNonenumerableDescriptor(ExecutionMode mode)
    {
        const string source = """
            class Owner { static method() { return 7; } }
            const owner: any = Owner;
            owner.method = () => 9;
            const descriptor: any = Object.getOwnPropertyDescriptor(owner, "method");
            console.log(descriptor.writable, descriptor.enumerable, descriptor.configurable, descriptor.value());
            class Field { static value = 4; }
            console.log(Object.getOwnPropertyDescriptor(Field, "value")!.enumerable, Field.value);
            """;
        Assert.Equal("true false true 9\ntrue 4\n", TestHarness.Run(source, mode));
    }
}

using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class FrozenConstructorSloppyWriteTests
{
    [Theory, ModeData]
    public void PreserveIssue2567Behavior(ExecutionMode mode)
    {
        const string source = """
            class C {}
            const target: any = C;
            target.value = 1;
            Object.freeze(target);
            target.value = 2;
            console.log(Object.isFrozen(target), target.value);
            """;
        Assert.Equal("true 1\n", TestHarness.Run(source, mode));
    }
}

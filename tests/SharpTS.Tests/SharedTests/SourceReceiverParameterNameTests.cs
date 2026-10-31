using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class SourceReceiverParameterNameTests
{
    [Theory, ModeData]
    public void SourceParameterDoesNotReplaceTypedClassReceiver(ExecutionMode mode)
    {
        const string source = """
            class C {
                value = 1;
                read(__this: number) { return this.value + __this; }
            }
            console.log(new C().read(2));
            """;
        Assert.Equal("3\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void SourceParameterRemainsDistinctInEvaluatedMethodsAndFunctions(ExecutionMode mode)
    {
        const string source = """
            const C = class {
                value = 1;
                read(__this: number) { return this.value + __this; }
            };
            const object = { value: 2, read(__this: number) { return this.value + __this; } };
            const read: any = function(__this: number) { return this.value + __this; };
            console.log(new C().read(2), object.read(2), read.call({ value: 3 }, 2));
            """;
        Assert.Equal("3 4 5\n", TestHarness.Run(source, mode));
    }
}

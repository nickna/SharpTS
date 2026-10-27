using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class SealedReadonlyRedefinitionTests
{
    [Theory, ModeData]
    public void SealedReadonlyDefinitionsRejectChangedValues(ExecutionMode mode)
    {
        const string source = """
            const target: any = { value: 7 };
            Object.seal(target);
            console.log(Reflect.defineProperty(target, "value", { writable: false }));
            const descriptor: any = Object.getOwnPropertyDescriptor(target, "value");
            console.log(descriptor.writable, descriptor.configurable);
            console.log(Reflect.defineProperty(target, "value", { value: 8 }), target.value);
            console.log(Reflect.defineProperty(target, "value", { value: 7 }), target.value);
            """;
        Assert.Equal("true\nfalse false\nfalse 7\ntrue 7\n", TestHarness.Run(source, mode));
    }
}

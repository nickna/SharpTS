using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class AutoAccessorPrototypeDescriptorTests
{
    [Theory, ModeData]
    public void Issue2570Regression(ExecutionMode mode)
    {
        const string source = """
            class C { accessor label: string = "first"; }
            const descriptor: any = Object.getOwnPropertyDescriptor(C.prototype, "label");
            console.log(typeof descriptor, typeof descriptor?.get, typeof descriptor?.set);
            """;
        Assert.Equal("object function function\n", TestHarness.Run(source, mode));
    }
}

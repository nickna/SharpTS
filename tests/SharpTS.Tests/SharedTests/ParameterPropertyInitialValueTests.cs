using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ParameterPropertyInitialValueTests
{
    [Theory, ModeData]
    public void EarlierFieldInitializerReadsUndefinedParameterProperty(ExecutionMode mode)
    {
        const string source = """
            class C {
                early = (this as any).value;
                constructor(public value: number) {}
            }
            const c = new C(44);
            console.log(typeof c.early, c.value);
            """;
        Assert.Equal("undefined 44\n", TestHarness.Run(source, mode));
    }
}

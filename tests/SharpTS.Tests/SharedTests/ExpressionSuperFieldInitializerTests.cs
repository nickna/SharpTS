using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ExpressionSuperFieldInitializerTests
{
    [Theory, ModeData]
    public void Issue2373Regression(ExecutionMode mode)
    {
        const string source = """
            class Base { value() { return "instance"; } static value() { return "static"; } }
            const Derived: any = class extends Base { result = super.value(); };
            console.log(new Derived().result);
            """;
        Assert.Equal("instance\n", TestHarness.Run(source, mode));
    }
}
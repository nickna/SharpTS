using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class NestedClassInitializerCaptureTests
{
    [Theory, ModeData]
    public void IntermediateFactoryForwardsInitializerOnlyCapture(ExecutionMode mode)
    {
        const string source = """
            function outer(value: number) {
                function make() { return class { field = value; }; }
                const C: any = make();
                console.log(new C().field);
            }
            outer(7); outer(9);
            """;
        Assert.Equal("7\n9\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ForwardedInitializerReadsLiveMutableBinding(ExecutionMode mode)
    {
        const string source = """
            function outer(value: number) {
                function make() { return class { field = value; }; }
                const C: any = make();
                value += 1;
                console.log(new C().field);
            }
            outer(7); outer(9);
            """;
        Assert.Equal("8\n10\n", TestHarness.Run(source, mode));
    }
}

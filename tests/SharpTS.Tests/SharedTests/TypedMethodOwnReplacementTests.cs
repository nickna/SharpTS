using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class TypedMethodOwnReplacementTests
{
    [Theory, ModeData]
    public void TypedCallsObserveOwnMethodReplacements(ExecutionMode mode)
    {
        const string source = """
            class C { m(): number { return 1; } }
            function run() {
                const c = new C();
                c.m = () => 2;
                console.log(c.m());
            }
            run();
            """;
        Assert.Equal("2\n", TestHarness.Run(source, mode));
    }
}

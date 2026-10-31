using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class NestedFunctionClassSelfTests
{
    [Theory, ModeData]
    public void OrdinaryFunctionsRetainEvaluatedClassSelf(ExecutionMode mode)
    {
        const string source = """
            function make() {
                return class Named {
                    static read() { function f() { return Named; } return f(); }
                    static make() { function f() { return () => Named; } return f(); }
                };
            }
            const first: any = make(), second: any = make();
            console.log(first.read() === first, second.read() === second,
                first.make()() === first, second.make()() === second);
            """;
        Assert.Equal("true true true true\n", TestHarness.Run(source, mode));
    }
}

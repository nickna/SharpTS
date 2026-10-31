using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class GenericExpressionModuleParentTests
{
    [Theory, ModeData]
    public void Issue2332Regression(ExecutionMode mode)
    {
        const string source = """
            class Root<T> {
                value: T;
                constructor(value: T) { this.value = value; }
                read(): T { return this.value; }
            }
            const Middle = class<T> extends Root<T> {};
            class Leaf extends Middle<number> {}
            console.log(new Leaf(43).read());
            """;
        Assert.Equal("43\n", TestHarness.Run(source, mode));
        Assert.Equal("43\n", TestHarness.RunModules(new Dictionary<string, string> { ["main.ts"] = source }, "main.ts", mode));
    }
}

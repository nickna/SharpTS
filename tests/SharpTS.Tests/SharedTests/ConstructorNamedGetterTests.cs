using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ConstructorNamedGetterTests
{
    [Theory, ModeData]
    public void Issue2433Regression(ExecutionMode mode)
    {
        const string source = """
            const state = { gets: 0 };
            function key(): string { return "constructor"; }
            class C { get [key()](): any { state.gets++; return 17; } }
            const value: any = new C();
            console.log(value["constructor"], state.gets);
            """;
        Assert.Equal("17 1\n", TestHarness.Run(source, mode));
    }
}
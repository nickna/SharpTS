using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class DelegatingYieldOperandTests
{
    [Theory]
    [InlineData("function* values() { yield*; }")]
    [InlineData("function* values() { yield* }")]
    [InlineData("function* values() { (yield*); }")]
    [InlineData("function* values() { [yield*, 1]; }")]
    [InlineData("function* values() { yield*")]
    [InlineData("function* values() { yield*\n; }")]
    [InlineData("async function* values() { yield*; }")]
    public void DelegatingYield_RequiresAnOperandEvenWhenNeverExecuted(string source)
    {
        Assert.ThrowsAny<Exception>(() => TestHarness.ParseOrThrow(source));
    }

    [Theory, ModeData]
    public void DelegatingYield_AllowsLineTerminatorAfterStar(ExecutionMode mode)
    {
        const string source = """
            function* values() { yield*
                [7]; }
            const generator = values();
            console.log(generator.next().value, generator.next().done);
            """;
        Assert.Equal("7 true\n", TestHarness.RunUnchecked(source, mode));
    }
}

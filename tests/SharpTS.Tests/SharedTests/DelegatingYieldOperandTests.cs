using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class DelegatingYieldOperandTests
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\r\n")]
    [InlineData("\u2028")]
    [InlineData("\u2029")]
    public void DelegatingYield_RejectsLineTerminatorBeforeStar(string separator)
    {
        foreach (string prefix in new[] { "function*", "async function*" })
        foreach (string trivia in new[] { separator, $"/*{separator}*/", $"// comment{separator}" })
            Assert.ThrowsAny<Exception>(() => TestHarness.ParseOrThrow(
                $"{prefix} values() {{ yield{trivia}* [1]; }}"));
    }

    [Theory, ModeData]
    public void ParenthesizedBareYield_CanBeMultipliedAcrossLineTerminator(ExecutionMode mode)
    {
        const string source = """
            function* values() { console.log((yield)
                * 3); yield /* same line */ * [7]; }
            const generator = values();
            console.log(generator.next().value);
            console.log(generator.next(2).value);
            """;
        Assert.Equal("undefined\n6\n7\n", TestHarness.RunUnchecked(source, mode));
    }

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

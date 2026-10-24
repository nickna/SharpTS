using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class OrdinaryCallSpreadTests
{
    [Theory]
    [InlineData("m(...[1], 'bad');", "TS2345")]
    [InlineData("m(...[1], ...['bad']);", "TS2345")]
    [InlineData("m(...[1], ...42);", "TS2488")]
    [InlineData("m(...[1], missing);", "TS2304")]
    public void ArgumentsAfterSpreadsAreChecked(string call, string code)
    {
        var error = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(
            "function m(...args: number[]) {} " + call));
        Assert.Equal(code, error.Diagnostic.TsCode);
    }

    [Fact]
    public void MultipleCompatibleSpreadsAndValuesAreAccepted()
        => Assert.Equal("1,2,3\n", TestHarness.RunInterpreted("""
            function m(...args: number[]) { console.log(args.join(',')); }
            m(...[1], 2, ...[3]);
            """));
}

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

    [Theory]
    [InlineData("m(...['x', 1, 2] as const);", "x 1,2\n")]
    [InlineData("m(...['x'] as const);", "x \n")]
    [InlineData("m(...['x'] as const, ...[1, 2] as const);", "x 1,2\n")]
    public void FixedTupleElementsConsumeFixedParametersBeforeRest(string call, string expected)
        => Assert.Equal(expected, TestHarness.RunInterpreted(
            "function m(first: string, ...rest: number[]) { console.log(first, rest.join(',')); } " + call));

    [Fact]
    public void TupleSpreadRejectsAnIncompatibleFixedPrefix()
    {
        var error = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            function m(first: string, ...rest: number[]) {}
            m(...[1, 2, 3] as const);
            """));
        Assert.Equal("TS2345", error.Diagnostic.TsCode);
    }

    [Theory]
    [InlineData("function f(x: number) {} const a: number[] = [1]; f(...a);", "TS2556")]
    [InlineData("function f(x: number, ...rest: number[]) {} const a: number[] = [1]; f(...a);", "TS2556")]
    [InlineData("function f(x: number) {} f(...[] as const);", "TS2554")]
    [InlineData("function f(x: number) {} f(...[1, 2] as const);", "TS2554")]
    public void SpreadMustProveFixedParameterPositions(string source, string code)
    {
        var error = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Equal(code, error.Diagnostic.TsCode);
    }

    [Fact]
    public void ArraysAreAcceptedAfterASatisfiedFixedPrefix()
        => TestHarness.RunInterpreted("""
            function f(x: number, ...rest: number[]) {}
            const a: number[] = [2, 3];
            f(1, ...a);
            f(...[1] as const, ...a);
            """);

    [Fact]
    public void FreshArraySpreadsAreContextuallyTypedAsTuples()
        => TestHarness.RunInterpreted("""
            function f(x: number) {}
            function g(x: number, y: string) {}
            function h(callback: (value: number) => number) {}
            f(...[1]);
            g(...[1, 'ok']);
            h(...[value => value + 1]);
            """);
}

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
    [InlineData("class C { m(first: string, second: string, ...rest: any[]) {} } const three: string[] = ['x', 'y', 'a']; new C().m(...three);", "TS2556")]
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
    public void OpenSpreadsMaySupplyOptionalParameterPositions()
        => TestHarness.RunInterpreted("""
            function optional(value?: number) {}
            function pack(prefix: number = 10, ...values: number[]) {}
            function mixed(first: number = 1, second: string = 'ok', ...values: string[]) {}
            function* items(): Generator<number> { yield 1; yield 2; }
            const numbers: number[] = [1, 2];
            optional(...numbers);
            pack(...items(), 3, ...[4, 5]);
            mixed(...numbers, 'ok', ...['tail']);
            mixed(...[...numbers, 'ok', 'tail']);
            """);

    [Theory]
    [InlineData("function f(value: number, ...rest: number[]) {} f(...items());", "TS2556")]
    [InlineData("function f(value: number) {} f(...items());", "TS2556")]
    [InlineData("function f(value?: number) {} f(1, ...items());", "TS2556")]
    [InlineData("function f(value: string = 'ok', ...rest: number[]) {} f(...items());", "TS2345")]
    [InlineData("function f(first: number = 1, second: string = 'ok', ...rest: string[]) {} f(...items(), 2);", "TS2345")]
    [InlineData("function f(first: number = 1, second: string = 'ok', ...rest: string[]) {} f(...[...items(), 2]);", "TS2345")]
    [InlineData("function f(first: number = 1, ...rest: string[]) {} f(...items(), 2);", "TS2345")]
    public void OptionalPrefixSpreadsStillCheckRequiredPositionsAndLaterArguments(string source, string code)
    {
        var error = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(
            "function* items(): Generator<number> { yield 1; yield 2; } " + source));
        Assert.Equal(code, error.Diagnostic.TsCode);
    }

    [Fact]
    public void FreshArraySpreadsAreContextuallyTypedAsTuples()
        => TestHarness.RunInterpreted("""
            function f(x: number) {}
            function g(x: number, y: string) {}
            function h(callback: (value: number) => number) {}
            f(...[1]);
            f(...([1]));
            g(...[1, 'ok']);
            g(...[...[1] as const, 'ok']);
            h(...[value => value + 1]);
            """);

    [Fact]
    public void NestedUnknownArraySpreadsCannotProveFixedPositions()
    {
        var error = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted("""
            function g(x: number, y: string) {}
            g(...[...[1], 'ok']);
            """));
        Assert.Equal("TS2556", error.Diagnostic.TsCode);
    }
}

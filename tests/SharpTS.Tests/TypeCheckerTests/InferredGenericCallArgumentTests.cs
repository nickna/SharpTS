using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class InferredGenericCallArgumentTests
{
    [Theory]
    [InlineData("declare function same<T>(first: T, second: T): void; same(1, 'bad');")]
    [InlineData("declare function same<T>(first: T, second: T): void; same('bad', 1);")]
    [InlineData("declare function same<const T>(first: T, second: T): void; same(1, 'bad');")]
    [InlineData("declare function same<T extends number>(first: T, second: T): void; same(1, 'bad');")]
    [InlineData("declare function same<T>(...values: T[]): void; same(1, 'bad');")]
    [InlineData("declare function same<T>(first: T, second: T): void; const pair: [number, string] = [1, 'bad']; same(...pair);")]
    [InlineData("declare function same<T>(first: T, second: T): void; same<number>(1, 'bad');")]
    [InlineData("declare function same<T>(first: T, second: T): T; const value: number | string = same(1, 'bad');")]
    public void InferredAndExplicitCallsRejectIncompatiblePrimitiveArguments(string source)
    {
        var diagnostic = Assert.Single(Check(source).Diagnostics);
        Assert.Equal("TS2345", diagnostic.TsCode);
    }

    [Theory]
    [InlineData("same(1, 2);")]
    [InlineData("same('a', 'b');")]
    [InlineData("same(true, false);")]
    [InlineData("same(1, null);")]
    [InlineData("same(1, undefined);")]
    [InlineData("same(null, 1);")]
    [InlineData("same(undefined, 1);")]
    [InlineData("same({ a: 1 }, { b: 2 });")]
    [InlineData("same([1], ['a']);")]
    [InlineData("const pair: [number, number] = [1, 2]; same(...pair);")]
    [InlineData("class Base { base = 1; } class Child extends Base { child = 2; } same(new Base(), new Child()); same(new Child(), new Base());")]
    public void CompatibleCommonCandidatesRemainAccepted(string call)
    {
        Assert.Empty(Check("declare function same<T>(first: T, second: T): T; " + call).Diagnostics);
    }

    [Fact]
    public void RestSpreadsCallbacksConstraintsAndDefaultsRemainAccepted()
    {
        Assert.Empty(Check("""
            declare function collect<T>(...values: T[]): T[];
            const values: number[] = [1, 2];
            const collected: number[] = collect(...values);
            const nullable = collect(1, null, 2);
            declare function use<T>(value: T, callback: (value: T) => void): void;
            use(1, value => { const number: number = value; });
            declare function constrained<T extends number>(first: T, second: T): T;
            const result: number = constrained(1, 2);
            declare function create<T = number>(): T;
            const defaulted: number = create();
            """).Diagnostics);
    }

    [Fact]
    public void ConditionalCallbackParametersInferFromTheirApplicableBranch()
    {
        Assert.Empty(Check("""
            type Input<T> = T extends number ? { n: T } : { x: T };
            declare function use<T>(callback: (input: Input<T>) => void): T;
            declare function text(input: Input<string>): void;
            declare function number(input: Input<number>): void;
            const textResult: string = use(text);
            const numberResult: number = use(number);
            """).Diagnostics);
    }

    [Fact]
    public void ConditionalCallbackCannotInferAnInapplicableBranch()
    {
        var result = Check("""
            type Input<T> = T extends number ? { n: T } : { x: T };
            declare function use<T>(callback: (input: Input<T>) => void): T;
            declare function invalid(input: { x: number }): void;
            use(invalid);
            """);
        Assert.Equal("TS2345", Assert.Single(result.Diagnostics).TsCode);
    }

    [Fact]
    public void ReverseMappedCallbacksInferTheInputShape()
    {
        Assert.Empty(Check("""
            type Handlers<T> = { [K in keyof T]: (input: T[K]) => void };
            declare function on<T>(handlers: Handlers<T>): T;
            const empty = on({ test: () => {} });
            const typed = on({ test: (input: boolean) => {} });
            const result: boolean = typed.test;
            """).Diagnostics);
    }

    private static SharpTS.Diagnostics.TypeCheckDiagnosticResult Check(string source) =>
        new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(TestHarness.ParseOrThrow(source));
}

using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class PrivateStaticBracketAccessTests
{
    [Theory]
    [InlineData("class C { private static value = 1; } const value: number = C['value'];")]
    [InlineData("class C { private static '0' = 1; } const value: number = C[0];")]
    [InlineData("class C { private static read(): number { return 1; } } const value: number = C['read']();")]
    [InlineData("class C { private static get value(): number { return 1; } } const value: number = C['value'];")]
    [InlineData("class C { protected static value = 1; } const value: number = C['value'];")]
    [InlineData("class Base<T> { private static value = 1; } class C extends Base<number> {} const value: number = C['value'];")]
    [InlineData("class C { private static first = 1; private static second = 's'; } declare const key: 'first' | 'second'; const value: number | string = C[key];")]
    [InlineData("class Base { private static value = 1; } class C extends Base {} class D extends Base {} declare const constructor: typeof C | typeof D; const value: number = constructor['value'];")]
    public void LiteralBracketReadsRetainOrdinaryPrivateMemberTypes(string source)
    {
        new TypeChecker(TypeCheckerOptions.Strict).Check(TestHarness.ParseOrThrow(source));
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
    }

    [Theory]
    [InlineData("class C { private static value = 1; } C.value;", "TS2341")]
    [InlineData("class C { private static read(): number { return 1; } } C.read();", "TS2341")]
    [InlineData("class C { private static get value(): number { return 1; } } C.value;", "TS2341")]
    [InlineData("class C { protected static value = 1; } C.value;", "TS2445")]
    [InlineData("class C { private static value = 1; } const invalid: string = C['value'];", "TS2322")]
    [InlineData("class C { private static read(value: number): number { return value; } } C['read']('bad');", "TS2345")]
    [InlineData("class C { static #value = 1; } C['#value'];", "TS7053")]
    [InlineData("class C { static readonly value = 1; } C.value = 2;", "TS2540")]
    [InlineData("class C { private static readonly value = 1; } C['value']++;", "TS2540")]
    [InlineData("class C { private static readonly '0' = 1; } C[0] = 2;", "TS2540")]
    [InlineData("class C { private static readonly first = 1; private static second = 2; } declare const key: 'first' | 'second'; C[key]++;", "TS2540")]
    [InlineData("class C { private static value = 1; } class D { private static value = 2; } declare const constructor: typeof C | typeof D; constructor['value'];", "TS7053")]
    [InlineData("class C { private static value = 1; } class D { static value = 2; } declare const constructor: typeof C | typeof D; constructor['value'];", "TS7053")]
    public void BracketEscapePreservesDotVisibilityPrivateBrandsAndTypeChecks(string source, string code)
        => Assert.Equal(code, Assert.ThrowsAny<TypeCheckException>(() =>
            new TypeChecker(TypeCheckerOptions.Strict).Check(TestHarness.ParseOrThrow(source))).Diagnostic.TsCode);

    [Theory, ModeData]
    public void PrivateStaticLiteralReadsRunInBothModes(ExecutionMode mode)
        => Assert.Equal("1\n2\n3\n", TestHarness.Run("""
            class C {
                private static value = 1;
                private static read(): number { return 2; }
                private static get answer(): number { return 3; }
            }
            console.log(C['value']);
            console.log(C['read']());
            console.log(C['answer']);
            """, mode));
}

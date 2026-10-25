using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class NumericStaticPropertyTests
{
    private static List<Stmt> Parse(string source)
        => new Parser(new Lexer(source).ScanTokens()).Parse().Statements;

    [Theory]
    [InlineData("class C { static '0'(): number { return 1; } } const value: number = C[0]();")]
    [InlineData("class Base<T> { static '0': number = 1; } class Child<U> extends Base<U> {} const value: number = Child[0];")]
    [InlineData("class C { static '0': number = 1; } declare const key: 0; const value: number = C[key];")]
    [InlineData("class C { static '0': number = 1; static '1': string = 'a'; } declare const key: 0 | 1; const value: number | string = C[key];")]
    [InlineData("class C { static '0': number = 1; static text: string = 'a'; } declare const key: 0 | 'text'; const value: number | string = C[key];")]
    [InlineData("class C { static '0': number = 1; } class D { static '0': number = 2; } declare const value: typeof C | typeof D; const result: number = value[0];")]
    public void NumericKeysResolveStaticMembers(string source)
    {
        new TypeChecker(strictNullChecks: true).Check(Parse(source));
        Assert.Empty(new TypeChecker(strictNullChecks: true).CheckWithRecovery(Parse(source)).Diagnostics);
    }

    [Theory]
    [InlineData("class C { static '0'(): number { return 1; } } const invalid: string = C[0]();", "TS2322")]
    [InlineData("class C { '0'(): number { return 1; } } C[0]();", "TS7053")]
    public void NumericKeysPreserveStaticTypeAndAccessChecks(string source, string expected)
        => Assert.Equal(expected, Assert.ThrowsAny<TypeCheckException>(() =>
            new TypeChecker(strictNullChecks: true).Check(Parse(source))).Diagnostic.TsCode);

    [Theory, ModeData]
    public void NumericStaticAndRecordReadsExecuteInBothModes(ExecutionMode mode)
        => Assert.Equal("1\n2\n", TestHarness.Run("""
            class C { static "0"(): number { return 1; } }
            const object: { 0: number } = { 0: 2 };
            console.log(C[0]());
            console.log(object[0]);
            """, mode));
}

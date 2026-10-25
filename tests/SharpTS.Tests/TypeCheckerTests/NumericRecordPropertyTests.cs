using SharpTS.Parsing;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class NumericRecordPropertyTests
{
    private static List<Stmt> Parse(string source)
        => new Parser(new Lexer(source).ScanTokens()).Parse().Statements;

    [Theory]
    [InlineData("const object: { 0: number; readonly other: number } = { 0: 1, other: 1 }; const value: number = object[0]; object[0]++;")]
    [InlineData("declare const object: { 0: number } | { 0: number; extra: string }; const value: number = object[0];")]
    [InlineData("function read<T extends { 0: number }>(object: T): number { return object[0]; }")]
    [InlineData("declare const key: 0 | 1; declare const object: { 0: number; 1: string }; const value: number | string = object[key];")]
    [InlineData("declare const object: { 0: number; [key: number]: number | string }; const value: number = object[0];")]
    [InlineData("declare const key: 0 | 'name'; declare const object: { 0: number; name: string }; const value: number | string = object[key];")]
    [InlineData("declare const key: 0 | 'name'; declare const object: { 0: number; name: number }; object[key] = 2;")]
    public void NumericKeysResolveNamedProperties(string source)
    {
        new TypeChecker(strictNullChecks: true).Check(Parse(source));
        Assert.Empty(new TypeChecker(strictNullChecks: true).CheckWithRecovery(Parse(source)).Diagnostics);
    }

    [Theory]
    [InlineData("declare const object: { readonly 0: number }; object[0]++;", "TS2540")]
    [InlineData("declare const object: { readonly 0: number }; object[0] = 2;", "TS2540")]
    [InlineData("declare const object: { 0: number }; object[0] = 'bad';", "TS2322")]
    [InlineData("declare const object: { 0?: number }; const value: number = object[0];", "TS2322")]
    [InlineData("declare const object: { 0: number }; const value: string = object[0];", "TS2322")]
    [InlineData("declare const object: { 0: number }; object[1];", "TS7053")]
    [InlineData("declare const key: 0 | 'name'; declare const object: { 0: number; name: string }; object[key] = 2;", "TS2322")]
    public void NumericKeysPreserveValidation(string source, string expected)
    {
        Assert.Equal(expected, Assert.ThrowsAny<TypeCheckException>(() =>
            new TypeChecker(strictNullChecks: true).Check(Parse(source))).Diagnostic.TsCode);
        Assert.Equal(expected, Assert.Single(new TypeChecker(strictNullChecks: true)
            .CheckWithRecovery(Parse(source)).Diagnostics).TsCode);
    }
}

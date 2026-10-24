using SharpTS.Parsing;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class NumberPropertyValidationTests
{
    private static List<Stmt> Parse(string source)
        => new Parser(new Lexer(source).ScanTokens()).Parse().Statements;

    [Theory]
    [InlineData("const value: number = 1; value.length;")]
    [InlineData("const value = 1 as const; value.missing;")]
    [InlineData("function read<T extends number>(value: T) { return value.length; }")]
    [InlineData("function read(value: string | number) { return value.length; }")]
    public void MissingNumberPropertiesAreRejected(string source)
    {
        var statements = Parse(source);
        Assert.Equal("TS2339", Assert.ThrowsAny<TypeCheckException>(() => new TypeChecker().Check(statements)).Diagnostic.TsCode);
        var diagnostic = Assert.Single(new TypeChecker().CheckWithRecovery(statements).Diagnostics);
        Assert.Equal("TS2339", diagnostic.TsCode);
    }

    [Fact]
    public void NumberMethodsRetainTheirReturnTypes()
    {
        new TypeChecker().Check(Parse("""
            const value: number = 1;
            const fixed: string = value.toFixed(2);
            const precision: string = value.toPrecision();
            const exponential: string = value.toExponential(2);
            const text: string = value.toString(16);
            const localized: string = value.toLocaleString('en-US');
            const primitive: number = value.valueOf();
            function read<T extends number>(n: T): string { return n.toFixed(); }
            """));
        Assert.Equal("TS2322", Assert.ThrowsAny<TypeCheckException>(() => new TypeChecker().Check(Parse(
            "const value: number = 1; const invalid: number = value.toFixed();"))).Diagnostic.TsCode);
    }
}

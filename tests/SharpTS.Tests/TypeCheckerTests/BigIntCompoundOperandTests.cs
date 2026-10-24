using SharpTS.Parsing;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class BigIntCompoundOperandTests
{
    private static void Check(string source)
        => new TypeChecker(strictNullChecks: true).Check(new Parser(new Lexer(source).ScanTokens()).Parse().Statements);

    [Theory]
    [InlineData("*=")]
    [InlineData("/=")]
    [InlineData("%=")]
    [InlineData("-=")]
    [InlineData("+=")]
    [InlineData("**=")]
    [InlineData("<<=")]
    [InlineData(">>=")]
    [InlineData("&=")]
    [InlineData("|=")]
    [InlineData("^=")]
    public void BigIntAndDynamicOperandsAreAccepted(string operation)
    {
        Check($"let value: any = 2n; value {operation} 3n;");
        Check($"let value: bigint = 2n; value {operation} 3n;");
    }

    [Theory]
    [InlineData("let value = 0; if (value === 0) value += 1;")]
    [InlineData("let value: bigint = 0n; if (value === 0n) value += 1n;")]
    [InlineData("let value: string = ''; if (value === '') value += 'next';")]
    [InlineData("let value: 0 = 0; value += 1;")]
    [InlineData("let value: 0n = 0n; value += 1n;")]
    [InlineData("let value: '' = ''; value += 'next';")]
    public void CompoundAdditionWidensLiteralOperands(string source)
        => Check(source);

    [Theory]
    [InlineData("let value: bigint = 2n; value *= 3;", "TS2365")]
    [InlineData("let value: number = 2; value *= 3n;", "TS2365")]
    [InlineData("let value: bigint = 2n; value >>>= 1n;", "TS2791")]
    [InlineData("let value: bigint = 2n; value *= true;", "TS2363")]
    [InlineData("let value = 0; if (value === 0) value += 'next';", "TS2365")]
    [InlineData("let value: bigint = 0n; if (value === 0n) value += 1;", "TS2365")]
    public void InvalidNumericPairingsRemainRejected(string source, string code)
        => Assert.Equal(code, Assert.ThrowsAny<TypeCheckException>(() => Check(source)).Diagnostic.TsCode);
}

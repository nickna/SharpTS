using SharpTS.Parsing;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public class NullableGenericTypeofTests
{
    private static void Check(string source) => new TypeChecker(strictNullChecks: true)
        .Check(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());

    [Fact]
    public void TypeofObjectRetainsTheNullPossibility()
        => Assert.Equal("TS2322", Assert.ThrowsAny<TypeCheckException>(() => Check("""
            function read<T>(value: T) {
                if (typeof value === 'object') {
                    const original: T = value;
                    const wrong: object = value;
                }
            }
            """)).Diagnostic.TsCode);

    [Theory]
    [InlineData("value !== null")]
    [InlineData("value != null")]
    public void ExplicitNullExclusionEstablishesTheObjectFacet(string condition)
        => Check($$"""
            function read<T>(value: T) {
                if (typeof value === 'object' && {{condition}}) {
                    const correct: object = value;
                    const original: T = value;
                }
            }
            """);

    [Fact]
    public void NonNullableConstraintIsAlreadyAnObject()
        => Check("""
            function read<T extends object>(value: T) {
                if (typeof value === 'object') { const correct: object = value; }
            }
            """);
}

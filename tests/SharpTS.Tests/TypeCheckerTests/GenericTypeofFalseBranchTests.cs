using SharpTS.Parsing;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public class GenericTypeofFalseBranchTests
{
    private static void Check(string source) => new TypeChecker(strictNullChecks: true)
        .Check(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());

    [Theory]
    [InlineData("typeof value === 'object'", "else")]
    [InlineData("typeof value !== 'object'", "then")]
    public void FalseFacetKeepsItsSourceParameterAndSelectedConstraint(string condition, string branch)
    {
        string body = "const selected: string = value; const original: T = value;";
        Check($$"""
            interface Box { field: string; }
            function read<T extends Box | string>(value: T) {
                if ({{condition}}) { {{(branch == "then" ? body : "")}} }
                else { {{(branch == "else" ? body : "")}} }
            }
            """);
    }

    [Fact]
    public void UnrelatedFalseFacetRemainsInvalid()
        => Assert.Equal("TS2322", Assert.ThrowsAny<TypeCheckException>(() => Check("""
            interface Box { field: string; }
            function read<T extends Box | number>(value: T) {
                if (typeof value === 'object') {} else { const wrong: string = value; }
            }
            """)).Diagnostic.TsCode);

    [Fact]
    public void OpenParameterDoesNotAcquireAnUnprovedPrimitiveFacet()
        => Assert.Equal("TS2322", Assert.ThrowsAny<TypeCheckException>(() => Check("""
            function read<T>(value: T) {
                if (typeof value === 'object') {} else { const wrong: string = value; }
            }
            """)).Diagnostic.TsCode);
}

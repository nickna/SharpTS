using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;
namespace SharpTS.Tests.TypeCheckerTests;
public class KnownInstanceBracketTypeTests
{
    [Theory]
    [InlineData("class C { value: number = 7; }")]
    [InlineData("class C { get value(): number { return 7; } }")]
    [InlineData("const C = class { get value(): number { return 7; } };")]
    [InlineData("class B<T> { value: T; } class C extends B<number> {}")]
    public void KnownBracketMember_RetainsItsDeclaredType(string declaration)
    {
        foreach (string resultType in new[] { "number", "string" })
        {
            var source = declaration + " const result: " + resultType + " = new C()[\"value\"];";
            var result = new TypeChecker(strictNullChecks: true).CheckWithRecovery(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
            Assert.Equal(resultType == "string", result.Diagnostics.Any(d => d.TsCode == "TS2322"));
        }
    }
}

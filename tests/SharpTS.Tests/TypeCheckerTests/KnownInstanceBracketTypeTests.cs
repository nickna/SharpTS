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
    [Theory]
    [InlineData("private value: number = 7;", "TS2341")]
    [InlineData("protected value: number = 7;", "TS2445")]
    [InlineData("private get value(): number { return 7; }", "TS2341")]
    [InlineData("protected get value(): number { return 7; }", "TS2445")]
    public void LiteralBracketVisibilityEscape_KeepsPreciseType(string member, string visibilityCode)
    {
        var declaration = "class C { " + member + " } const c = new C(); ";
        var valid = new TypeChecker().CheckWithRecovery(new Parser(new Lexer(declaration + "const value: number = c['value'];").ScanTokens()).ParseOrThrow());
        Assert.Empty(valid.Diagnostics);
        var invalid = new TypeChecker().CheckWithRecovery(new Parser(new Lexer(declaration + "const value: string = c['value'];").ScanTokens()).ParseOrThrow());
        Assert.Contains(invalid.Diagnostics, d => d.TsCode == "TS2322");
        var dot = new TypeChecker().CheckWithRecovery(new Parser(new Lexer(declaration + "const value: number = c.value;").ScanTokens()).ParseOrThrow());
        Assert.Contains(dot.Diagnostics, d => d.TsCode == visibilityCode);
    }
}

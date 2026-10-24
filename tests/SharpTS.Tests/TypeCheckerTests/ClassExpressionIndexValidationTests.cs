using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;
namespace SharpTS.Tests.TypeCheckerTests;
public class ClassExpressionIndexValidationTests
{
    [Theory]
    [InlineData("const C = class { [key: string]: number; wrong: string = 'bad'; };", "TS2411")]
    [InlineData("const C = class { [key: symbol]: number; [Symbol.toStringTag]: string = 'bad'; };", "TS2411")]
    [InlineData("class Base { [key: string]: number; } const C = class extends Base { [key: string]: string; };", "TS2415")]
    public void InvalidExpressionIndexes_AreValidated(string source, string code)
    {
        var result = new TypeChecker(strictNullChecks: true).CheckWithRecovery(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
        Assert.Contains(result.Diagnostics, d => d.TsCode == code);
    }
    [Fact]
    public void MatchingExpressionIndex_RemainsValid()
    {
        var source = "const C = class { [key: string]: number; value: number = 7; }; const value: number = new C().value;";
        Assert.Empty(new TypeChecker(strictNullChecks: true).CheckWithRecovery(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow()).Diagnostics);
    }
}

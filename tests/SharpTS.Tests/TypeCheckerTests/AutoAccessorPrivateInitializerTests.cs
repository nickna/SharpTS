using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;
namespace SharpTS.Tests.TypeCheckerTests;
public class AutoAccessorPrivateInitializerTests
{
    [Theory]
    [InlineData("", "this.#stored", "1")]
    [InlineData("static ", "this.#stored", "1")]
    public void AutoAccessorInitializer_RetainsPrivateOwner(string modifier, string read, string initial)
    {
        var source = "class C { " + modifier + "#stored = " + initial + "; " + modifier + "accessor value: number = " + read + "; }";
        Assert.Empty(new TypeChecker(strictNullChecks: true).CheckWithRecovery(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow()).Diagnostics);
        source += " const invalid: string = " + (modifier.Length == 0 ? "new C()" : "C") + ".value;";
        Assert.Contains(new TypeChecker(strictNullChecks: true).CheckWithRecovery(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow()).Diagnostics, d => d.TsCode == "TS2322");
    }
    [Theory]
    [InlineData("class C", "", "", "new C()")]
    [InlineData("const C = class", ";", "", "new C()")]
    [InlineData("class C", "", "static ", "C")]
    [InlineData("const C = class", ";", "static ", "C")]
    public void InferredAutoAccessorInitializer_KeepsPrivateContextAndPublishesItsType(string declaration, string ending, string modifier, string receiver)
    {
        var source = declaration + " { " + modifier + "#stored = 1; " + modifier + "accessor value = this.#stored; }" + ending;
        foreach (string type in new[] { "number", "string" })
        {
            var result = new TypeChecker(strictNullChecks: true).CheckWithRecovery(new Parser(new Lexer(source + " const value: " + type + " = " + receiver + ".value;").ScanTokens()).ParseOrThrow());
            if (type == "number") Assert.Empty(result.Diagnostics);
            else Assert.Contains(result.Diagnostics, d => d.TsCode == "TS2322");
        }
    }
}

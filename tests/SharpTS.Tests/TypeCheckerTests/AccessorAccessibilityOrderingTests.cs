using SharpTS.Diagnostics;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;
namespace SharpTS.Tests.TypeCheckerTests;
public class AccessorAccessibilityOrderingTests
{
    [Theory]
    [InlineData("class C", "")]
    [InlineData("const C = class", ";")]
    public void GetterMustBeAtLeastAsAccessibleAsSetter(string declaration, string ending)
    {
        foreach (string modifier in new[] { "", "static " })
        {
            var invalid = declaration + " { private " + modifier + "get value(): number { return 7; } public " + modifier + "set value(next: number) {} }" + ending;
            Assert.Equal(2, Check(invalid).Diagnostics.Count(d => d.TsCode == "TS2808"));
            var valid = declaration + " { public " + modifier + "get value(): number { return 7; } private " + modifier + "set value(next: number) {} }" + ending;
            Assert.Empty(Check(valid).Diagnostics);
        }
    }
    private static TypeCheckDiagnosticResult Check(string source) => new TypeChecker(strictNullChecks: true)
        .CheckWithRecovery(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
}

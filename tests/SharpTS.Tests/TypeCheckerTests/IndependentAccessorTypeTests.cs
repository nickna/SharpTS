using SharpTS.Diagnostics;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public class IndependentAccessorTypeTests
{
    [Theory]
    [InlineData("class C", "", "new C()", "")]
    [InlineData("const C = class", ";", "new C()", "")]
    [InlineData("class C", "", "C", "static ")]
    [InlineData("const C = class", ";", "C", "static ")]
    public void ExplicitAccessorTypes_KeepIndependentReadAndWriteTypes(string declaration, string ending, string receiver, string modifier)
    {
        var source = declaration + " { " + modifier + "get value(): string { return \"read\"; } " + modifier + "set value(next: number) {} }" + ending;
        Assert.Empty(Check(source + $" {receiver}.value = 3; const read: string = {receiver}.value;").Diagnostics);
        Assert.Contains(Check(source + $" {receiver}.value = \"bad\";").Diagnostics, d => d.TsCode == "TS2322");
        Assert.Contains(Check(source + $" const read: number = {receiver}.value;").Diagnostics, d => d.TsCode == "TS2322");
    }

    private static TypeCheckDiagnosticResult Check(string source) => new TypeChecker(strictNullChecks: true)
        .CheckWithRecovery(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
}


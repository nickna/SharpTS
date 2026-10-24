using SharpTS.Diagnostics;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public class ComputedLiteralClassFieldTests
{
    [Theory]
    [InlineData("class C", "")]
    [InlineData("const C = class", ";")]
    public void LiteralFields_PublishPreciseMemberTypes(string declaration, string ending)
    {
        var source = declaration + " { [\"known\"]: string = \"ok\"; [7]: number = 1; read(): string { return this.known; } }" + ending;
        var valid = source + " const good: string = new C().known; const numeric: number = new C()[7];";
        Assert.Empty(Check(valid).Diagnostics);
        var invalid = source + " const bad: number = new C().known;";
        Assert.Contains(Check(invalid).Diagnostics, diagnostic => diagnostic.TsCode == "TS2322");
    }

    private static TypeCheckDiagnosticResult Check(string source) => new TypeChecker(strictNullChecks: true)
        .CheckWithRecovery(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
}

using SharpTS.Diagnostics;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public class ComputedLiteralClassMethodTests
{
    [Theory]
    [InlineData("class C", "")]
    [InlineData("const C = class", ";")]
    public void LiteralMethods_PublishInferredReturns(string declaration, string ending)
    {
        var source = declaration + " { [\"read\"]() { return 42; } static [\"staticRead\"]() { return 7; } }" + ending;
        Assert.Empty(Check(source + " const n: number = new C().read(); const s: number = C.staticRead();").Diagnostics);
        Assert.Contains(Check(source + " const invalid: string = new C().read();").Diagnostics,
            diagnostic => diagnostic.TsCode == "TS2322");
    }

    private static TypeCheckDiagnosticResult Check(string source) => new TypeChecker(strictNullChecks: true)
        .CheckWithRecovery(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
}


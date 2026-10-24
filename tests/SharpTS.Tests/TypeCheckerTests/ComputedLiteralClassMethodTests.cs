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

    [Theory]
    [InlineData("class C", "", "\"\"")]
    [InlineData("const C = class", ";", "\"\"")]
    [InlineData("class C", "", "\"read\"")]
    [InlineData("const C = class", ";", "\"read\"")]
    [InlineData("class C", "", "0")]
    [InlineData("const C = class", ";", "0")]
    public void LiteralMethodsKeepStaticAndInstanceSignaturesIndependent(
        string declaration, string ending, string key)
    {
        string source = $$"""
            {{declaration}} {
                [{{key}}](): number { return 1; }
                static [{{key}}](): string { return "static"; }
            }{{ending}}
            """;
        string access = key == "0" ? "\"0\"" : key;
        Assert.Empty(Check(source + $" const n: number = new C()[{access}](); const s: string = C[{access}]();").Diagnostics);
        Assert.Contains(Check(source + $" const invalid: string = new C()[{access}]();").Diagnostics,
            diagnostic => diagnostic.TsCode == "TS2322");
        Assert.Contains(Check(source + $" const invalid: number = C[{access}]();").Diagnostics,
            diagnostic => diagnostic.TsCode == "TS2322");
    }

    [Theory]
    [InlineData("")]
    [InlineData("static ")]
    public void DuplicateLiteralMethodsOnTheSameFacetStillReject(string modifier)
    {
        Assert.Contains(Check($$"""
            class C {
                {{modifier}}[""]() { return 1; }
                {{modifier}}[""]() { return 2; }
            }
            """).Diagnostics, diagnostic => diagnostic.TsCode == "TS2393");
    }

    [Theory]
    [InlineData("Symbol.iterator")]
    [InlineData("((Symbol.iterator))")]
    public void SymbolOverloadsRetainStaticConsistencyDiagnostics(string key)
    {
        var result = Check($$"""
            class C {
                [{{key}}](value: string): string;
                static [{{key}}](value: number): number;
                [{{key}}](value: any): any { return value; }
            }
            """);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.TsCode == "TS2387");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.TsCode == "TS2388");
    }

    private static TypeCheckDiagnosticResult Check(string source) => new TypeChecker(strictNullChecks: true)
        .CheckWithRecovery(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
}

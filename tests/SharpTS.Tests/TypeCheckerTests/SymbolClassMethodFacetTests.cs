using SharpTS.Diagnostics;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public class SymbolClassMethodFacetTests
{
    [Theory]
    [InlineData("class C", "")]
    [InlineData("const C = class", ";")]
    public void StaticAndInstanceImplementationsRetainIndependentTypes(string declaration, string ending)
    {
        string source = declaration + " { [Symbol.iterator](): number { return 1; } " +
            "static [Symbol.iterator](): string { return 'static'; } }" + ending;
        Assert.Empty(Check(source + " const n: number = new C()[Symbol.iterator](); const s: string = C[Symbol.iterator]();").Diagnostics);
        Assert.Contains(Check(source + " const wrong: string = new C()[Symbol.iterator]();").Diagnostics,
            diagnostic => diagnostic.TsCode == "TS2322");
        Assert.Contains(Check(source + " const wrong: number = C[Symbol.iterator]();").Diagnostics,
            diagnostic => diagnostic.TsCode == "TS2322");
    }

    [Fact]
    public void IndependentOverloadGroupsAreAccepted()
    {
        Assert.Empty(Check("""
            class C {
                [Symbol.iterator](value: number): number;
                [Symbol.iterator](value: any): any { return value; }
                static [Symbol.iterator](value: string): string;
                static [Symbol.iterator](value: any): any { return value; }
            }
            """).Diagnostics);
    }

    [Fact]
    public void DuplicateImplementationOnOneFacetStillRejects()
    {
        Assert.Contains(Check("""
            class C {
                [Symbol.iterator]() { return 1; }
                [Symbol.iterator]() { return 2; }
                static [Symbol.iterator]() { return 3; }
            }
            """).Diagnostics, diagnostic => diagnostic.TsCode == "TS2393");
    }

    private static TypeCheckDiagnosticResult Check(string source) => new TypeChecker(strictNullChecks: true)
        .CheckWithRecovery(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
}

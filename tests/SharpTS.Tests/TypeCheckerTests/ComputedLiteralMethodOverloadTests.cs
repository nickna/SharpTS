using SharpTS.Diagnostics;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public class ComputedLiteralMethodOverloadTests
{
    [Theory]
    [InlineData("class C", "", false)]
    [InlineData("const C = class", ";", false)]
    [InlineData("class C", "", true)]
    [InlineData("const C = class", ";", true)]
    public void CallsUsePublicOverloadReturns(string declaration, string ending, bool isStatic)
    {
        foreach (string returnAnnotation in new[] { "", ": number | string" })
        {
            string modifier = isStatic ? "static " : "";
            string receiver = isStatic ? "C" : "new C()";
            string source = declaration + " { " +
                modifier + "['read'](value: number): number; " +
                modifier + "['read'](value: string): string; " +
                modifier + "['read'](value: number | string)" + returnAnnotation + " { return value; } }" + ending;
            Assert.Empty(Check(source + " const n: number = " + receiver + ".read(1); " +
                "const s: string = " + receiver + ".read('ok');").Diagnostics);
            Assert.Contains(Check(source + " const wrong: string = " + receiver + ".read(1);").Diagnostics,
                diagnostic => diagnostic.TsCode == "TS2322");
        }
    }

    [Theory]
    [InlineData("class C", "", false)]
    [InlineData("const C = class", ";", false)]
    [InlineData("class C", "", true)]
    [InlineData("const C = class", ";", true)]
    public void ImplementationSignatureIsNotExposed(string declaration, string ending, bool isStatic)
    {
        string modifier = isStatic ? "static " : "";
        string receiver = isStatic ? "C" : "new C()";
        string source = declaration + " { " +
            modifier + "['read'](value: number): number; " +
            modifier + "['read'](value: string): string; " +
            modifier + "['read'](value: any): any { return value; } }" + ending;
        Assert.Contains(Check(source + " " + receiver + ".read(true);").Diagnostics,
            diagnostic => diagnostic.TsCode == "TS2769");
    }

    [Theory]
    [InlineData("class C", "")]
    [InlineData("const C = class", ";")]
    public void NumericAndEmptyLiteralNamesRetainOverloads(string declaration, string ending)
    {
        foreach (string key in new[] { "7", "''" })
        {
            string source = declaration + " { [" + key + "](value: number): number; [" + key +
                "](value: string): string; [" + key + "](value: number | string): number | string { return value; } }" + ending;
            Assert.Empty(Check(source + " const result: number = new C()[" + key + "](1);").Diagnostics);
        }
    }

    [Theory]
    [InlineData("class C", "")]
    [InlineData("const C = class", ";")]
    public void GenericPublicOverloadsAreRetained(string declaration, string ending)
    {
        string source = declaration + " { ['read']<T>(value: T): T; ['read'](value: number): number; " +
            "['read'](value: any): any { return value; } }" + ending;
        Assert.Empty(Check(source + " const n: number = new C().read(1); const s: string = new C().read('ok');").Diagnostics);
        Assert.Contains(Check(source + " const wrong: string = new C().read(1);").Diagnostics,
            diagnostic => diagnostic.TsCode == "TS2322");
    }

    [Theory]
    [InlineData("class C", "")]
    [InlineData("const C = class", ";")]
    public void ImplementationCannotRequireMoreArgumentsThanPublicOverload(string declaration, string ending)
    {
        string source = declaration + " { ['read'](value: number): number; " +
            "['read'](value: number, extra: number): number { return value; } }" + ending;
        Assert.Contains(Check(source).Diagnostics, diagnostic => diagnostic.TsCode == "TS2394");
    }

    [Theory]
    [InlineData("class C", "")]
    [InlineData("const C = class", ";")]
    public void ComputedConstructorOverloadsKeepConstructionIndependent(string declaration, string ending)
    {
        string source = declaration + " { constructor(value: boolean) {} " +
            "['constructor'](value: number): number; ['constructor'](value: string): string; " +
            "['constructor'](value: any): any { return value; } }" + ending;
        Assert.Empty(Check(source + " const n: number = new C(true)['constructor'](1); " +
            "const s: string = new C(false)['constructor']('ok');").Diagnostics);
        Assert.Contains(Check(source + " new C(1);").Diagnostics, diagnostic => diagnostic.TsCode == "TS2345");
        Assert.Contains(Check(source + " new C(true)['constructor'](true);").Diagnostics,
            diagnostic => diagnostic.TsCode == "TS2769");
    }

    private static TypeCheckDiagnosticResult Check(string source) => new TypeChecker()
        .CheckWithRecovery(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
}

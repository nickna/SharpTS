using SharpTS.Diagnostics;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public class ComputedConstructorSignatureTests
{
    [Theory]
    [InlineData("class C", "", false)]
    [InlineData("class C", "", true)]
    [InlineData("const C = class", ";", false)]
    [InlineData("const C = class", ";", true)]
    public void ComputedMethodAndRealConstructorRetainIndependentSignatures(
        string declaration, string ending, bool methodFirst)
    {
        const string constructor = "constructor(value: number) {}";
        const string method = "[\"constructor\"](suffix: string) { return 42; }";
        string source = declaration + " { " + (methodFirst ? method + constructor : constructor + method) + " }" + ending;
        Assert.Empty(Check(source + " const value: number = new C(3)['constructor']('ok');").Diagnostics);
        Assert.Contains(Check(source + " new C('wrong');").Diagnostics, diagnostic => diagnostic.TsCode == "TS2345");
        Assert.Contains(Check(source + " new C();").Diagnostics, diagnostic => diagnostic.TsCode == "TS2554");
        Assert.Contains(Check(source + " new C(3)['constructor'](false);").Diagnostics,
            diagnostic => diagnostic.TsCode == "TS2345");
        Assert.Contains(Check(source + " const wrong: string = new C(3)['constructor']('ok');").Diagnostics,
            diagnostic => diagnostic.TsCode == "TS2322");
    }

    [Theory]
    [InlineData("class C", "")]
    [InlineData("const C = class", ";")]
    public void ComputedMethodDoesNotSupplyDefaultConstructionParameters(string declaration, string ending)
    {
        string source = declaration + " { ['constructor'](value: number) { return value; } }" + ending;
        Assert.Empty(Check(source + " const value: number = new C()['constructor'](3);").Diagnostics);
        Assert.Contains(Check(source + " new C(3);").Diagnostics, diagnostic => diagnostic.TsCode == "TS2554");
    }

    [Theory]
    [InlineData("class C", "")]
    [InlineData("const C = class", ";")]
    public void ComputedMethodPreservesPublicConstructorOverloads(string declaration, string ending)
    {
        string source = declaration + " { constructor(value: number); constructor(value: string); " +
            "constructor(value: any) {} ['constructor']() { return true; } }" + ending;
        Assert.Empty(Check(source + " new C(3); const value: boolean = new C('ok')['constructor']();").Diagnostics);
        Assert.Contains(Check(source + " new C(false);").Diagnostics, diagnostic => diagnostic.TsCode == "TS2769");
    }

    [Fact]
    public void GenericComputedMethodSubstitutesItsOwnReturnType()
    {
        const string source = "class C<T> { constructor(value: T) {} ['constructor'](value: T): T { return value; } }";
        Assert.Empty(Check(source + " const value: string = new C<string>('ok')['constructor']('ok');").Diagnostics);
        Assert.Contains(Check(source + " const wrong: number = new C<string>('ok')['constructor']('ok');").Diagnostics,
            diagnostic => diagnostic.TsCode == "TS2322");
    }

    [Theory]
    [InlineData("class C", "")]
    [InlineData("const C = class", ";")]
    public void InstanceStructuralProjectionUsesTheComputedMethod(string declaration, string ending)
    {
        string source = declaration +
            " { constructor(value: number) {} ['constructor'](value: string): number { return 42; } }" + ending;
        Assert.Empty(Check(source + " const shape: {constructor: (value: string) => number} = new C(3);").Diagnostics);
        Assert.Contains(Check(source + " const wrong: {constructor: (value: string) => string} = new C(3);").Diagnostics,
            diagnostic => diagnostic.TsCode == "TS2322");
    }

    private static TypeCheckDiagnosticResult Check(string source) => new TypeChecker(strictNullChecks: true)
        .CheckWithRecovery(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
}

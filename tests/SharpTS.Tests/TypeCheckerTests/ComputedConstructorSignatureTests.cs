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

    [Theory]
    [InlineData("", false)]
    [InlineData(", extra: number", true)]
    [InlineData(", extra?: number", false)]
    public void GenericConstructorAssignmentUsesRealConstructionArity(string extra, bool rejected)
    {
        string source = $$"""
            class Target<T> {
                constructor(value: T) {}
                ["constructor"](value: string): number { return 1; }
            }
            class Source<U> {
                constructor(value: U{{extra}}) {}
                ["constructor"](value: string): number { return 1; }
            }
            const Alias: typeof Target = Source;
            """;
        AssertRejected(Check(source), rejected);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void MixedConstructorAssignmentKeepsComputedMethodsSeparateFromConstruction(
        bool targetGeneric, bool sourceGeneric)
    {
        string source = $$"""
            class Target{{(targetGeneric ? "<T>" : "")}} {
                constructor(value: {{(targetGeneric ? "T" : "number")}}) {}
                ["constructor"](value: string): number { return 1; }
            }
            class Source{{(sourceGeneric ? "<U>" : "")}} {
                constructor(value: {{(sourceGeneric ? "U" : "number")}}, extra: number) {}
                ["constructor"](value: string): number { return 1; }
            }
            const Alias: typeof Target = Source;
            """;
        AssertRejected(Check(source), true);
    }

    [Theory]
    [InlineData("string", "number", false)]
    [InlineData("number", "number", true)]
    [InlineData("string", "string", true)]
    public void GenericConstructorAssignmentAlsoChecksComputedInstanceMethod(
        string parameter, string result, bool rejected)
    {
        string source = $$"""
            class Target<T> {
                constructor(value: T) {}
                ["constructor"](value: string): number { return 1; }
            }
            class Source<U> {
                constructor(value: U) {}
                ["constructor"](value: {{parameter}}): {{result}} { return 1 as any; }
            }
            const Alias: typeof Target = Source;
            """;
        AssertRejected(Check(source), rejected);
    }

    [Theory]
    [InlineData("string", "number", false)]
    [InlineData("number", "number", true)]
    [InlineData("string", "string", true)]
    public void DistinctInstancesCheckComputedMethodButIgnoreRealConstructionSignatures(
        string parameter, string result, bool rejected)
    {
        string source = $$"""
            class Target {
                constructor(value: number) {}
                ["constructor"](value: string): number { return 1; }
            }
            class Source {
                constructor(value: boolean, extra: number) {}
                ["constructor"](value: {{parameter}}): {{result}} { return 1 as any; }
            }
            declare const source: Source;
            const target: Target = source;
            """;
        AssertRejected(Check(source), rejected);
    }

    [Theory]
    [InlineData("string", "number", false)]
    [InlineData("number", "number", true)]
    [InlineData("string", "string", true)]
    public void RecordsAndInstancesProjectTheComputedMethodInBothDirections(
        string parameter, string result, bool rejected)
    {
        const string declaration = """
            class C {
                constructor(value: boolean, extra: number) {}
                ["constructor"](value: string): number { return 1; }
            }
            """;
        AssertRejected(Check(declaration + $$"""
            declare const value: C;
            const shape: { constructor: (value: {{parameter}}) => {{result}} } = value;
            """), rejected);
        AssertRejected(Check(declaration + $$"""
            declare const shape: { constructor: (value: {{parameter}}) => {{result}} };
            const value: C = shape;
            """), rejected);
    }

    [Theory]
    [InlineData("number", false)]
    [InlineData("string", true)]
    public void InheritedGenericComputedMethodsRetainSubstitutionInRecordProjections(
        string result, bool rejected)
    {
        string source = $$"""
            class Base<T> {
                constructor(value: T) {}
                ["constructor"](value: T): T { return value; }
            }
            class Derived extends Base<number> {}
            declare const value: Derived;
            const shape: { constructor: (value: number) => {{result}} } = value;
            """;
        AssertRejected(Check(source), rejected);
    }

    private static void AssertRejected(TypeCheckDiagnosticResult result, bool rejected)
    {
        if (rejected)
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.TsCode == "TS2322");
        else
            Assert.Empty(result.Diagnostics);
    }

    private static TypeCheckDiagnosticResult Check(string source) => new TypeChecker(strictNullChecks: true)
        .CheckWithRecovery(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
}

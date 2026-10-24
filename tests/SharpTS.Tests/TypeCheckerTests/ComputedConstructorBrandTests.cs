using SharpTS.Diagnostics;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class ComputedConstructorBrandTests
{
    [Theory]
    [InlineData("private", "")]
    [InlineData("private", "<T>")]
    [InlineData("protected", "")]
    [InlineData("protected", "<T>")]
    public void InheritedNonPublicComputedMethodRetainsItsNominalBrand(string access, string parameters)
    {
        string type = parameters.Length == 0 ? "Child" : "Child<number>";
        string declaration = $$"""
            class Base { {{access}} ["constructor"](): number { return 1; } }
            class Middle extends Base {}
            class Child{{parameters}} extends Middle {}
            """;
        var diagnostics = Check(declaration + $"const child: {type} = {{}};");
        Assert.Contains(diagnostics.Diagnostics, diagnostic => diagnostic.TsCode == "TS2322");
        Assert.Empty(Check(declaration + $"const child: {type} = new {type}();").Diagnostics);
    }

    [Fact]
    public void InheritedPublicComputedMethodStillAcceptsItsStructuralContract()
    {
        Assert.Empty(Check("""
            class Base { ["constructor"](): number { return 1; } }
            class Child<T> extends Base {}
            const child: Child<number> = { constructor: (): number => 1 };
            """).Diagnostics);
    }

    [Theory]
    [InlineData("private", false)]
    [InlineData("private", true)]
    [InlineData("protected", false)]
    [InlineData("protected", true)]
    public void InstantiatedGenericAncestorsRetainTheComputedMethodsBrand(string access, bool genericChild)
    {
        string type = genericChild ? "Child<number>" : "Child";
        string declaration = $$"""
            class Base<T> { {{access}} ["constructor"](): number { return 1; } }
            class Middle<T> extends Base<T> {}
            class Child{{(genericChild ? "<T>" : "")}} extends Middle<{{(genericChild ? "T" : "number")}}> {}
            """;
        Assert.Contains(Check(declaration + $"const child: {type} = {{}};").Diagnostics,
            diagnostic => diagnostic.TsCode == "TS2322");
        Assert.Empty(Check(declaration + $"const child: {type} = new {type}();").Diagnostics);
    }

    [Fact]
    public void InheritedPublicGenericComputedMethodKeepsItsStructuralContract()
    {
        Assert.Empty(Check("""
            class Base<T> { ["constructor"](value: T): T { return value; } }
            class Child<T> extends Base<T> {}
            const child: Child<number> = { constructor: (value: number): number => value };
            """).Diagnostics);
    }

    private static TypeCheckDiagnosticResult Check(string source) => new TypeChecker(strictNullChecks: true)
        .CheckWithRecovery(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
}

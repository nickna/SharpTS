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

    private static TypeCheckDiagnosticResult Check(string source) => new TypeChecker(strictNullChecks: true)
        .CheckWithRecovery(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
}

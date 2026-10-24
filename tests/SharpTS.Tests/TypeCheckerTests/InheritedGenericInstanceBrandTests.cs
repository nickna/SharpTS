using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class InheritedGenericInstanceBrandTests
{
    [Theory]
    [InlineData("private p", false)]
    [InlineData("private p", true)]
    [InlineData("protected p", false)]
    [InlineData("protected p", true)]
    [InlineData("#p", false)]
    [InlineData("#p", true)]
    public void GenericAncestorsRetainInstanceBrands(string field, bool genericChild)
    {
        string type = genericChild ? "Child<number>" : "Child";
        string declarations = $$"""
            class Base<T> { {{field}} = 1; }
            class Middle<T> extends Base<T> {}
            class Child{{(genericChild ? "<T>" : "")}} extends Middle<{{(genericChild ? "T" : "number")}}> {}
            """;
        var missingBrand = Check(declarations + $"const child: {type} = {{}};");
        Assert.Single(missingBrand.Diagnostics);
        Assert.Contains(missingBrand.Diagnostics, diagnostic => diagnostic.TsCode is "TS2741" or "TS2322");
        Assert.Empty(Check(declarations + $"const child: {type} = new {type}();").Diagnostics);
    }

    [Fact]
    public void PublicGenericAncestorsRemainStructural()
    {
        Assert.Empty(Check("""
            class Base<T> { p: T = undefined as any; }
            class Child<T> extends Base<T> {}
            const child: Child<number> = { p: 1 };
            """).Diagnostics);
    }

    [Fact]
    public void GenericDerivedInstanceKeepsTheInheritedBrandAndSubstitution()
    {
        Assert.Empty(Check("""
            class Base<T> { private p = 1; value: T = undefined as any; }
            class Child<T> extends Base<T> {}
            const child: Base<number> = new Child<number>();
            """).Diagnostics);
    }

    private static SharpTS.Diagnostics.TypeCheckDiagnosticResult Check(string source) =>
        new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(TestHarness.ParseOrThrow(source));
}

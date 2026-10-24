using SharpTS.Diagnostics;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class ComputedConstructorKeyOfTests
{
    [Theory]
    [InlineData("", "C", "public", false)]
    [InlineData("<T>", "C<number>", "public", false)]
    [InlineData("<T = number>", "C", "public", false)]
    [InlineData("", "C", "private", true)]
    [InlineData("<T>", "C<number>", "private", true)]
    [InlineData("<T = number>", "C", "private", true)]
    [InlineData("", "C", "protected", true)]
    [InlineData("<T>", "C<number>", "protected", true)]
    [InlineData("<T = number>", "C", "protected", true)]
    public void KeyOfUsesTheComputedMethodsAccessInsteadOfTheRealConstructorsAccess(
        string parameters, string type, string access, bool rejected)
    {
        string declaration = $$"""
            class C{{parameters}} {
                constructor(value: number) {}
                {{access}} ["constructor"]() {}
                visible() {}
            }
            """;
        Assert.Empty(Check(declaration + $"const ordinary: keyof {type} = 'visible';").Diagnostics);
        var result = Check(declaration + $"const key: keyof {type} = 'constructor';");
        if (rejected) Assert.Contains(result.Diagnostics, diagnostic => diagnostic.TsCode == "TS2322");
        else Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData("", "C")]
    [InlineData("<T>", "C<number>")]
    public void RealConstructionSignatureDoesNotCreateAnInstanceKey(string parameters, string type)
    {
        Assert.Contains(Check($$"""
            class C{{parameters}} { constructor(value: number) {} visible() {} }
            const key: keyof {{type}} = "constructor";
            """).Diagnostics, diagnostic => diagnostic.TsCode == "TS2322");
    }

    [Fact]
    public void PublicComputedMethodRemainsAKeyWithAPrivateRealConstructor()
    {
        Assert.Empty(Check("""
            class C { private constructor() {} public ["constructor"]() {} }
            const key: keyof C = "constructor";
            """).Diagnostics);
    }

    private static TypeCheckDiagnosticResult Check(string source) => new TypeChecker(strictNullChecks: true)
        .CheckWithRecovery(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
}

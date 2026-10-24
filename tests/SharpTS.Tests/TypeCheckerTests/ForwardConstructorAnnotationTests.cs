using SharpTS.Diagnostics;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public class ForwardConstructorAnnotationTests
{
    [Theory]
    [InlineData("class C { static test(other: typeof D) { return other.missing; } } class D extends C {}")]
    [InlineData("function read(other: typeof D) { return other.missing; } class D {}")]
    public void ForwardConstructorAnnotationRejectsMissingMembers(string source)
    {
        Assert.Equal("TS2339", Assert.Single(Check(source).Diagnostics).TsCode);
    }

    [Fact]
    public void ForwardConstructorAnnotationRetainsDeclaredMembers()
    {
        var result = Check("class C { static test(other: typeof D): string { return other.answer; } } class D extends C { static answer: number = 1; }");
        Assert.Equal("TS2322", Assert.Single(result.Diagnostics).TsCode);
    }

    [Fact]
    public void ActualAnyParameterStaysDynamic()
    {
        Assert.Empty(Check("class C { static test(other: any) { return other.missing; } } class D extends C {}").Diagnostics);
    }

    private static TypeCheckDiagnosticResult Check(string source) => new TypeChecker(strictNullChecks: true)
        .CheckWithRecovery(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
}

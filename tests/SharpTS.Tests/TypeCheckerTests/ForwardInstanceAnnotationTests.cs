using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public class ForwardInstanceAnnotationTests
{
    [Theory]
    [InlineData("declare const value: Later; value.answer; class Later { answer: number = 1; }")]
    [InlineData("function read(value: Later): number { return value.answer; } class Later { answer = 1; }")]
    [InlineData("function outer() { let value: Later; class Later { answer: number = 1; } }")]
    public void InstanceTypeIsVisibleBeforeClassValueInitializes(string source)
    {
        Assert.Empty(Check(source).Diagnostics);
    }

    [Fact]
    public void ForwardInstanceAnnotationPreservesMemberTypes()
    {
        var result = Check("declare const value: Later; const wrong: string = value.answer; class Later { answer: number = 1; }");
        Assert.Equal("TS2322", Assert.Single(result.Diagnostics).TsCode);
    }

    [Fact]
    public void PublishingTypeFacetDoesNotPermitEarlyConstruction()
    {
        Assert.Contains(Check("const value: Later = new Later(); class Later {}").Diagnostics,
            diagnostic => diagnostic.TsCode == "TS2449");
    }

    private static TypeCheckDiagnosticResult Check(string source) => new TypeChecker(strictNullChecks: true)
        .CheckWithRecovery(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
}

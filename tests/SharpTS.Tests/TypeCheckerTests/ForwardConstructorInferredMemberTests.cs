using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class ForwardConstructorInferredMemberTests
{
    [Theory]
    [InlineData("let saved: typeof Later; class Later { static value = 1 + 1; } saved = Later; const wrong: string = saved.value;")]
    [InlineData("declare const saved: typeof Later; class Later { static value = 1 + 1; } const wrong: string = saved.value;")]
    [InlineData("declare const holder: { ctor: typeof Later }; class Later { static value = 1 + 1; } const wrong: string = holder.ctor.value;")]
    [InlineData("type Constructor = typeof Later; function read(saved: Constructor): string { return saved.value; } class Later { static value = 1 + 1; }")]
    [InlineData("function read(saved: typeof Later): string { return saved.value; } class Later { static value = 1 + 1; }")]
    [InlineData("class Earlier { static read(saved: typeof Later): string { return saved.value; } } class Later { static value = 1 + 1; }")]
    [InlineData("function read(saved: typeof Later): string { return saved.value; } function compute(): number { return 2; } class Later { static value = compute(); }")]
    public void EarlyConstructorAnnotationsKeepInferredStaticMemberTypes(string source)
    {
        var result = new TypeChecker(strictNullChecks: true).CheckWithRecovery(Parse(source));
        Assert.Equal("TS2322", Assert.Single(result.Diagnostics).TsCode);
    }

    [Theory]
    [InlineData("function read(saved: typeof Later): number { return saved.value; } class Later { static value = 1 + 1; }")]
    [InlineData("function read(saved: typeof Later): string { return saved.value; } class Later { static value: any = 1 + 1; }")]
    [InlineData("let outer: string | number = 'ok'; function read(saved: typeof Later): number { return saved.value; } const text: string = outer; class Later { static value = (outer = 1); }")]
    public void EarlyStaticInferencePreservesValidTypesAndDeclarationFlow(string source)
        => Assert.Empty(new TypeChecker(strictNullChecks: true).CheckWithRecovery(Parse(source)).Diagnostics);

    [Fact]
    public void InvalidInitializerIsReportedOnlyAtItsDeclaration()
    {
        var result = new TypeChecker().CheckWithRecovery(Parse("function read(saved: typeof Later) { return saved.value; }\nclass Later { static value = missing; }"));
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal("TS2304", error.TsCode);
        Assert.Equal(2, error.Line);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RetainedConstructorAnnotationsFollowTheirOwnDeclarationAcrossCheckerCalls(bool recovery)
    {
        var checker = new TypeChecker(strictNullChecks: true);
        var declarations = Parse("declare const saved: typeof Later; class Later { static value = 1 + 1; }");
        if (recovery) Assert.Empty(checker.CheckWithRecovery(declarations).Diagnostics);
        else checker.Check(declarations);
        var result = checker.CheckWithRecovery(Parse("function shadow() { class Later { static value = 'other'; } } const wrong: string = saved.value;"));
        Assert.Equal("TS2322", Assert.Single(result.Diagnostics).TsCode);
    }

    private static List<Stmt> Parse(string source) => new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
}

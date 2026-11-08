using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;
namespace SharpTS.Tests.TypeCheckerTests;
public class ForwardGetterFieldDependencyTests
{
    [Theory]
    [InlineData("class C")]
    [InlineData("const C = class")]
    public void ArrowClosureResolvesLaterFieldThroughBracketAccess(string declaration)
    {
        var statements = TestHarness.ParseOrThrow($$"""
            {{declaration}} {
                read = () => this['value'];
                value = 1;
            }
            const right: number = new C().read();
            const wrong: string = new C().read();
            """);
        var diagnostic = Assert.Single(new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(statements).Diagnostics);
        Assert.Equal(6, diagnostic.Line);
        Assert.Equal("TS2322", diagnostic.TsCode);
    }

    [Theory]
    [InlineData("class C { static get callback() { return () => C.later; } static copied = C.callback; static later = 7; }")]
    [InlineData("const C = class Named { static get callback() { return () => Named.later; } static copied = Named.callback; static later = 7; };")]
    [InlineData("class C { static get callback() { return () => C['later']; } static copied = C.callback; static later = 7; }")]
    public void GetterClosure_ResolvesLaterFieldWithoutLosingResultType(string declaration)
    {
        foreach (var type in new[] { "number", "string" })
        {
            var source = declaration + " const result: " + type + " = C.copied();";
            var result = new TypeChecker(strictNullChecks: true).CheckWithRecovery(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
            if (type == "number") Assert.Empty(result.Diagnostics);
            else Assert.Contains(result.Diagnostics, d => d.TsCode == "TS2322");
        }
    }
    [Fact]
    public void NonCallableForwardGetter_RemainsRejected()
    {
        var source = "class C { static get callback() { return C.later; } static copied = C.callback; static later = 7; } C.copied();";
        Assert.Contains(new TypeChecker().CheckWithRecovery(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow()).Diagnostics, d => d.TsCode == "TS2349");
    }
}

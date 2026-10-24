using SharpTS.Diagnostics;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public class DeferredClassFieldFlowTests
{
    [Theory]
    [InlineData("class C", "")]
    [InlineData("const C = class", ";")]
    public void CallbackPropertyGuardDoesNotNarrowLaterInstanceField(string declaration, string ending)
    {
        string source = "function create(value: {prop: string | number}) { " + declaration + " { " +
            "read = () => { if (typeof value.prop === 'string') { const wrong: string = this.later; } }; " +
            "later = value.prop; }" + ending + " }";
        Assert.Equal("TS2322", Assert.Single(Check(source).Diagnostics).TsCode);
    }

    [Theory]
    [InlineData("class C", "")]
    [InlineData("const C = class", ";")]
    public void ResolvingFieldPreservesTheCallbacksOwnPropertyGuard(string declaration, string ending)
    {
        string source = "function create(value: {prop: string | number}) { " + declaration + " { " +
            "read = () => { if (typeof value.prop === 'string') { " +
            "const whole: string | number = this.later; const narrowed: string = value.prop; } }; " +
            "later = value.prop; }" + ending + " }";
        Assert.Empty(Check(source).Diagnostics);
    }

    [Theory]
    [InlineData("class C", "")]
    [InlineData("const C = class C", ";")]
    public void InstanceDependencyCanReferenceLaterDeclaredClass(string declaration, string ending)
    {
        string source = declaration + " { read = () => this.later; later = new Later(); }" + ending +
            " class Later {} const result: Later = new C().read();";
        Assert.Empty(Check(source).Diagnostics);
    }

    [Theory]
    [InlineData("class C", "")]
    [InlineData("const C = class", ";")]
    public void StaticDependencyRetainsOuterFlowNarrowing(string declaration, string ending)
    {
        string source = "function create(value: string | number) { if (typeof value === 'string') { " +
            declaration + " { static read = () => C.later; static later = value; }" + ending +
            " const result: string = C.read(); } }";
        Assert.Empty(Check(source).Diagnostics);
    }

    [Theory]
    [InlineData("class C", "")]
    [InlineData("const C = class", ";")]
    public void OrdinaryInstanceInitializerAlsoIgnoresOuterPropertyGuard(string declaration, string ending)
    {
        string source = "function create(value: {prop: string | number}) { if (typeof value.prop === 'string') { " +
            declaration + " { later = value.prop; }" + ending + " const wrong: string = new C().later; } }";
        Assert.Equal("TS2322", Assert.Single(Check(source).Diagnostics).TsCode);
    }

    [Fact]
    public void EagerStaticInitializerStillRejectsLaterDeclaredClass()
    {
        Assert.Contains(Check("class C { static later = new Later(); } class Later {}").Diagnostics,
            diagnostic => diagnostic.TsCode == "TS2449");
    }

    private static TypeCheckDiagnosticResult Check(string source) => new TypeChecker(strictNullChecks: true)
        .CheckWithRecovery(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
}

using SharpTS.Diagnostics;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public class InstanceFieldOuterVariableFlowTests
{
    [Theory]
    [InlineData("class C", "")]
    [InlineData("const C = class", ";")]
    public void MutableOuterInitializerNarrowingDoesNotBecomeTheInstanceFieldType(string declaration, string ending)
    {
        string source = "let value: string | number = 'ok'; " + declaration +
            " { later = value; }" + ending + " const wrong: string = new C().later;";
        Assert.Equal("TS2322", Assert.Single(Check(source).Diagnostics).TsCode);
    }

    [Fact]
    public void AnnotatedConstRetainsItsDeclaredTypeInInstanceFieldInference()
    {
        Assert.Equal("TS2322", Assert.Single(Check("const value: string | number = 'ok'; class C { later = value; } const wrong: string = new C().later;").Diagnostics).TsCode);
    }

    [Fact]
    public void OuterGuardDoesNotBecomeTheInstanceFieldType()
    {
        Assert.Equal("TS2322", Assert.Single(Check("""
            function create(value: string | number) {
                if (typeof value === 'string') {
                    class C { later = value; }
                    const wrong: string = new C().later;
                }
            }
            """).Diagnostics).TsCode);
    }

    [Theory]
    [InlineData("const value = 'ok'; class C { later = value; } const valid: string = new C().later;")]
    [InlineData("let value: string | number = 'outer'; class C { later = (() => { let value = 1; return value; })(); } const valid: number = new C().later;")]
    [InlineData("let value: string | number = 'outer'; class C { later = (() => { if (typeof value === 'string') { const valid: string = value; } return value; })(); }")]
    public void ConstAndInitializerLocalFlowRemainsAvailable(string source)
    {
        Assert.Empty(Check(source).Diagnostics);
    }

    private static TypeCheckDiagnosticResult Check(string source) => new TypeChecker(strictNullChecks: true)
        .CheckWithRecovery(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
}

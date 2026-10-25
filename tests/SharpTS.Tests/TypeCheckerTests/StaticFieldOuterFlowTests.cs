using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class StaticFieldOuterFlowTests
{
    [Theory]
    [InlineData("class C", "")]
    [InlineData("const C = class", ";")]
    public void StaticFieldUsesTheOuterVariablesDeclaredType(string declaration, string ending)
    {
        string source = "let value: string | number = 'ok'; " + declaration +
            " { static later = value; }" + ending + " const wrong: string = C.later;";
        Assert.Equal("TS2322", Assert.Single(Check(source)).TsCode);
    }

    [Theory]
    [InlineData("const value: string | number = 'ok'; class C { static later = value; } const wrong: string = C.later;", "TS2322")]
    [InlineData("let value: string | number = 'ok'; class C { static later: string = value; }", "TS2322")]
    [InlineData("let value: string | number = 'ok'; class C { static later = value.toUpperCase(); }", "TS2339")]
    [InlineData("let value: string | number = 'ok'; class C { static first = (value = 1); static second = value; } const wrong: number = C.second;", "TS2322")]
    [InlineData("let value: string | number = 'ok'; class C { static first = (value = 1); } const wrong: number = value;", "TS2322")]
    [InlineData("class C { static later = new Later(); } class Later {}", "TS2449")]
    [InlineData("function read(saved: typeof Later): string { return saved.value; } class Later { static value = 1 + 1; }", "TS2322")]
    public void StaticFieldFlowAndValueOrderRemainChecked(string source, string code)
        => Assert.Equal(code, Assert.Single(Check(source)).TsCode);

    [Theory]
    [InlineData("const value = 'ok'; class C { static later = value; } const good: string = C.later;")]
    [InlineData("let value: string | number = 'ok'; class C { static later = (() => { if (typeof value === 'string') return value; return ''; })(); } const good: string = C.later;")]
    [InlineData("let value: string | number = 'ok'; class C { static later = (() => { value = 1; const good: number = value; return value; })(); } const good: number = C.later;")]
    [InlineData("let value: string | number = 'ok'; class C { static { value = 1; const good: number = value; } }")]
    public void InitializerLocalGuardsAndStaticBlocksKeepTheirOwnFlow(string source)
        => Assert.Empty(Check(source));

    private static IReadOnlyList<SharpTS.Diagnostics.Diagnostic> Check(string source)
        => new TypeChecker(strictNullChecks: true)
            .CheckWithRecovery(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow()).Diagnostics;
}

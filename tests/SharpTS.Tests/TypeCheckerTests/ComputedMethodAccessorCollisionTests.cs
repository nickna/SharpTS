using SharpTS.Diagnostics;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class ComputedMethodAccessorCollisionTests
{
    private static TypeCheckDiagnosticResult Check(string source)
        => new TypeChecker(strictNullChecks: true).CheckWithRecovery(
            new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());

    public static IEnumerable<object[]> CollisionCases()
    {
        string[] bodies = [
            "['read']() { return 1; } get ['read']() { return 2; }",
            "['read']() { return 1; } get [('read')]() { return 2; }",
            "get ['read']() { return 2; } ['read']() { return 1; }",
            "read() { return 1; } get ['read']() { return 2; }",
            "['read']() { return 1; } get read() { return 2; }",
            "['read']() {} set ['read'](value: number) {}",
            "['read']() {} set [('read')](value: number) {}",
            "static ['read']() {} static get ['read']() { return 2; }",
            "[0]() {} get [0]() { return 2; }",
            "['']() {} get ['']() { return 2; }",
            "constructor() {} ['constructor']() {} get ['constructor']() { return 2; }",
            "get ['read']() { return 1; } get ['read']() { return 2; }",
            "set ['read'](value: number) {} set ['read'](value: number) {}"
        ];
        foreach (string body in bodies)
        {
            yield return ["class C { " + body + " }"];
            yield return ["const C = class { " + body + " };"];
        }
    }

    [Theory, MemberData(nameof(CollisionCases))]
    public void KnownMethodAndAccessorCollisionsAreRejected(string source)
        => Assert.Contains(Check(source).Diagnostics, diagnostic => diagnostic.TsCode == "TS2300");

    [Theory]
    [InlineData("class C", "")]
    [InlineData("const C = class", ";")]
    public void IndependentFacetsAndAccessorPairsRemainValid(string declaration, string ending)
        => Assert.Empty(Check(declaration + """
             {
                constructor() {}
                get ['constructor']() { return 1; }
                ['instanceMethod']() {}
                static get ['instanceMethod']() { return 2; }
                static ['staticMethod']() {}
                get ['staticMethod']() { return 3; }
                get ['paired']() { return 4; }
                set ['paired'](value: number) {}
             }
            """ + ending).Diagnostics);

    [Theory]
    [InlineData("class C", "")]
    [InlineData("const C = class", ";")]
    public void WellKnownSymbolCollisionKeepsItsDiagnosticDisplay(string declaration, string ending)
        => Assert.Contains(Check(declaration + """
             {
                [Symbol.iterator]() { return 1; }
                get [Symbol.iterator]() { return 2; }
             }
            """ + ending).Diagnostics,
            diagnostic => diagnostic.TsCode == "TS2300" && diagnostic.Message.Contains("[Symbol.iterator]"));
}

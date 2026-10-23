using SharpTS.Parsing;
using SharpTS.Tests.SharedTests;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class DestructuredParameterDefaultTypeCheckTests
{
    [Fact]
    public void HoistedCallInfersLaterPatternDefaultFromEarlierBinding()
    {
        const string source = """
            run([1]);
            function run([value]: number[], { next = value } = {}) {
                console.log(value, next);
            }
            """;
        var document = new SourceDocument("hoisted-inferred-pattern.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens())
            .WithSourceDocument(document).ParseOrThrow();

        new TypeChecker().Check(statements, document);
    }

    [Theory, MemberData(nameof(DestructuredParameterDefaultScopeTests.Cases), MemberType = typeof(DestructuredParameterDefaultScopeTests))]
    public void EarlierPatternBindingsAreAvailableWhileCheckingLaterDefaults(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.NotEmpty(expected);
        var document = new SourceDocument(name + ".ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens())
            .WithSourceDocument(document).ParseOrThrow();

        new TypeChecker().Check(statements, document);
    }

    public static IEnumerable<object[]> RejectedCases()
    {
        yield return ["hoisted-inferred-pattern-return-type", """
            const wrong: string = run([1]);
            function run([value]: number[], { next = value } = {}) { return next; }
            """, "TS2322", 1];
        yield return ["array-binding-default-type", """
            function run([value]: number[], next: string = value) {}
            """, "TS2322", 1];
        yield return ["object-alias-default-type", """
            function run({ original: renamed }: { original: number }, next: string = renamed) {}
            """, "TS2322", 1];
        yield return ["body-use-retains-pattern-type", """
            function run([value]: number[], next = value) {
                const wrong: string = value;
            }
            """, "TS2322", 2];
        yield return ["later-pattern-binding-remains-unavailable", """
            function run(first = later, [later]: number[] = [1]) {}
            """, "TS2304", 1];
    }

    [Theory, MemberData(nameof(RejectedCases))]
    public void PatternPublicationKeepsTypesAndParameterOrder(
        string name, string source, string code, int line)
    {
        Assert.NotEmpty(name);
        var document = new SourceDocument(name + ".ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens())
            .WithSourceDocument(document).ParseOrThrow();
        var result = new TypeChecker().CheckWithRecovery(statements, document);
        var diagnostic = Assert.Single(result.Diagnostics);

        Assert.Equal(code, diagnostic.TsCode);
        Assert.Equal(line, diagnostic.Line);
    }
}

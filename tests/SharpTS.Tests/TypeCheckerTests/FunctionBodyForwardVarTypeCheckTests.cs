using SharpTS.Parsing;
using SharpTS.Tests.SharedTests;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class FunctionBodyForwardVarTypeCheckTests
{
    [Theory, MemberData(nameof(FunctionBodyForwardVarScopeTests.Cases), MemberType = typeof(FunctionBodyForwardVarScopeTests))]
    public void OwnLaterVarBindingsAreAvailableToDeferredBodyChecking(
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
        yield return ["early-typed-callable-argument", """
            function run() {
                const read = () => fn("bad");
                var fn: (input: number) => number = (input: number) => input;
            }
            """, "TS2345", 2];
        yield return ["early-callable-typeof-sibling-signature", """
            function run() {
                const read = () => fn("bad");
                var fn: typeof target = (n: number) => n;
                function target(n: number): number { return n; }
            }
            run();
            """, "TS2345", 2];
        yield return ["annotated-later-use", """
            function run() {
                const read = () => value;
                var value: number = 1;
                const wrong: string = value;
            }
            """, "TS2322", 4];
        yield return ["inferred-later-use", """
            function run() {
                const read = () => value;
                var value = 1;
                const wrong: string = value;
            }
            """, "TS2322", 4];
        yield return ["initializer-keeps-annotation", """
            function run() {
                const read = () => value;
                var value: number = "wrong";
            }
            """, "TS2322", 3];
        yield return ["annotated-redeclaration", """
            function run() {
                const read = () => value;
                var value: number = 1;
                var value: string;
            }
            """, "TS2403", 4];
        yield return ["inferred-redeclaration", """
            function run() {
                const read = () => value;
                var value = 1;
                var value: string;
            }
            """, "TS2403", 4];
    }

    [Theory, MemberData(nameof(RejectedCases))]
    public void ForwardRegistrationRetainsDeclarationTypesAndDiagnostics(
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

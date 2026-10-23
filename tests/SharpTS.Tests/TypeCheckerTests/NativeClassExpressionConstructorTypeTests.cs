using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class NativeClassExpressionConstructorTypeTests
{
    public static IEnumerable<object[]> RejectedCases()
    {
        yield return ["concrete-native-array-iterator-result-is-not-string", """
            const Values = class extends Array<number> {};
            const values = new Values(1, 2);
            for (const value of values) { const wrong: string = value; }
            """, "TS2322", 3];
        yield return ["explicit-array-expression-constructor-arity", """
            const Values = class extends Array<number> { constructor(value: number) { super(value); } };
            new Values();
            """, "TS2554", 2];
        yield return ["explicit-array-expression-constructor-type", """
            const Values = class extends Array<number> { constructor(value: number) { super(value); } };
            new Values("wrong");
            """, "TS2345", 2];
        yield return ["local-array-shadow-retains-typed-constructor", """
            function check() {
                class Array { constructor(value: string) {} }
                const Values = class extends Array {};
                new Values(1);
            }
            """, "TS2345", 4];
        yield return ["local-promise-shadow-retains-typed-constructor", """
            function check() {
                class Promise { constructor(value: string) {} }
                const Pending = class extends Promise {};
                new Pending(1);
            }
            """, "TS2345", 4];
    }

    [Theory, MemberData(nameof(RejectedCases))]
    public void NativeFallbackDoesNotReplaceExplicitOrShadowedConstructorTypes(
        string name, string source, string code, int line)
    {
        var document = new SourceDocument(name + ".ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).ParseOrThrow();
        var result = new TypeChecker().CheckWithRecovery(statements, document);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.TsCode == code && diagnostic.Line == line);
    }
}

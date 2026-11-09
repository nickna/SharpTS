using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public class ClassBodyInferenceTests
{
    [Theory]
    [InlineData("class C", "leaf() { return 1; } read() { return this.leaf(); }", "new C().read()")]
    [InlineData("class C", "read() { return this.middle(); } middle() { return this.leaf(); } leaf() { return 1; }", "new C().read()")]
    [InlineData("const C = class", "read() { return this.#leaf(); } #leaf() { return 1; }", "new C().read()")]
    [InlineData("class C", "static read() { return C.leaf(); } static leaf() { return 1; }", "C.read()")]
    [InlineData("class C", "read() { return this.leaf(1); } leaf<T>(value: T) { return value; }", "new C().read()")]
    public void InferredReturnTypesFlowThroughSiblingCalls(string declaration, string methods, string call)
    {
        var statements = new Parser(new Lexer($$"""
            {{declaration}} { {{methods}} }
            const correct: number = {{call}};
            const wrong: string = {{call}};
            """).ScanTokens()).ParseOrThrow();

        var diagnostic = Assert.Single(new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(statements).Diagnostics);
        Assert.Equal("TS2322", diagnostic.TsCode);
        Assert.Equal(3, diagnostic.Line);
    }

    [Theory]
    [InlineData("value.method()")]
    [InlineData("value.property")]
    public void MethodAndGetterInference_AreBothPublished(string expression)
    {
        // Both stages must run, even when the method stage already requests a
        // rebuild. A runtime-only assertion would miss an <inferred> placeholder.
        var source = """
            class Value {
                method() { return 1; }
                get property() { return 2; }
            }
            const value = new Value();
            """ + $"\nconst invalid: string = {expression};";

        var parsed = new Parser(new Lexer(source).ScanTokens()).Parse();
        Assert.True(parsed.IsSuccess);
        var result = new TypeChecker().CheckWithRecovery(parsed.Statements);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("TS2322", diagnostic.TsCode);
        Assert.Equal(6, diagnostic.Line);
    }

    [Fact]
    public void InferredClassExpressionGetter_RejectsTheIssue2205StringAssignment()
    {
        const string source = """
            const C = class { get value() { return 7; } };
            const value: string = new C().value;
            console.log(value);
            """;

        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var result = new TypeChecker(strictNullChecks: true).CheckWithRecovery(statements);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("TS2322", diagnostic.TsCode);
        Assert.Equal(2, diagnostic.Line);
    }

    public static IEnumerable<object[]> ClassExpressionGetterCases()
    {
        yield return ["inferred-number", """
            const C = class { get value() { return 7; } };
            const value: number = new C().value;
            console.log(value);
            """];
        yield return ["annotated-number", """
            const C = class { get value(): number { return 7; } };
            const value: number = new C().value;
            console.log(value);
            """];
        yield return ["explicit-any", """
            const C = class { get value(): any { return 7; } };
            const value: string = new C().value;
            console.log(value);
            """];
    }

    [Theory, MemberData(nameof(ClassExpressionGetterCases))]
    public void ClassExpressionGetter_PreservesInferenceAnnotationsAndExplicitAny(string name, string source)
    {
        Assert.NotEmpty(name);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        Assert.Empty(new TypeChecker(strictNullChecks: true).CheckWithRecovery(statements).Diagnostics);
    }
}

using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class ConstrainedReturnContextInferenceTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var (source, codes) in new (string, string[])[]
        {
            ("declare function f<T extends number=number>():T;const value:string=f();", ["TS2322"]),
            ("declare function f<T extends number>():T;const value:string=f();", ["TS2322"]),
            ("declare function f<T extends number=number,U extends T=T>():U;const value:string=f();", ["TS2322"]),
            ("declare function f<T extends {value:number}={value:1}>():T;const value:string=f();", ["TS2322"]),
            ("declare function f<T extends string|number=number>():T;const value:string=f();", []),
            ("declare function f<T=number>():T;const value:string=f();", []),
            ("declare function f<T extends number=1>():T;const value:string|number=f();", []),
            ("declare function f<T extends number>(value:T):T;f('wrong');", ["TS2345"]),
            ("declare function f<T extends number=number>():T;f<string>();", ["TS2344"]),
            ("declare function same<T>(first:T,second:T):T;const value:number|string=same(1,'wrong');", ["TS2345"]),
        })
            foreach (bool modules in new[] { false, true }) yield return [source, codes, modules];
    }

    [Theory, MemberData(nameof(Cases))]
    public void ContextCandidatesRespectConstraintsWithoutHidingInvalidArgumentCandidates(string source, string[] codes, bool modules)
    {
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        if (!modules)
            Assert.Equal(codes, checker.CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics.Select(diagnostic => diagnostic.TsCode));
        else
        {
            string path = Path.Combine(Path.GetTempPath(), "constrained-return-context-" + Guid.NewGuid().ToString("N"), "main.ts");
            var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source }, new TypeScriptProgramOptions { NoLib = true });
            var entry = resolver.LoadModule(path);
            checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
            Assert.Equal(codes, checker.GetDiagnostics().Select(diagnostic => diagnostic.TsCode));
        }
    }

    [Theory]
    [InlineData("const value:string|number=f();", "number")]
    [InlineData("const value=f();", "1")]
    public void EditorResultsDistinguishConstraintFallbackFromDefaultInference(string call, string returnType)
    {
        string source = "declare function f<T extends number=1>():T;" + call;
        var document = new SourceDocument("return-context-result.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).WithEditorSyntax().ParseOrThrow();
        var checker = new TypeChecker(TypeCheckerOptions.Strict).WithEditorMetadata();
        Assert.Empty(checker.CheckWithRecovery(statements, document).Diagnostics);
        var invocation = Assert.Single(document.EditorSyntax!.Invocations);
        var fact = Assert.IsType<EditorInvocationFact>(checker.EditorFacts.Freeze().GetInvocation(document, invocation.Owner));
        Assert.Equal(EditorInvocationStatus.Selected, fact.Status);
        Assert.True(Assert.Single(fact.Candidates).Instantiated!.IsAvailable);
        Assert.Equal(returnType, fact.SelectedSignature!.ReturnType.Text);
    }
}

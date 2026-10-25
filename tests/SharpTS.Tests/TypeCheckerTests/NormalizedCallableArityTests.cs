using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class NormalizedCallableArityTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var (source, codes) in new (string, string[])[]
        {
            ("interface Callable{(value:any):void;}declare const f:Callable;f();", ["TS2554"]),
            ("interface Callable{(value:any):void;}declare const f:Callable;f(1,2);", ["TS2554"]),
            ("interface Callable{():void;}declare const f:Callable;f(1);", ["TS2554"]),
            ("declare const f:{(value:any):void;tag:string};f();", ["TS2554"]),
            ("declare const f:{(value:any):void;tag:string};f(1,2);", ["TS2554"]),
            ("declare const f:{():void;tag:string};f(1);", ["TS2554"]),
            ("interface Callable{<T>(value:T):T;}declare const f:Callable;f();", ["TS2554"]),
            ("interface Callable{<T>(value:T):T;}declare const f:Callable;f(1,2);", ["TS2554"]),
            ("interface Callable{<T>(value:any):T;}declare const f:Callable;f<number>();", ["TS2554"]),
            ("interface Callable{<T>():T;}declare const f:Callable;f<number>(1);", ["TS2554"]),
            ("declare const f:{<T>(value:T):T;tag:string};f();", ["TS2554"]),
            ("declare const f:{<T>(value:T):T;tag:string};f(1,2);", ["TS2554"]),
            ("declare const f:{<T>():T;tag:string};f<number>(1);", ["TS2554"]),
            ("interface Callable<A>{<T extends A>(value:T):T;}declare const f:Callable<number>;f();", ["TS2554"]),
            ("interface Base{(value:any):void;}interface Callable extends Base{}declare const f:Callable&{tag:string};f();", ["TS2554"]),
            ("declare const f:{<T>(value:T):T;tag:string};f(...[]);", ["TS2554"]),
            ("declare const f:{<T>(value:T):T;tag:string};f(...[1,2]);", ["TS2554"]),
            ("interface Callable{(value:any):void;}declare const f:Callable;f(1);", []),
            ("interface Callable{():void;}declare const f:Callable;f();", []),
            ("declare const f:{(value?:any):void;tag:string};f();f(1);", []),
            ("declare const f:{(value:any,...rest:any[]):void;tag:string};f(1);f(1,2,3);", []),
            ("interface Callable{<T>(value?:T):T;}declare const f:Callable;f();f(1);", []),
            ("interface Callable{<T>(...values:T[]):T;}declare const f:Callable;f();f(1,2,3);", []),
            ("interface Callable{<T>(value:T,...rest:T[]):T;}declare const f:Callable;f(1);f(1,2,3);", []),
            ("declare const f:{<T>(value:T,...rest:T[]):T;tag:string};f(1);f(1,2,3);", []),
            ("declare const f:{<T>():T;tag:string};const value:number=f<number>();", []),
            ("declare const f:{<T>(value:T):T;tag:string};const value:number=f(...[1]);", []),
            ("declare const f:{<T>(value:T,...rest:T[]):T;tag:string};declare const values:number[];const value:number=f(1,...values);", []),
        })
            foreach (bool modules in new[] { false, true }) yield return [source, codes, modules];
    }

    [Theory, MemberData(nameof(Cases))]
    public void DeclarationOwnedAdaptersRetainRequiredOptionalAndRestArity(string source, string[] codes, bool modules)
        => Assert.Equal(codes, Check(source, modules).Select(diagnostic => diagnostic.TsCode));

    [Fact]
    public void OrdinaryUntypedFunctionsKeepTheirExistingJavaScriptCallPolicy()
        => Assert.Empty(Check("function anyValue(value:any){}function zero(){}anyValue();anyValue(1,2);zero(1,2);", false));

    [Fact]
    public void ArityFailureKeepsTheOriginalCallableDeclarationCandidate()
    {
        const string source = "interface Callable{(value:any):void;}declare const f:Callable;f(1);f();";
        var document = new SourceDocument("normalized-callable-arity.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).WithEditorSyntax().ParseOrThrow();
        var checker = new TypeChecker(TypeCheckerOptions.Strict).WithEditorMetadata();
        Assert.Equal("TS2554", Assert.Single(checker.CheckWithRecovery(statements, document).Diagnostics).TsCode);
        var facts = checker.EditorFacts.Freeze();
        var calls = document.EditorSyntax!.Invocations.Select(invocation =>
            Assert.IsType<EditorInvocationFact>(facts.GetInvocation(document, invocation.Owner))).ToArray();
        Assert.Equal(2, calls.Length);
        Assert.Equal(EditorInvocationStatus.Selected, calls[0].Status);
        Assert.Equal(EditorInvocationStatus.CandidatesOnly, calls[1].Status);
        var first = Assert.Single(calls[0].Candidates);
        var rejected = Assert.Single(calls[1].Candidates);
        Assert.Equal(first.OriginalSignatureId, rejected.OriginalSignatureId);
        if (first.Origin is { } sourceOrigin) Assert.Same(sourceOrigin.Owner, rejected.Origin!.Owner);
        else Assert.Null(rejected.Origin);
        Assert.True(rejected.Declared.IsAvailable);
        Assert.Null(calls[1].SelectedOrdinal);
    }

    private static IReadOnlyList<SharpTS.Diagnostics.Diagnostic> Check(string source, bool modules)
    {
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        if (!modules) return checker.CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        string path = Path.Combine(Path.GetTempPath(), "normalized-callable-arity-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source }, new TypeScriptProgramOptions { NoLib = true });
        var entry = resolver.LoadModule(path);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        return checker.GetDiagnostics();
    }
}

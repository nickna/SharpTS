using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class ImplementedOverloadScopeTests
{
    private const string RuntimeSource = "function choose<T extends number>(value:T):T;function choose(value:string):string;function choose(value:any){return value;}console.log(choose(1));console.log(choose('ok'));";

    private const string PathDeclarations = """
        function path<T,K1 extends keyof T>(obj:T,key1:K1):T[K1];
        function path<T,K1 extends keyof T,K2 extends keyof T[K1]>(obj:T,key1:K1,key2:K2):T[K1][K2];
        function path<T,K1 extends keyof T,K2 extends keyof T[K1],K3 extends keyof T[K1][K2]>(obj:T,key1:K1,key2:K2,key3:K3):T[K1][K2][K3];
        function path(obj:any,...keys:(string|number)[]):any;
        function path(obj:any,...keys:(string|number)[]):any{return obj;}
        type Thing={a:{x:number,y:string},b:boolean};
        """;

    public static IEnumerable<object[]> Cases()
    {
        foreach (var (source, codes) in new (string, string[])[]
        {
            (PathDeclarations + "function sample(thing:Thing){let x1=path(thing,'a');let x2=path(thing,'a','y');let x3=path(thing,'b');let x4=path(thing,...['a','x']);}", []),
            ("function choose<T extends number>(value:T):T;function choose(value:string):string;function choose(value:any):any{return value;}const n:number=choose(1);const s:string=choose('ok');", []),
            ("function choose<T>(value:T):T;function choose<T,U>(first:T,second:U):U;function choose(...values:any[]):any{return values[0];}const n:number=choose(1);const s:string=choose(1,'ok');", []),
            ("function choose<T extends number>(value:T):T;function choose<T extends string>(value:T):T;function choose(value:any):any{return value;}const n:number=choose(1);const s:string=choose('ok');", []),
            ("function choose<T extends number>(value:T):T;function choose(value:string):string;function choose(value:any){return value;}const n:number=choose(1);const s:string=choose('ok');", []),
            ("function scope(){function choose<T extends number>(value:T):T;function choose(value:string):string;function choose(value:any):any{return value;}const n:number=choose(1);const s:string=choose('ok');}", []),
            ("function choose<T extends number>(value:T):T;function choose(value:string):string;function choose(value:any){return value;}choose(true);", ["TS2769"]),
            ("function nested<T,K extends keyof T>(obj:T,key1:K,key2:keyof T[K]):T[K][keyof T[K]];function nested<T,K1 extends keyof T,K2 extends keyof T[K1]>(obj:T,key1:K1,key2:K2):T[K1][K2];function nested(obj:any,...keys:any[]):any{return obj;}declare const thing:{a:{x:number,y:string},b:boolean};nested(thing,'a','missing');", ["TS2769"]),
        })
            foreach (bool modules in new[] { false, true }) yield return [source, codes, modules];
    }

    [Theory, MemberData(nameof(Cases))]
    public void EachImplementedOverloadKeepsItsOwnGenericScopeAndPublicArity(string source, string[] codes, bool modules)
        => Assert.Equal(codes, Check(source, modules).Select(diagnostic => diagnostic.TsCode));

    [Fact]
    public void MixedGroupsKeepDeclaredEditorOwnersAndAnImplementationOnlyCompilerSignature()
    {
        const string source = "function choose<T extends number>(value:T):T;function choose(value:string):string;function choose(value:any){return value;}choose(1);choose('ok');";
        var document = new SourceDocument("implemented-overload-scopes.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).WithEditorSyntax().ParseOrThrow();
        var checker = new TypeChecker(TypeCheckerOptions.Strict).WithEditorMetadata();
        var result = checker.CheckWithRecovery(statements, document);
        Assert.Empty(result.Diagnostics);
        var implementation = Assert.IsType<Stmt.Function>(statements[2]);
        var compilerSignature = Assert.IsType<TypeInfo.Function>(result.TypeMap.GetFunctionType("choose"));
        Assert.IsType<TypeInfo.Any>(Assert.Single(compilerSignature.ParamTypes));
        var facts = checker.EditorFacts.Freeze();
        Assert.Equal(2, document.EditorSyntax!.Invocations.Count);
        var calls = document.EditorSyntax.Invocations.Select(invocation =>
            Assert.IsType<EditorInvocationFact>(facts.GetInvocation(document, invocation.Owner))).ToArray();
        Assert.Equal(0, calls[0].SelectedOrdinal);
        Assert.Equal(1, calls[1].SelectedOrdinal);
        Assert.All(calls, call =>
        {
            Assert.Equal(EditorInvocationStatus.Selected, call.Status);
            Assert.Equal(2, call.Candidates.Count);
            Assert.Same(statements[0], call.Candidates[0].Origin!.Owner);
            Assert.Same(statements[1], call.Candidates[1].Origin!.Owner);
            Assert.DoesNotContain(call.Candidates, candidate => ReferenceEquals(candidate.Origin?.Owner, implementation));
            Assert.All(call.Candidates, candidate => Assert.True(candidate.Declared.IsAvailable));
        });
        Assert.Equal(calls[0].Candidates[0].OriginalSignatureId, calls[1].Candidates[0].OriginalSignatureId);
        Assert.Equal(calls[0].Candidates[1].OriginalSignatureId, calls[1].Candidates[1].OriginalSignatureId);
        Assert.IsType<TypeInfo.OverloadSet>(result.TypeMap.Get(Assert.IsType<Expr.Call>(document.EditorSyntax.Invocations[0].Owner).Callee));
    }

    [Theory, ModeData]
    public void MixedImplementedCallsKeepRuntimeResults(ExecutionMode mode)
        => Assert.Equal("1\nok\n", TestHarness.Run(RuntimeSource, mode));

    [Fact]
    public void MixedImplementedCallsProduceValidCompilerIl()
        => Assert.Empty(TestHarness.CompileAndVerifyOnly(RuntimeSource));

    [Fact]
    public void ReusedCheckerKeepsNewDeclarationScopesAndOwnersAfterAnAmbientEntry()
    {
        var checker = new TypeChecker(TypeCheckerOptions.Strict).WithEditorMetadata();
        Assert.Empty(checker.CheckWithRecovery(TestHarness.ParseOrThrow(
            "declare function choose<T extends number>(value:T):T;declare function choose(value:string):string;")).Diagnostics);
        const string source = "function choose<T extends boolean>(value:T):T;function choose(value:string):string;function choose(value:any):any{return value;}choose(true);choose('ok');choose(1);";
        var document = new SourceDocument("reused-overload-scopes.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).WithEditorSyntax().ParseOrThrow();
        Assert.Equal(["TS2769"], checker.CheckWithRecovery(statements, document).Diagnostics.Select(diagnostic => diagnostic.TsCode));
        var facts = checker.EditorFacts.Freeze();
        var firstCall = Assert.IsType<EditorInvocationFact>(facts.GetInvocation(document, document.EditorSyntax!.Invocations[0].Owner));
        Assert.Equal(2, firstCall.Candidates.Count);
        Assert.Same(statements[0], firstCall.Candidates[0].Origin!.Owner);
        Assert.Same(statements[1], firstCall.Candidates[1].Origin!.Owner);
    }

    private static IReadOnlyList<SharpTS.Diagnostics.Diagnostic> Check(string source, bool modules)
    {
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        if (!modules) return checker.CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        string path = Path.Combine(Path.GetTempPath(), "implemented-overload-scopes-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source }, new TypeScriptProgramOptions { NoLib = true });
        var entry = resolver.LoadModule(path);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        return checker.GetDiagnostics();
    }
}

using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class PureCallableArityTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var (source, codes) in new (string, string[])[]
        {
            ("declare const f:{(value:any):number};f();", ["TS2554"]),
            ("declare const f:{():number};f(1);", ["TS2554"]),
            ("declare const f:{<T>(value:T):T};f(1,2);", ["TS2554"]),
            ("declare const f:{(value:any):number};f(1,2);", ["TS2554"]),
            ("declare const f:{<T>(value:T):T};f();", ["TS2554"]),
            ("type F={(value:any):number};declare const f:F;f();", ["TS2554"]),
            ("type F<U>={(value:U):U};declare const f:F<any>;f();", ["TS2554"]),
            ("type F<U>={<T extends U>(value:T):T};declare const f:F<number>;f(1,2);", ["TS2554"]),
            ("declare const box:{f:{(value:any):number}};box.f();", ["TS2554"]),
            ("declare function make():{(value:any):number};make()();", ["TS2554"]),
            ("const f:{(value:any):number}=()=>1;f();", ["TS2554"]),
            ("const f:{(value:any):void}=function(value:any){};f();", ["TS2554"]),
            ("declare const f:{<T extends number=number>(value?:T):T};f(1,2);", ["TS2554"]),
            ("declare const f:{<T extends number>(value:T):T};f<string>('wrong');", ["TS2344"]),
            ("declare const f:{(callback:(value:number)=>number):number};f((value:string)=>value.length);", ["TS2345"]),
            ("declare const f:{(value:any):number};f(1);", []),
            ("declare const f:{():number};f();", []),
            ("declare const f:{(value?:any):number};f();f(1);", []),
            ("declare const f:{(value:any,...rest:any[]):number};f(1);f(1,2,3);", []),
            ("declare const f:{<T>(value?:T):T};f();f(1);", []),
            ("declare const f:{<T>(...values:T[]):T};f();f(1,2,3);", []),
            ("declare const f:{<T extends number=number>(value?:T):T};const n:number=f();", []),
            ("declare const f:{<T>(...values:[T,T]):T};const n:number=f(1,2);", []),
            ("declare const f:{(callback:(value:number)=>number):number};const n:number=f(value=>value+1);", []),
            ("type F<U>={(value:U):U};declare const f:F<number>;const n:number=f(1);", []),
            ("declare const f:{<T>(value:T,...rest:T[]):T};declare const values:number[];const n:number=f(1,...values,2);", []),
            ("declare const f:{(value:any):number};f(...[1]);", []),
        })
            foreach (bool modules in new[] { false, true }) yield return [source, codes, modules];
    }

    [Theory, MemberData(nameof(Cases))]
    public void WrittenObjectProtocolsKeepRequiredOptionalAndRestArity(string source, string[] codes, bool modules)
        => Assert.Equal(codes, Check(source, modules).Select(diagnostic => diagnostic.TsCode));

    [Fact]
    public void PureProtocolsKeepTheirFunctionRepresentationAndOriginalEditorCandidate()
    {
        const string source = "declare const f:{<T>(value:T):T};f(1);f(1,2);";
        var document = new SourceDocument("pure-callable-arity.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).WithEditorSyntax().ParseOrThrow();
        var checker = new TypeChecker(TypeCheckerOptions.Strict).WithEditorMetadata();
        var result = checker.CheckWithRecovery(statements, document);
        Assert.Equal("TS2554", Assert.Single(result.Diagnostics).TsCode);
        var facts = checker.EditorFacts.Freeze();
        var calls = document.EditorSyntax!.Invocations.Select(invocation =>
            Assert.IsType<EditorInvocationFact>(facts.GetInvocation(document, invocation.Owner))).ToArray();
        Assert.Equal(2, calls.Length);
        Assert.Equal(EditorInvocationStatus.Selected, calls[0].Status);
        Assert.Equal(EditorInvocationStatus.CandidatesOnly, calls[1].Status);
        Assert.Equal(Assert.Single(calls[0].Candidates).OriginalSignatureId,
            Assert.Single(calls[1].Candidates).OriginalSignatureId);
        Assert.Null(calls[1].SelectedOrdinal);
        var callee = Assert.IsType<Expr.Call>(document.EditorSyntax.Invocations[0].Owner).Callee;
        var function = Assert.IsType<TypeInfo.GenericFunction>(result.TypeMap.Get(callee));
        Assert.NotNull(function.WrittenCallSignature);
    }

    [Fact]
    public void OrdinaryJavaScriptFunctionCallsKeepTheirExistingPermissivePolicy()
        => Assert.Empty(Check("function loose(value:any){}function zero(){}loose();loose(1,2);zero(1,2);", false));

    private static IReadOnlyList<SharpTS.Diagnostics.Diagnostic> Check(string source, bool modules)
    {
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        if (!modules) return checker.CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        string path = Path.Combine(Path.GetTempPath(), "pure-callable-arity-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source }, new TypeScriptProgramOptions { NoLib = true });
        var entry = resolver.LoadModule(path);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        return checker.GetDiagnostics();
    }
}

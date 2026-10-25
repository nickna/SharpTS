using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class CallableIntersectionSignatureTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var (source, codes) in new (string, string[])[]
        {
            ("declare const f:{(value:string):string;tag:string}&{other:number};const value:string=f('ok');const tag:string=f.tag;const other:number=f.other;", []),
            ("interface Callable{(value:string):string;}declare const f:Callable&{tag:string};const value:string=f('ok');", []),
            ("interface Base{(value:string):string;readonly tag:string;}interface Callable extends Base{}declare const f:Callable&{other:number};const value:string=f('ok');const tag:string=f.tag;", []),
            ("interface Base{(value:string):string;}interface Middle extends Base{}interface Callable extends Middle{}declare const f:Callable;const value:string=f('ok');", []),
            ("interface Base<T>{(value:T):T;}interface Callable<A> extends Base<A>{}declare const f:Callable<number>&{tag:string};const value:number=f(1);", []),
            ("interface Base<A>{<T extends A=A>(value:T):T;}interface Callable<B> extends Base<B>{}declare const f:Callable<number>&{tag:string};const value:number=f(1);", []),
            ("interface Base<A>{<T extends A=A>():T;}interface Callable<B> extends Base<B>{}declare const f:Callable<number>&{tag:string};const value:number=f();", []),
            ("interface Base<T>{<T>(value:T):T;}interface Callable<U> extends Base<U>{}declare const f:Callable<number>&{tag:string};const text:string=f<string>('ok');", []),
            ("interface Base<T>{<T extends string=string>(value:T):T;}interface Callable<U> extends Base<U>{}declare const f:Callable<number>&{tag:string};const text:string=f<string>('ok');", []),
            ("declare const f:{(value:string):string;tag?:string}&{other:number};const value:string=f('ok');const tag:string|undefined=f.tag;", []),
            ("declare const f:((value:number)=>number)&{(value:string):string;tag:string};const n:number=f(1);const s:string=f('ok');", []),
            ("declare const f:{(value:number):number;left:string}&{(value:string):string;right:number};const n:number=f(1);const s:string=f('ok');", []),
            ("declare const f:{<T>(value:T):T;tag:string}&{other:number};const n:number=f<number>(1);const s:string=f('ok');", []),
            ("interface Callable<A>{<T extends A=A>(value:T):T;}declare const f:Callable<number>&{tag:string};const n:number=f<number>(1);", []),
            ("interface Callable{<T>(...values:[T,T]):T;}declare const f:Callable&{tag:string};const n:number=f(1,2);", []),
            ("declare const f:{(...values:[number,string?]):number;tag:string}&{other:number};const values:[number,string]=[1,'ok'];const n:number=f(...values);", []),
            ("declare const f:({(value:number):number;tag:string}&{other:number})|undefined;const n:number|undefined=f?.(1);", []),
            ("declare const f:{(callback:(value:number)=>number):number;tag:string}&{other:number};const n:number=f(value=>value+1);", []),
            ("declare const f:{(options:{callback:(value:number)=>number}):number;tag:string}&{other:number};const n:number=f({callback:value=>value+1});", []),
            ("declare const f:{<T>(value:T,options:{callback:(value:T)=>T}):T;tag:string}&{other:number};const n:number=f(1,{callback:value=>value+1});", []),
            ("class Brand{private brand=1;}interface Callable{(value:string):string;}declare const f:Brand&Callable;const s:string=f('ok');", []),
            ("declare const f:{(value:string):string;tag:string}&{other:number};f(1);", ["TS2345"]),
            ("interface Base{(value:string):string;}interface Callable extends Base{}declare const f:Callable&{tag:string};f(1);", ["TS2345"]),
            ("interface Base<T>{(value:T):T;}interface Callable<A> extends Base<A>{}declare const f:Callable<number>&{tag:string};f('wrong');", ["TS2345"]),
            ("interface Base<A>{<T extends A=A>(value:T):T;}interface Callable<B> extends Base<B>{}declare const f:Callable<number>&{tag:string};f<string>('wrong');", ["TS2344"]),
            ("interface Base<A>{<T extends A=A>():T;}interface Callable<B> extends Base<B>{}declare const f:Callable<number>&{tag:string};const value:string=f();", ["TS2322"]),
            ("interface Base<T>{<T extends string=string>(value:T):T;}interface Callable<U> extends Base<U>{}declare const f:Callable<number>&{tag:string};f<number>(1);", ["TS2344"]),
            ("declare const f:{(value:number):number;(value:string):string;tag:string}&{other:number};f(true);", ["TS2769"]),
            ("declare const f:{<T extends number>(value:T):T;tag:string}&{other:number};f<string>('wrong');", ["TS2344"]),
            ("declare const f:{(value:string):string;readonly tag:string}&{other:number};f.tag='wrong';", ["TS2540"]),
            ("declare const f:{(value:string):string;tag:string}&{other:number};const n:number=f('wrong');", ["TS2322"]),
            ("declare const f:{(callback:(value:number)=>number):number;tag:string}&{other:number};f((value:string)=>1);", ["TS2345"]),
            ("declare const f:{(callback:(value:number)=>number):number;tag:string}&{other:number};f(value=>value.missing());", ["TS2339"]),
            ("declare const f:{(options:{callback:(value:number)=>number}):number;tag:string}&{other:number};f({callback:value=>value.missing()});", ["TS2339"]),
            ("declare const f:((value:number)=>number)&{(options:{callback:(value:number)=>number}):number;tag:string};f({callback:value=>value.missing()});", ["TS2339"]),
            ("declare const f:{<T>(value:T,options:{callback:(value:T)=>T}):T;tag:string}&{other:number};f<number>(1,{callback:(value:string)=>1});", ["TS2322"]),
            ("declare const f:({(value:number):number;tag:string}&{other:number})|undefined;f(1);", ["TS2722"]),
        })
            foreach (bool modules in new[] { false, true }) yield return [source, codes, modules];
    }

    [Theory, MemberData(nameof(Cases))]
    public void StructuralIntersectionsKeepCallableSignaturesAndNamedMemberMetadata(string source, string[] codes, bool modules)
        => Assert.Equal(codes, Check(source, modules).Select(diagnostic => diagnostic.TsCode));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedCallableAdaptersUseTheOriginalDeclaredSignatureIds(bool inherited)
    {
        string source = "interface Callable{(value:string):string;}interface Derived extends Callable{}declare const original:Callable;declare const mixed:((value:number)=>number)&" +
            (inherited ? "Derived" : "Callable") + "&{readonly tag:string};original('first');mixed('second');mixed(1);";
        var document = new SourceDocument("callable-intersection-identities.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).WithEditorSyntax().ParseOrThrow();
        var checker = new TypeChecker(TypeCheckerOptions.Strict).WithEditorMetadata();
        var result = checker.CheckWithRecovery(statements, document);
        Assert.Empty(result.Diagnostics);
        var invocations = document.EditorSyntax!.Invocations;
        Assert.Equal(3, invocations.Count);
        var facts = checker.EditorFacts.Freeze();
        var original = Assert.IsType<EditorInvocationFact>(facts.GetInvocation(document, invocations[0].Owner));
        var mixed = Assert.IsType<EditorInvocationFact>(facts.GetInvocation(document, invocations[1].Owner));
        var numeric = Assert.IsType<EditorInvocationFact>(facts.GetInvocation(document, invocations[2].Owner));
        Assert.Equal(EditorInvocationStatus.Selected, mixed.Status);
        Assert.Equal(1, mixed.SelectedOrdinal);
        Assert.Equal(0, numeric.SelectedOrdinal);
        Assert.Equal(Assert.Single(original.Candidates).OriginalSignatureId, mixed.Candidates[1].OriginalSignatureId);
        Assert.All(mixed.Candidates, candidate => Assert.True(candidate.Declared.IsAvailable));
        Assert.Equal("string", mixed.SelectedSignature!.ReturnType.Text);
        var call = Assert.IsType<Expr.Call>(invocations[1].Owner);
        var fullType = Assert.IsType<TypeInfo.Intersection>(result.TypeMap.Get(call.Callee));
        var structural = Assert.Single(fullType.FlattenedTypes.OfType<TypeInfo.Record>());
        Assert.True(structural.IsFieldReadonly("tag"));
        Assert.Single(structural.CallSignatures!);
    }

    [Fact]
    public void InstantiatedInheritedCallsKeepTheirOriginalIdsAndConcreteEditorResult()
    {
        const string source = "interface Base<A>{(value:A):A;}interface Callable<B> extends Base<B>{}declare const original:Callable<number>;declare const mixed:Callable<number>&{tag:string};original(1);mixed(2);";
        var document = new SourceDocument("inherited-callable-identities.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).WithEditorSyntax().ParseOrThrow();
        var checker = new TypeChecker(TypeCheckerOptions.Strict).WithEditorMetadata();
        Assert.Empty(checker.CheckWithRecovery(statements, document).Diagnostics);
        var facts = checker.EditorFacts.Freeze();
        var invocations = document.EditorSyntax!.Invocations;
        Assert.Equal(2, invocations.Count);
        var original = Assert.IsType<EditorInvocationFact>(facts.GetInvocation(document, invocations[0].Owner));
        var mixed = Assert.IsType<EditorInvocationFact>(facts.GetInvocation(document, invocations[1].Owner));
        var candidate = Assert.Single(mixed.Candidates);
        Assert.Equal(Assert.Single(original.Candidates).OriginalSignatureId, candidate.OriginalSignatureId);
        Assert.True(candidate.Declared.IsAvailable);
        Assert.True(candidate.Instantiated!.IsAvailable);
        Assert.Equal(EditorInvocationStatus.Selected, mixed.Status);
        Assert.Equal("number", mixed.SelectedSignature!.ReturnType.Text);
    }

    [Theory, ModeData]
    public void CallableIntersectionValuesExecuteWithVerifiedCompilerIl(ExecutionMode mode)
        => Assert.Equal("3\n", TestHarness.Run("interface Callable{(value:number):number;}const f=((value:number)=>value+1) as Callable&{tag?:string};console.log(f(2));", mode));

    private static IReadOnlyList<SharpTS.Diagnostics.Diagnostic> Check(string source, bool modules)
    {
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        if (!modules) return checker.CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        string path = Path.Combine(Path.GetTempPath(), "callable-intersection-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source }, new TypeScriptProgramOptions { NoLib = true });
        var entry = resolver.LoadModule(path);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        return checker.GetDiagnostics();
    }
}

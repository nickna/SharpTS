using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class ReachableLoopBodyDomainTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var (source, codes) in new (string, string[])[]
        {
            ("declare const choose:boolean;let source:string|number='s';let result:string|number='s';do{if(choose){const bad:number=source.length;result='done';break;}source=1;result=1;}while(true);", ["TS2339"]),
            ("declare const choose:boolean;let source:string|number='s';let result:string|number='s';do{if(choose){const bad:number=source.length;result='done';break;}source=1;result=1;}while(true);const good:string=result;", ["TS2339"]),
            ("type State={kind:'text',text:string}|{kind:'count',count:number};let state:State={kind:'text',text:'a'};do{if(state.kind==='count'){const inside:number=state.count;break;}state={kind:'count',count:1};}while(true);", []),
            ("let value:string|number|boolean='s';do{const domain:string|number=value;if(typeof value==='number'){const inside:number=value;break;}value=1;}while(true);", []),
            ("let value:string|number='s';do{const first:string=value;value=1;}while(false);", []),
            ("let value:string|number='s';do{const first:string=value;break;value=1;}while(true);", []),
            ("let value:string|number='s';do{const first:string=value;continue;value=1;}while(false);", []),
            ("let value:string|number='s';do{if(typeof value==='number'){const inside:number=value;break;}value=1;continue;}while(true);", []),
            ("let value:string|number='s';outer:do{if(typeof value==='number'){const inside:number=value;break outer;}value=1;continue outer;}while(true);", []),
            ("let value:string|number='s';do{if(typeof value==='number'){const inside:number=value;break;}try{continue;}finally{value=1;}}while(true);", []),
            ("declare const choose:boolean;let value:string|number='s';do{const first:string=value;try{if(choose)break;value=1;}finally{value='done';}}while(true);", []),
            ("let value:string|number='s';do{const first:string=value;{let value=1;value=2;}if(typeof value==='string')break;}while(true);", []),
            ("let value:string|number='s';do{const callback=()=>{value=1;};const first:string=value;break;}while(true);", []),
            ("function identity<T>(value:T):T{return value;}let value:string|number='s';do{if(typeof value==='number'){const inside:number=value;break;}value=identity(1);}while(true);", []),
            ("let value:string|number='s';do{if(typeof value==='number'){const inside:number=value;break;}}while((value=1,true));", []),
            ("declare const choose:boolean;let value:string|number='s';do{const copy=value;if(choose){const bad:number=copy.length;break;}value=1;}while(true);", ["TS2339"]),
            ("declare const choose:boolean;let value:string|number='s';do{if(choose){const bad:string=value;break;}value=1;}while(true);", ["TS2322"]),
            ("let value:string|number='s';let counter=0;const box={value:0};do{if(typeof value==='number'){const inside:number=value;break;}counter+=1;box.value=counter;value=1;}while(true);", []),
            ("let value:string|number='s';do{if(typeof value==='number'){const inside:number=value;break;}(()=>{value=1;})();}while(true);", []),
            ("let value:string|number='s';do{if(typeof value==='number'){const inside:number=value;break;}((next:number)=>{value=next;})(1);}while(true);", []),
            ("let value:string|number='s';do{if(typeof value==='number'){const inside:number=value;break;}for(const next of [1]){value=next;}}while(true);", []),
            ("declare function make():{p?:string};let value:string|undefined='s';do{const wrong:string=value;value=make().p;}while(true);", ["TS2322"]),
            ("declare function make():{p?:string};let value:string|undefined='s';do{const wrong:string=value;value=({...make()}).p;}while(true);", ["TS2322"]),
            ("let value:string|1='s';do{const wrong:string=value;switch(value){case(value=1):break;}}while(true);", ["TS2322"]),
            ("declare let dynamic:any;let value:string|number='s';do{const wrong:number=value.length;if(typeof dynamic==='string')value=1;}while(true);", ["TS2339"]),
            ("declare const choose:boolean;declare let values:any[];do{const good:number=values[0]();if(choose)break;values=[()=>1];}while(true);", []),
            ("class Box<T>{value:T;constructor(value:T){this.value=value;}}declare const choose:boolean;let box=new Box<string|number>('s');do{const good:string|number=box.value;if(choose)break;box=new Box<string|number>(1);}while(true);", []),
            ("const box={count:0};let value:string|number='s';do{const wrong:string=value;box.count=1;box.count++;if(box.count===1)break;value=1;}while(true);", ["TS2322"]),
            ("let value:string|undefined='s';do{const wrong:string=value;value=(()=>{while(true){try{return 's';}finally{break;}}})();}while(true);", ["TS2322"]),
            ("let value:string|number='s';do{if(typeof value==='number'){const inside:number=value;break;}value=(()=>{try{return 1;}finally{const ignored=0;}})();}while(true);", []),
            ("let value:string|number='s';do{if(typeof value==='number'){const inside:number=value;break;}value=(()=>{try{return 1;}finally{while(true){break;}}})();}while(true);", []),
            ("class Base{p?:string;}class Derived extends Base{}declare function make():Derived;let value:string|undefined='s';do{const wrong:string=value;value=make().p;}while(true);", ["TS2322"]),
            ("class Base{p?:string;}class Derived extends Base{p='required';}declare function make():Derived;let value:string|undefined='s';do{const correct:string=value;value=make().p;}while(true);", []),
            ("type State={kind:'text',text:string}|{kind:'count',count:number};let state:State={kind:'text',text:'a'};do{if(state.kind==='count'){const inside:number=state.count;break;}state=(()=>{return {kind:'count' as const,count:1};})();}while(true);", []),
            ("declare const choose:boolean;let value:string|number='s';do{const correct:string=value;(()=>{var value=1;value=2;})();if(choose)break;}while(true);", []),
            ("let value:string|number='s';do{const wrong:number=value.length;value=(()=>{return 1;})();}while(true);", ["TS2339"]),
        })
            foreach (bool modules in new[] { false, true }) yield return [source, codes, modules];
    }

    [Theory, MemberData(nameof(Cases))]
    public void BodyReadsUseOnlyInitialAndReachableBackedgeValueDomains(string source, string[] codes, bool modules)
        => Assert.Equal(codes, Check(source, modules).Select(diagnostic => diagnostic.TsCode));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnUnreachableDeclaredConstituentDoesNotAuthorizeItsMember(bool modules)
        => Assert.Equal("TS2339", Assert.Single(Check("type State={kind:'text',text:string}|{kind:'count',count:number};let state:State={kind:'text',text:'a'};do{if(state.kind==='count'){const bad:number=state.count;break;}state={kind:'text',text:'b'};}while(true);", modules)).TsCode);

    [Fact]
    public void OuterReadsWritesAndCallsRetainTheirSourceIdentities()
    {
        const string source = "declare function touch():void;let value:string|number='s';do{touch();if(typeof value==='number'){const inside:number=value;break;}value=1;}while(true);";
        var document = new SourceDocument("reachable-loop-body-identities.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).WithEditorSyntax().ParseOrThrow();
        var checker = new TypeChecker(TypeCheckerOptions.Strict).WithEditorMetadata();
        Assert.Empty(checker.CheckWithRecovery(statements, document).Diagnostics);
        var facts = checker.EditorFacts.Freeze();
        Assert.Single(facts.Invocations);
        var callSyntax = Assert.Single(document.EditorSyntax!.Invocations);
        var call = Assert.IsType<EditorInvocationFact>(facts.GetInvocation(document, callSyntax.Owner));
        Assert.Equal(EditorInvocationStatus.Selected, call.Status);
        Assert.Same(statements[0], Assert.Single(call.Candidates).Origin!.Owner);
        var variable = Assert.IsType<Stmt.Var>(statements[1]);
        var loop = Assert.IsType<Stmt.DoWhile>(statements[2]);
        var body = Assert.IsType<Stmt.Block>(loop.Body);
        var conditional = Assert.IsType<Stmt.If>(body.Statements[1]);
        var guard = Assert.IsType<Expr.Binary>(conditional.Condition);
        var read = Assert.IsType<Expr.Variable>(Assert.IsType<Expr.Unary>(guard.Left).Right);
        var assignment = Assert.IsType<Expr.Assign>(Assert.IsType<Stmt.Expression>(body.Statements[2]).Expr);
        var symbol = checker.Bindings.GetSymbol(variable.Name);
        Assert.NotNull(symbol);
        Assert.Same(symbol, checker.Bindings.GetSymbol(read.Name));
        Assert.Same(symbol, checker.Bindings.GetSymbol(assignment.Name));
    }

    private static IReadOnlyList<SharpTS.Diagnostics.Diagnostic> Check(string source, bool modules)
    {
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        if (!modules) return checker.CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        string path = Path.Combine(Path.GetTempPath(), "reachable-loop-body-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source }, new TypeScriptProgramOptions { NoLib = true });
        var entry = resolver.LoadModule(path);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        return checker.GetDiagnostics();
    }
}

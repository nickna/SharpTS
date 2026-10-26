using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class GuardCheckedFactQueryTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (string source in new[]
        {
            "const object={ok(value:any):boolean{return true;}};let value:string|number='s';if((value=1,object).ok(value='ok')){const correct:string=value;}",
            "const object={ok(value:any):boolean{return true;}};let value:string|number='s';if((()=>{value=1;return object;})().ok(value='ok')){const correct:string=value;}",
            "function isString<T>(value:string|number,ignored:T):value is string{return typeof value==='string';}function read(value:string|number){if(isString(value,(()=>1)())){const correct:string=value;}}",
            "function isString<T>(value:string|number,callback:()=>T):value is string{return typeof value==='string';}let count=0;function read(value:string|number){if(isString(value,(count++,()=>1))){const correct:string=value;}}",
            "function read(box:{value:string|null}){if(box.value!==null){const correct:string=box.value;}}",
            "class Box{value:string|null=null;}function read(box:Box){if(box.value!==null){const correct:string=box.value;}}",
            "function read(values:[string|null]){if(values[0]!==null){const correct:string=values[0];}}",
            "function read(box:{value:string|number}){if(typeof box.value==='string'){const correct:string=box.value;}}",
            "function read(value:string|null){if(value!==null&&(value='fresh',value!==null)){const correct:string=value;}}",
            "function read(props:{step?:number}){const step=props.step===undefined?1:props.step;const correct:number=step;}",
            "function read(props:{step?:number}){const step=props.step===undefined?1:props.step;const precision=props.step===undefined?(step<1?2:0):props.step;const correct:number=precision;}",
            "function read(props:{callback?:(value:number)=>boolean}){return props.callback===undefined?false:props.callback(1);}",
            "function read(options:{initial?:{focus:()=>void}}){if(options.initial)options.initial.focus();}",
            "interface Props<T>{value?:T;}function read(props:Props<number>){const value=props.value===undefined?1:props.value;const correct:number=value;}",
            "interface Base{value?:number;}interface Props extends Base{}function read(props:Props){const value=props.value===undefined?1:props.value;const correct:number=value;}",
            "function read(options:{initial?:{focus:()=>void}}){if(options.initial&&true)options.initial.focus();}",
        })
            foreach (bool modules in new[] { false, true }) yield return [source, modules];
    }

    [Theory, MemberData(nameof(Cases))]
    public void GuardQueriesUseCheckedFactsAndCurrentBranchBindings(string source, bool modules)
        => Assert.Empty(Check(source, modules));

    [Theory]
    [InlineData("function read(props:{step?:number}){if(props.step===undefined){const wrong:number=props.step;}}")]
    [InlineData("function read(props:{step?:number}){if(props.step!==undefined){const wrong:undefined=props.step;}}")]
    public void OptionalMemberQueriesKeepTheNullishAndPresentBranchesDistinct(string source)
    {
        foreach (bool modules in new[] { false, true })
            Assert.Equal("TS2322", Assert.Single(Check(source, modules)).TsCode);
    }

    [Fact]
    public void GuardMethodCallsKeepTheirOriginalOwnerAndCheckedArgumentTypes()
    {
        const string source = "function ok(value:any):boolean{return true;}const object={ok};object.ok(0);let value:string|number='s';if((value=1,object).ok(value='ok')){const correct:string=value;}";
        var document = new SourceDocument("guard-checked-facts.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).WithEditorSyntax().ParseOrThrow();
        var checker = new TypeChecker(TypeCheckerOptions.Strict).WithEditorMetadata();
        var result = checker.CheckWithRecovery(statements, document);
        Assert.Empty(result.Diagnostics);
        var facts = checker.EditorFacts.Freeze();
        Assert.Equal(2, facts.Invocations.Count);
        var calls = document.EditorSyntax!.Invocations.Select(invocation =>
            Assert.IsType<EditorInvocationFact>(facts.GetInvocation(document, invocation.Owner))).ToArray();
        Assert.All(calls, call => Assert.Equal(EditorInvocationStatus.Selected, call.Status));
        var first = Assert.Single(calls[0].Candidates);
        var guarded = Assert.Single(calls[1].Candidates);
        Assert.Equal(first.OriginalSignatureId, guarded.OriginalSignatureId);
        Assert.Same(statements[0], first.Origin!.Owner);
        Assert.Same(first.Origin!.Owner, guarded.Origin!.Owner);
        var conditional = Assert.IsType<Stmt.If>(statements[4]);
        var body = Assert.IsType<Stmt.Block>(conditional.ThenBranch);
        var read = Assert.IsType<Stmt.Const>(Assert.Single(body.Statements)).Initializer;
        Assert.True(result.TypeMap.Get(read) is TypeInfo.String or TypeInfo.StringLiteral);
    }

    private static IReadOnlyList<SharpTS.Diagnostics.Diagnostic> Check(string source, bool modules)
    {
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        if (!modules) return checker.CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        string path = Path.Combine(Path.GetTempPath(), "guard-checked-facts-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source }, new TypeScriptProgramOptions { NoLib = true });
        var entry = resolver.LoadModule(path);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        return checker.GetDiagnostics();
    }
}

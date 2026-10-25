using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class IntersectionCallableFacetTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var (source, codes) in new (string, string[])[]
        {
            ("type NodeLike={} | string | number | boolean; class Component<P>{props!:P&{children?:NodeLike};state:any;} interface Props{children:(n:number)=>string;} class FetchUser extends Component<Props>{render(){return this.props.children(this.state.result);}}", []),
            ("declare const f:((value:number)=>string)&{tag:string}; const value:string=f(1); const tag:string=f.tag;", []),
            ("declare const f:(<T>(value:T)=>T)&{tag:string}; const value:number=f<number>(1); const text:string=f('text');", []),
            ("declare const f:((value:number)=>number)&((value:string)=>string)&{tag:string}; const value:number=f(1); const text:string=f('text');", []),
            ("type A=((value:'left'|'shared')=>number)&{left:number}; type B=((value:'right'|'shared')=>string)&{right:string}; declare const f:A|B; const value:number|string=f('shared');", []),
            ("declare const f:(((value:number)=>number)&{tag:string})|undefined; const value:number|undefined=f?.(1);", []),
            ("declare const f:((value:number)=>number)&{tag:string}; f('wrong');", ["TS2345"]),
            ("type A=((value:number)=>number)&{left:number}; type B=((value:string)=>string)&{right:string}; declare const f:A|B; f(1);", ["TS2345"]),
            ("declare const f:(((value:number)=>number)&{tag:string})|{tag:string}; f(1);", ["TS2349"]),
            ("declare const f:(((value:number)=>number)&{tag:string})|undefined; f(1);", ["TS2722"]),
            ("declare const f:((value:number)=>number)&{tag:string}; const value:number=f.tag;", ["TS2322"]),
        })
            foreach (bool modules in new[] { false, true }) yield return [source, codes, modules];
    }

    [Theory, MemberData(nameof(Cases))]
    public void CallsUseGuaranteedCallableFacetsWithoutChangingUnionOrPropertySemantics(string source, string[] codes, bool modules)
    {
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        if (!modules)
            Assert.Equal(codes, checker.CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics.Select(d => d.TsCode));
        else
        {
            string path = Path.Combine(Path.GetTempPath(), "intersection-callable-" + Guid.NewGuid().ToString("N"), "main.ts");
            var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source }, new TypeScriptProgramOptions { NoLib = true });
            var entry = resolver.LoadModule(path);
            checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
            Assert.Equal(codes, checker.GetDiagnostics().Select(d => d.TsCode));
        }
    }

    [Fact]
    public void InvocationUsesTheOriginalFunctionWhileTheCalleeRetainsItsPropertyFacets()
    {
        const string source = "declare const f:((value:number)=>number)&{tag:string}; const value=f(1); const other=f(2);";
        var document = new SourceDocument("intersection-callable.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).WithEditorSyntax().ParseOrThrow();
        var checker = new TypeChecker(TypeCheckerOptions.Strict).WithEditorMetadata();
        var result = checker.CheckWithRecovery(statements, document);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(2, document.EditorSyntax!.Invocations.Count);
        var call = Assert.IsType<Expr.Call>(document.EditorSyntax.Invocations[0].Owner);
        var intersection = Assert.IsType<TypeInfo.Intersection>(result.TypeMap.Get(call.Callee));
        Assert.Single(intersection.FlattenedTypes.OfType<TypeInfo.Function>());
        Assert.Contains(intersection.FlattenedTypes, type => type is TypeInfo.Record record && record.Fields.ContainsKey("tag"));
        var facts = checker.EditorFacts.Freeze();
        var fact = Assert.IsType<EditorInvocationFact>(facts.GetInvocation(document, call));
        Assert.Equal(EditorInvocationStatus.Selected, fact.Status);
        Assert.Equal(0, fact.SelectedOrdinal);
        var candidate = Assert.Single(fact.Candidates);
        Assert.True(candidate.Declared.IsAvailable);
        var second = Assert.IsType<EditorInvocationFact>(facts.GetInvocation(document, document.EditorSyntax.Invocations[1].Owner));
        Assert.Equal(candidate.OriginalSignatureId, Assert.Single(second.Candidates).OriginalSignatureId);
        Assert.Equal("number", fact.SelectedSignature!.ReturnType.Text);
    }
}

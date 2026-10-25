using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class ImmediateArrowArgumentOrderTests
{
    public static IEnumerable<object[]> ValidPrograms()
    {
        foreach (string source in new[]
        {
            "let value:string|number=\"before\"; ((unused:any)=>{const good:number=value;})(value=1);",
            "let value:string|number=\"before\"; ((unused:any)=>{value=1;})(value=\"argument\"); const good:number=value;",
            "let value:string|number=\"before\"; ((callback:(value?:number)=>void)=>{})((param:number=(value=1))=>{}); const good:string=value;",
            "const result:number=(<T>(ignored:any)=>null as T)(1);",
            "const result:number=(<T>(value:T,callback:(value:T)=>T)=>callback(value))(1,value=>value+1);",
            "const result:number=(<T>(value:T,options:{callback:(value:T)=>T})=>options.callback(value))(1,{callback:value=>value+1});",
            "const result:number=(<T>(value:T,callbacks:((value:T)=>T)[])=>callbacks[0](value))(1,[value=>value+1]);",
            "const result:number=(<T>(value:T)=>value)<number>(1);",
        })
            foreach (bool modules in new[] { false, true }) yield return [source, modules];
    }

    [Theory, MemberData(nameof(ValidPrograms))]
    public void EagerArgumentsPrecedeOneBodyCheckAndKeepGenericContextualTyping(string source, bool modules)
        => Assert.Empty(Check(source, modules));

    public static IEnumerable<object[]> InvalidPrograms()
    {
        foreach (var (source, code) in new[]
        {
            ("((unused:number)=>{function bad(value){};})(1);", "TS7006"),
            ("((unused:number)=>{((other:number)=>{function bad(value){};})(2);})(1);", "TS7006"),
            ("((callback:(value:number)=>number)=>callback(1))((value:string)=>1);", "TS2345"),
            ("(<T>(value:T,options:{callback:(value:T)=>void})=>options.callback(value))(1,{callback:value=>value.notThere()});", "TS2339"),
            ("(<T>(value:T,callbacks:((value:T)=>void)[])=>callbacks[0](value))(1,[value=>value.notThere()]);", "TS2339"),
            ("((unused:number)=>{const bad:number=\"bad\";})(1);", "TS2322"),
            ("((unused:number)=>{((other:number)=>{const bad:number=\"bad\";})(2);})(1);", "TS2322"),
            ("((callback:(value:number)=>void)=>{} )((value:number)=>{const bad:number=\"bad\";});", "TS2322"),
        })
            foreach (bool modules in new[] { false, true }) yield return [source, code, modules];
    }

    [Theory, MemberData(nameof(InvalidPrograms))]
    public void ImmediateBodiesAndDeferredCallbacksDoNotDuplicateDiagnostics(string source, string code, bool modules)
        => Assert.Equal(code, Assert.Single(Check(source, modules)).TsCode);

    [Fact]
    public void GenericImmediateCallKeepsItsActualEditorCandidateAndResultContext()
    {
        const string source = "const result:number=(<T>(ignored:any)=>null as T)(1);";
        var document = new SourceDocument("immediate-generic.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).WithEditorSyntax().ParseOrThrow();
        var checker = new TypeChecker(TypeCheckerOptions.Strict).WithEditorMetadata(true);
        Assert.Empty(checker.CheckWithRecovery(statements, document).Diagnostics);
        var invocation = Assert.Single(document.EditorSyntax!.Invocations);
        var fact = checker.EditorFacts.Freeze().GetInvocation(document, invocation.Owner)!;
        Assert.Equal(EditorInvocationStatus.Selected, fact.Status);
        Assert.Equal(0, fact.SelectedOrdinal);
        Assert.True(Assert.Single(fact.Candidates).Instantiated!.IsAvailable);
        Assert.Equal("number", fact.SelectedSignature!.ReturnType.Text);
    }

    [Theory, ModeData]
    public void EagerArgumentsExecuteOnceBeforeTheBodyWithVerifiedCompilerIl(ExecutionMode mode)
    {
        const string source = """
            let count=0;
            let value:string|number='before';
            ((unused:number)=>{
                const checked:number=value;
                console.log(checked.toFixed(0));
                console.log(count);
                value='body';
            })((count+=1,value=7));
            const checked:string=value;
            console.log(checked);
            console.log(count);
            """;
        Assert.Equal("7\n1\nbody\n1\n", TestHarness.Run(source, mode));
    }

    private static IReadOnlyList<SharpTS.Diagnostics.Diagnostic> Check(string source, bool modules)
    {
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        if (!modules) return checker.CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        string path = Path.Combine(Path.GetTempPath(), "immediate-arrow-order-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source }, new TypeScriptProgramOptions { NoLib = true });
        var entry = resolver.LoadModule(path);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        return checker.GetDiagnostics();
    }
}

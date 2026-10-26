using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class LogicalConditionWriteGuardTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var (source, codes) in new (string, string[])[]
        {
            ("let value:string|number='s';if(typeof value==='string'&&(value=1)){const wrong:string=value;}", ["TS2322"]),
            ("let value:string|number='s';if(typeof value==='string'&&(value=1,true)){const wrong:string=value;}", ["TS2322"]),
            ("let value:string|number='s';if(typeof value==='string'&&(value=1)){const correct:number=value;}", []),
            ("let value:string|number='s';if(typeof value==='string'&&(value=1,true)){const correct:number=value;}", []),
            ("function read(value:string|number){if(typeof value==='string'&&true){const correct:string=value;}}", []),
            ("let value:string|number='s';if((value=1,true)&&typeof value==='number'){const correct:number=value;}", []),
            ("let value:string|number='s';if((value=1)&&typeof value==='string'){const unreachable:never=value;}", []),
            ("declare const choose:boolean;function read(value:string|number){if(typeof value==='string'&&choose&&(value=1,true)){const correct:number=value;}}", []),
            ("declare const choose:boolean;function read(value:string|number){if((typeof value==='string'&&(value=1,true))&&choose){const correct:number=value;}}", []),
            ("function read(value:string|number){if(typeof value==='string'&&(value=1)){}else{const correct:number=value;}}", []),
            ("declare function accept(callback:()=>void):boolean;function read(value:string|number){if(typeof value==='string'&&accept(()=>{value=1;})){const correct:string=value;}}", []),
            ("function read(value:string|number){if(typeof value==='string'&&((value:number)=>{value=2;return true;})(1)){const correct:string=value;}}", []),
            ("function read(value:string|number){if(typeof value==='string'&&(()=>{value=2;return true;})()){const correct:number=value;}}", []),
            ("let value:string|number='s';if(typeof value==='string'&&(value=1,typeof value==='number')){const correct:number=value;}", []),
            ("let value:string|number='s';if(typeof value==='string'&&(value=1,typeof value==='string')){const unreachable:never=value;}", []),
            ("let value:string|number='s';do{if(typeof value==='string'&&(value=1,true))break;value=1;}while(true);const correct:number=value;", []),
            ("let value:string|number='s';do{if(typeof value==='string'&&(value=1,true))break;value=1;}while(true);const wrong:string=value;", ["TS2322"]),
        })
            foreach (bool modules in new[] { false, true }) yield return [source, codes, modules];
    }

    [Theory, MemberData(nameof(Cases))]
    public void EvaluatedWritesInvalidateOnlyEarlierProofsOfTheSameBinding(string source, string[] codes, bool modules)
        => Assert.Equal(codes, Check(source, modules).Select(diagnostic => diagnostic.TsCode));

    [Fact]
    public void RightCallIsCapturedOnceFromItsCheckedDeclaration()
    {
        const string source = "declare function choose():boolean;let value:string|number='s';if(typeof value==='string'&&(value=1,choose())){const correct:number=value;}";
        var document = new SourceDocument("logical-condition-write.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).WithEditorSyntax().ParseOrThrow();
        var checker = new TypeChecker(TypeCheckerOptions.Strict).WithEditorMetadata();
        Assert.Empty(checker.CheckWithRecovery(statements, document).Diagnostics);
        var facts = checker.EditorFacts.Freeze();
        Assert.Single(facts.Invocations);
        var invocation = Assert.Single(document.EditorSyntax!.Invocations);
        var call = Assert.IsType<EditorInvocationFact>(facts.GetInvocation(document, invocation.Owner));
        Assert.Equal(EditorInvocationStatus.Selected, call.Status);
        Assert.Same(statements[0], Assert.Single(call.Candidates).Origin!.Owner);
    }

    private static IReadOnlyList<SharpTS.Diagnostics.Diagnostic> Check(string source, bool modules)
    {
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        if (!modules) return checker.CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        string path = Path.Combine(Path.GetTempPath(), "logical-condition-write-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source }, new TypeScriptProgramOptions { NoLib = true });
        var entry = resolver.LoadModule(path);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        return checker.GetDiagnostics();
    }
}

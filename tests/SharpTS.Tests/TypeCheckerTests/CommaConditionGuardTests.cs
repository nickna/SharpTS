using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class CommaConditionGuardTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var (source, codes) in new (string, string[])[]
        {
            ("declare function touch():void;function read(value:string|number){if((touch(),typeof value==='string')){const correct:string=value;}}", []),
            ("declare function touch():void;function read(value:string|number){if((touch(),typeof value==='string')){}else{const correct:number=value;}}", []),
            ("declare function touch():void;function read(value:string|undefined){if((touch(),value!==undefined)){const correct:string=value;}}", []),
            ("let value:string|number='s';if((value=1,typeof value==='string')){const unreachable:never=value;}", []),
            ("declare function touch():void;function read(value:string|number){if((touch(),touch(),typeof value==='string')){const correct:string=value;}}", []),
            ("declare function touch():void;function read(value:string|number){const correct:number=(touch(),typeof value==='string')?value.length:value+1;}", []),
            ("declare function touch():void;function read(value:string|number){if((typeof value==='string',touch(),true)){const wrong:string=value;}}", ["TS2322"]),
            ("declare function touch():void;function read(value:string|number){if((touch(),typeof value==='string')){const wrong:number=value;}}", ["TS2322"]),
            ("declare function touch():void;function read(value:string|number){if((touch(),typeof value==='string')){}else{const wrong:string=value;}}", ["TS2322"]),
            ("declare function touch():void;function read(value:string|number){if((touch(),typeof value==='string')){{let value=1;const wrong:string=value;}}}", ["TS2322"]),
        })
            foreach (bool modules in new[] { false, true }) yield return [source, codes, modules];
    }

    [Theory, MemberData(nameof(Cases))]
    public void OnlyTheFinalOperandSuppliesTheCheckedConditionProof(string source, string[] codes, bool modules)
        => Assert.Equal(codes, Check(source, modules).Select(diagnostic => diagnostic.TsCode));

    [Fact]
    public void PrefixCallIsCapturedOnceWithItsOriginalDeclaration()
    {
        const string source = "declare function touch():void;function read(value:string|number){if((touch(),typeof value==='string')){const correct:string=value;}}";
        var document = new SourceDocument("comma-condition-guard.ts", source);
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
        string path = Path.Combine(Path.GetTempPath(), "comma-condition-guard-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source }, new TypeScriptProgramOptions { NoLib = true });
        var entry = resolver.LoadModule(path);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        return checker.GetDiagnostics();
    }
}

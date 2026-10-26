using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class LoopBodyProofFallbackTests
{
    [Theory]
    [InlineData("declare const again:boolean;let value:string|number='s';do{const correct:string=value;value+=1;}while(again);", false)]
    [InlineData("declare const again:boolean;let value=1;do{const correct:number=value;value+=1;}while(again);", false)]
    [InlineData("declare const again:boolean;declare function start():string|number;let value:string|number|boolean=start();do{const correct:string|number=value;value+='x';}while(again);", false)]
    [InlineData("declare const again:boolean;let value:string|number=1;do{value+='x';}while(again);", true)]
    [InlineData("declare const again:boolean;let value:string|number=1;do{const wrong:number=value;value+='x';}while(again);", true)]
    [InlineData("declare const again:boolean;declare function start():string|number;let outer:string|number='s';do{const wrong:string=outer;let local:string|number=start();local+='x';outer=local;}while(again);", true)]
    public void CompoundTransferPreservesOrdinaryOperatorAdmission(string source, bool rejected)
    {
        foreach (bool modules in new[] { false, true })
        {
            var diagnostics = Check(source, modules);
            if (rejected) Assert.NotEmpty(diagnostics);
            else Assert.Empty(diagnostics);
        }
    }

    private const string AliasSource = """
        declare function touch():void;
        function make(){
            type Link={next:Link};
            return null! as Link;
        }
        let value=make();
        do{
            touch();
            const wrong:number='x';
            value={next:value};
        }while(false);
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFailedAliasQueryKeepsEarlierBodyDiagnostics(bool modules)
    {
        var diagnostic = Check(AliasSource, modules).First();
        Assert.Equal("TS2322", diagnostic.TsCode);
        Assert.Equal(9, diagnostic.Line);
    }

    [Fact]
    public void AFailedAliasQueryKeepsTheOneAuthoritativeInvocation()
    {
        var document = new SourceDocument("loop-body-query-fallback.ts", AliasSource);
        var statements = new Parser(new Lexer(AliasSource).ScanTokens())
            .WithSourceDocument(document).WithEditorSyntax().ParseOrThrow();
        var checker = new TypeChecker(TypeCheckerOptions.Strict).WithEditorMetadata();
        var result = checker.CheckWithRecovery(statements, document);
        Assert.Equal("TS2322", result.Diagnostics.First().TsCode);
        var invocation = Assert.Single(checker.EditorFacts.Freeze().Invocations, call =>
            call.Candidates.Any(candidate => ReferenceEquals(candidate.Origin?.Owner, statements[0])));
        Assert.Equal(EditorInvocationStatus.Selected, invocation.Status);
        Assert.Same(statements[0], Assert.Single(invocation.Candidates).Origin!.Owner);
    }

    private static IReadOnlyList<SharpTS.Diagnostics.Diagnostic> Check(string source, bool modules)
    {
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        if (!modules) return checker.CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        string path = Path.Combine(Path.GetTempPath(), "loop-body-query-fallback-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source },
            new TypeScriptProgramOptions { NoLib = true });
        var entry = resolver.LoadModule(path);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        return checker.GetDiagnostics();
    }
}

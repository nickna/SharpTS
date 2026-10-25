using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class StaticBlockOuterFlowTests
{
    public static IEnumerable<object[]> ValidPrograms()
    {
        foreach (string source in new[]
        {
            "let value:string|number='ok'; class C { static { value=1; } } const good:number=value;",
            "let value:string|number='ok'; class C { static { { value=1; } } } const good:number=value;",
            "let value:string|number='ok'; class C { static { value=1; } static { const good:number=value; value='next'; } } const good:string=value;",
            "declare const choose:boolean; let value:string|number='ok'; class C { static { if(choose){value=1;}else{value=2;} } } const good:number=value;",
            "let value:string|number='ok'; class C { static { if(false) value=1; } } const good:string=value;",
            "let value:string|number='ok'; class C { static { while(false){value=1;} } } const good:string=value;",
            "let value:string|number='ok'; class C { static { do { value=1; } while(false); } } const good:number=value;",
            "let value:string|number='ok'; class C { static { (()=>value=1)(); } } const good:number=value;",
            "let value:string|number='ok'; class C { static { try { value='first'; } finally { value=1; } } } const good:number=value;",
            "declare const choose:number; let value:string|number='ok'; class C { static { switch(choose){case 0:value=1;break;default:value=2;} } } const good:number=value;",
            "let value:string|number='ok'; class C { static { value=1; } method(){value='deferred';} } const good:number=value;",
            "let value:string|number='ok'; class C { static { const later=()=>value=1; } } const good:string=value;",
            "let value:string|number='ok'; class C { static { let value:string|number=1; value=2; } } const good:string=value;",
            "let value:string|number='ok'; if(false){ class C { static { value=1; } } } const good:string=value;",
            "class C { static { const later=()=>new Later(); } } class Later {}",
            "let value:string|number='ok'; class C { static { do { value=1; break; value='dead'; } while(false); } } const good:number=value;",
            "let value:string|number='ok'; class C { static { ((unused:any)=>{value=1;})(value='argument'); } } const good:number=value;",
            "let value:string|number='ok'; class C { static { ((unused:any)=>{value=value;})(value=1); } } const good:number=value;",
            "let value:string|number='ok'; class C { static { (()=>{value=1;return;})(); } } const good:number=value;",
            "let value:string|number='ok'; class C { static { false&&(value=1); true||(value=1); } } const good:string=value;",
            "let value:string|number='ok'; class C { static { false?(value=1):(value='next'); } } const good:string=value;",
            "declare const choose:boolean; let value:string|number='ok'; class C { static { choose?(value=1):(value=2); } } const good:number=value;",
            "let value:string|number='ok'; class C { static { for(value=1;false;value='dead'){} } } const good:number=value;",
            "let value:string|number='ok'; class C { static { do{value=1;break;}while((value='dead',false)); } } const good:number=value;",
            "let value:string|number='ok'; class C { static { for(;;value='dead'){value=1;break;} } } const good:number=value;",
            "let value:string|number='ok'; class C { static { done:{value=1;break done;value='dead';} } } const good:number=value;"
        })
            foreach (bool modules in new[] { false, true }) yield return [source, modules];
    }

    [Theory, MemberData(nameof(ValidPrograms))]
    public void EagerStaticBlockExitFlowReachesTheEnclosingScope(string source, bool modules)
        => Assert.Empty(Check(source, modules));

    public static IEnumerable<object[]> InvalidPrograms()
    {
        foreach (var (source, code) in new[]
        {
            ("declare const choose:boolean; let value:string|number='ok'; class C { static { if(choose){value=1;} } } const wrong:number=value;", "TS2322"),
            ("declare const choose:boolean; let value:string|number='ok'; class C { static { while(choose){value=1;} } } const wrong:number=value;", "TS2322"),
            ("let value:string|number='ok'; class C { static field=(value=1); } const wrong:number=value;", "TS2322"),
            ("let value:string|number='ok'; class C { static { const later=()=>value=1; } } const wrong:number=value;", "TS2322"),
            ("let value:string|number='ok'; class C { static { let value:string|number=1; value=2; } } const wrong:number=value;", "TS2322"),
            ("class C { static { new Later(); } } class Later {}", "TS2449"),
            ("declare const choose:boolean; let value:string|number='ok'; if(choose){class C{static{value=1;}}} const wrong:number=value;", "TS2322"),
            ("let value:string|number='ok'; class C { static { if(false){ const bad:string=1; value=1; } } } const good:string=value;", "TS2322"),
            ("declare const present:number; let value:string|number='ok'; class C { static { present??(value=1); } } const wrong:string=value;", "TS2322"),
            ("declare const choose:boolean; let value:string|number='ok'; class C{static{try{value=1;if(choose)throw 0;value='ok';}catch{}}} const wrong:string=value;", "TS2322"),
            ("let value:string|number='ok'; class C{static{try{value=1;value='ok';}catch{}}} const wrong:string=value;", "TS2322"),
            ("let value:string|number='ok'; class C{static{try{value=1;throw 0;}catch{}}} const wrong:number=value;", "TS2322"),
            ("declare const choose:boolean; let value:string|number='ok'; class C{static{while(true){if(choose)break;value=1;break;}}} const wrong:number=value;", "TS2322"),
            ("declare const choose:boolean; declare const key:number; let value:string|number='ok'; class C{static{switch(key){case 0:if(choose)break;value=1;break;default:value=2;}}} const wrong:number=value;", "TS2322")
        })
            foreach (bool modules in new[] { false, true }) yield return [source, code, modules];
    }

    [Theory, MemberData(nameof(InvalidPrograms))]
    public void ConditionalDeferredAndShadowedWritesDoNotProveAnOuterNarrowing(string source, string code, bool modules)
        => Assert.Equal(code, Assert.Single(Check(source, modules)).TsCode);

    [Theory]
    [InlineData("let value:string|number='ok'; if(false)value=1; const wrong:number=value;", "TS2322")]
    [InlineData("declare const choose:boolean; let value:string|number='ok'; if(choose){value=1;}else{value=2;} const good:number=value;", null)]
    public void StaticBlocksAndOrdinaryBranchesUseTheSameCheckedJoin(string source, string? code)
    {
        var diagnostics = Check(source, false);
        if (code is null) Assert.Empty(diagnostics);
        else Assert.Equal(code, Assert.Single(diagnostics).TsCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullishSkippedBranchProvesItsSelectedVariableNonNullish(bool modules)
        => Assert.Empty(Check("""
            let source: string | undefined;
            let value: string | undefined;
            value ?? (value = source ?? 'fallback');
            const good: string = value;
            let assigned: string | undefined;
            assigned ??= source ?? 'fallback';
            const alsoGood: string = assigned;
            """, modules));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullishFallbackWithoutAssignmentDoesNotProveTheSelectedVariable(bool modules)
        => Assert.Equal("TS2322", Assert.Single(Check("""
            let value: string | undefined;
            value ?? 'fallback';
            const wrong: string = value;
            """, modules)).TsCode);

    private static IReadOnlyList<SharpTS.Diagnostics.Diagnostic> Check(string source, bool modules)
    {
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        if (!modules) return checker.CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        string path = Path.Combine(Path.GetTempPath(), "static-block-outer-flow-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source },
            new TypeScriptProgramOptions { NoLib = true });
        var entry = resolver.LoadModule(path);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        return checker.GetDiagnostics();
    }
}

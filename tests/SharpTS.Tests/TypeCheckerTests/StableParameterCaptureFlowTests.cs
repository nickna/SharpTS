using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class StableParameterCaptureFlowTests
{
    public static IEnumerable<object[]> StablePrograms()
    {
        foreach (string source in new[]
        {
            "type Action={kind:'move',layerId:number}|{kind:'other'}; declare const layers:{id:number}[]; function reduce(action:Action){switch(action.kind){case 'move':layers.findIndex(layer=>layer.id===action.layerId);}}",
            "type Action={kind:'text',command:string}|{kind:'other'}; declare const values:string[]; const reduce=(action:Action)=>{switch(action.kind){case 'text':values.map(value=>value+action.command);}};",
            "type Action={kind:'move',layerId:number}|{kind:'other'}; function read(action:Action){if(action.kind==='move'){const callback=()=>action.layerId;const shadow=(action:number)=>{action=2;};}}",
            "type Action={kind:'move',layerId:number}|{kind:'other'}; function read(action:Action){if(action.kind==='move'){const callback=()=>action.layerId;{let action=1;action=2;}}}",
            "type Action={kind:'move',layerId:number}|{kind:'other'}; function read(action:Action){if(action.kind==='move'){const callback=()=>action.layerId;function shadow(){var action=1;action=2;}}}",
            "type Action={kind:'move',layerId:number}|{kind:'other'}; function read(action:Action){if(action.kind==='move'){const callback=()=>action.layerId;try{}catch(action){action=1;}}}",
            "type Action={kind:'move',layerId:number}|{kind:'other'}; function read(action:Action){if(action.kind==='move'){const callback=()=>action.layerId;for(let action of [1,2]){action=2;}}}",
            "type Action={kind:'move',layerId:number}|{kind:'other'}; class Reducer{read(action:Action){if(action.kind==='move'){const callback=()=>action.layerId;}}}",
            "type Action={kind:'move',layerId:number}|{kind:'other'}; const Reducer=class{read(action:Action){if(action.kind==='move'){const callback=()=>action.layerId;}}};",
            "type Action={kind:'move',layerId:number}|{kind:'other'}; function read(action:Action){if(action.kind==='move'){const callback=()=>action.layerId;function shadow(){for(var action of [1,2])action=2;}}}",
            "type Action={kind:'move',layerId:number}|{kind:'other'}; function read({action}:{action:Action}){if(action.kind==='move'){const callback=()=>action.layerId;}}",
            "type Action={kind:'move',layerId:number}|{kind:'other'}; const read=({action}:{action:Action})=>{if(action.kind==='move'){const callback=()=>action.layerId;}};"
        })
            foreach (bool modules in new[] { false, true }) yield return [source, modules];
    }

    [Theory, MemberData(nameof(StablePrograms))]
    public void NeverWrittenParametersRetainBindingOwnedNarrowingInCallbacks(string source, bool modules)
        => Assert.Empty(Check(source, modules));

    public static IEnumerable<object[]> MutablePrograms()
    {
        foreach (string source in new[]
        {
            "type Action={kind:'move',layerId:number}|{kind:'other'}; function read(action:Action){if(action.kind==='move'){const callback=()=>action.layerId;action={kind:'other'};}}",
            "type Action={kind:'move',layerId:number}|{kind:'other'}; function read(action:Action){if(action.kind==='move'){const callback=()=>action.layerId;function mutate(){action={kind:'other'};}}}",
            "type Action={kind:'move',layerId:number}|{kind:'other'}; let action:Action={kind:'move',layerId:1}; const callback=()=>action.layerId; action={kind:'other'};",
            "let value:string|number='ok'; const callback=()=>{const wrong:string=value;}; value=1;",
            "type Action={kind:'move',layerId:number}|{kind:'other'}; function read({action}:{action:Action}){if(action.kind==='move'){const callback=()=>action.layerId;action={kind:'other'};}}"
        })
            foreach (bool modules in new[] { false, true }) yield return [source, modules];
    }

    [Theory, MemberData(nameof(MutablePrograms))]
    public void ReassignedParametersAndMutableGlobalsRemainConservative(string source, bool modules)
    {
        var diagnostic = Assert.Single(Check(source, modules));
        Assert.Contains(diagnostic.TsCode, new[] { "TS2339", "TS2322" });
    }

    private static IReadOnlyList<SharpTS.Diagnostics.Diagnostic> Check(string source, bool modules)
    {
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        if (!modules) return checker.CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        string path = Path.Combine(Path.GetTempPath(), "stable-parameter-capture-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source },
            new TypeScriptProgramOptions { NoLib = true });
        var entry = resolver.LoadModule(path);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        return checker.GetDiagnostics();
    }
}

using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class ShadowedAssignmentBindingTests
{
    public static IEnumerable<object[]> ValidPrograms()
    {
        foreach (string source in new[]
        {
            "function read(value:string){try{}catch(value){value=1;value='caught';}const outer:string=value;}",
            "function read(value:string){try{}catch(value:unknown){value=1;if(typeof value==='number'){value='caught';}}const outer:string=value;}",
            "function read(value:string){try{}catch(value:any){value=1;value='caught';}const outer:string=value;}",
            "function read(value:string){for(let value of [1,2]){value=2;}const outer:string=value;}",
            "function read(value:string){for(let value of [1]){value=2;}const outer:string=value;}",
            "function read(value:string){for(let value of [1 as const]){value=1;}const outer:string=value;}",
            "function read(value:string){for(let value of [1] as const){value=1;}const outer:string=value;}",
            "declare const values:(string|number)[];function read(value:boolean){for(let value of values){value=1;value='changed';const inner:string=value;}const outer:boolean=value;}",
            "function read(value:number){for(let value in {first:1}){value='next';}const outer:number=value;}",
            "function read(value:string){for(let value=0;value<1;value++){value=2;}value='after';}",
            "function read(value:string){function inner(){for(var value of [1,2]){value=2;}}const outer:string=value;}",
            "function read(value:string|number){if(typeof value==='string'){{let value=1;value=2;}value=1;const outer:number=value;}}",
            "type Action={kind:'move',layerId:number}|{kind:'other'};function read(action:Action){if(action.kind==='move'){const callback=()=>action.layerId;try{}catch(action){action=1;}for(let action of [1,2]){action=2;}}}"
        })
            foreach (bool modules in new[] { false, true }) yield return [source, modules];
    }

    [Theory, MemberData(nameof(ValidPrograms))]
    public void AssignmentTypesFollowTheResolvedShadowBinding(string source, bool modules)
        => Assert.Empty(Check(source, modules));

    public static IEnumerable<object[]> InvalidPrograms()
    {
        foreach (string source in new[]
        {
            "function read(value:string){try{}catch(value){value=1;}value=2;}",
            "function read(value:string){for(let value of [1,2]){value=2;}value=2;}",
            "function read(value:string){for(let value of [1,2]){value='wrong';}}",
            "function read(value:string){for(let value of [1 as const]){value=2;}}",
            "function read(value:string){for(let value of [1] as const){value=2;}}",
            "function read(value:number){for(let value in {first:1}){value=2;}}",
            "function read(value:string){function inner(){for(var value of [1,2]){value=2;}}value=2;}",
            "function read(value:string){for(let value=0;value<1;value++){value=2;}value=2;}"
        })
            foreach (bool modules in new[] { false, true }) yield return [source, modules];
    }

    [Theory, MemberData(nameof(InvalidPrograms))]
    public void InnerAndOuterDeclarationConstraintsRemainIndependent(string source, bool modules)
        => Assert.Equal("TS2322", Assert.Single(Check(source, modules)).TsCode);

    private static IReadOnlyList<SharpTS.Diagnostics.Diagnostic> Check(string source, bool modules)
    {
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        if (!modules) return checker.CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        string path = Path.Combine(Path.GetTempPath(), "shadowed-assignment-binding-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source },
            new TypeScriptProgramOptions { NoLib = true });
        var entry = resolver.LoadModule(path);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        return checker.GetDiagnostics();
    }
}

using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class LoopBodyTypeofProjectionTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var (source, codes) in new (string, string[])[]
        {
            ("interface Box{field:number;}declare const box:Box;let value:string|number='s';do{const wrong:string=value;if(typeof box==='object')value=1;}while(true);", ["TS2322"]),
            ("declare const absent:undefined;let value:string|number='s';do{const wrong:string=value;if(typeof absent==='undefined')value=1;}while(true);", ["TS2322"]),
            ("declare function identity<T>(value:T):T;let value:string|number='s';do{const wrong:string=value;if(typeof identity==='function')value=1;}while(true);", ["TS2322"]),
            ("interface Box{field:number;}declare const box:Box|undefined;let value:string|number='s';do{const wrong:string=value;if(typeof box==='object')value=1;}while(true);", ["TS2322"]),
            ("let value:string|number='s';do{if(typeof value==='number'){const correct:string=value.toFixed();break;}value=1;}while(true);", []),
        })
            foreach (bool modules in new[] { false, true }) yield return [source, codes, modules];
    }

    [Theory, MemberData(nameof(Cases))]
    public void UnclassifiedTypeofDomainsKeepReachableWrites(string source, string[] codes, bool modules)
        => Assert.Equal(codes, Check(source, modules).Select(diagnostic => diagnostic.TsCode));

    private static IReadOnlyList<SharpTS.Diagnostics.Diagnostic> Check(string source, bool modules)
    {
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        if (!modules) return checker.CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        string path = Path.Combine(Path.GetTempPath(), "loop-body-typeof-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source },
            new TypeScriptProgramOptions { NoLib = true });
        var entry = resolver.LoadModule(path);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        return checker.GetDiagnostics();
    }
}

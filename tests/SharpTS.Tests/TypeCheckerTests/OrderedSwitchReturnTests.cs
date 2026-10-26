using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class OrderedSwitchReturnTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var (source, codes) in new (string, string[])[]
        {
            ("function read(value:number):number{switch(value){case 0:console.log('fall');default:return 1;case 2:return 2;}}", []),
            ("function read(value:number):number{switch(value){default:const ignored=1;case 1:return 2;}}", []),
            ("function read(value:number,choose:boolean):number{switch(value){case 0:if(choose)break;return 1;default:return 2;}}", ["TS2366"]),
            ("function read(value:number,choose:boolean):number{switch(value){case 0:try{return 1;}finally{if(choose)break;}default:return 2;}}", ["TS2366"]),
            ("function read(value:number,choose:boolean):number{switch(value){case 0:local:{if(choose)break local;return 1;}default:return 2;}}", []),
        })
            foreach (bool modules in new[] { false, true }) yield return [source, codes, modules];
    }

    [Theory, MemberData(nameof(Cases))]
    public void ReturnProofFollowsNormalFallthroughAndKeepsAbruptAlternatives(string source, string[] codes, bool modules)
        => Assert.Equal(codes, Check(source, modules).Select(diagnostic => diagnostic.TsCode));

    private static IReadOnlyList<SharpTS.Diagnostics.Diagnostic> Check(string source, bool modules)
    {
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        if (!modules) return checker.CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        string path = Path.Combine(Path.GetTempPath(), "ordered-switch-return-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source },
            new TypeScriptProgramOptions { NoLib = true });
        var entry = resolver.LoadModule(path);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        return checker.GetDiagnostics();
    }
}

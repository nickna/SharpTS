using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class EmptyElseValueFlowTests
{
    public static IEnumerable<object[]> ValidPrograms()
    {
        foreach (string source in new[]
        {
            "function calls(exact?:number){let expected=exact;if(expected===undefined||expected===null)expected=1;const entry:{exact:number}={exact:expected};}",
            "function recover(value:number|null|undefined){if(value===undefined||value===null)value=1;const good:number=value;}",
            "function recover(value:number|null){if(value===null)value=1;const good:number=value;}",
            "function recover(value:string|number){if(typeof value==='string')value=1;const good:number=value;}",
            "function recover(left:number|undefined,right:number|undefined){if(left===undefined||right===undefined){left=1;right=1;}const first:number=left;const second:number=right;}"
        })
            foreach (bool modules in new[] { false, true }) yield return [source, modules];
    }

    [Theory, MemberData(nameof(ValidPrograms))]
    public void ImplicitElseRetainsTheConditionsFalseEdge(string source, bool modules)
        => Assert.Empty(Check(source, modules));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FalseConjunctionDoesNotExcludeBothOperands(bool modules)
        => Assert.Equal("TS2322", Assert.Single(Check("""
            function partial(left:number|undefined,right:number|undefined){
                if(left===undefined&&right===undefined){left=1;right=1;}
                const wrong:number=left;
            }
            """, modules)).TsCode);

    private static IReadOnlyList<SharpTS.Diagnostics.Diagnostic> Check(string source, bool modules)
    {
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        if (!modules) return checker.CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        string path = Path.Combine(Path.GetTempPath(), "empty-else-flow-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source },
            new TypeScriptProgramOptions { NoLib = true });
        var entry = resolver.LoadModule(path);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        return checker.GetDiagnostics();
    }
}

using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class LoopConditionFlowTests
{
    public static IEnumerable<object[]> VariantConditions()
    {
        foreach (var (declaration, loop) in new[]
        {
            ("class Link{next:Link|null=null;} let node:Link|null=new Link();", "while(node){value=1;node=node.next;}"),
            ("class Link{next:Link|null=null;} let node:Link|null=new Link();", "for(;node;){value=1;node=node.next;}"),
            ("let flag:true|false=true;", "while(flag){value=1;flag=false;}"),
            ("let flag:true|false=true;", "for(;flag;){value=1;flag=false;}"),
            ("let flag:true|false=false;", "while(flag){value=1;flag=true;}"),
            ("let flag:true|false=false;", "for(;flag;){value=1;flag=true;}"),
            ("", "while(0){value=1;}"),
            ("", "for(;0;){value=1;}")
        })
            foreach (bool staticBlock in new[] { false, true })
                foreach (bool modules in new[] { false, true })
                    yield return [declaration + " let value:string|number='none'; " +
                        (staticBlock ? "class C{static{" + loop + "}}" : loop) + " const wrong:string=value;", modules];
    }

    [Theory, MemberData(nameof(VariantConditions))]
    public void FirstPassBindingTruthinessDoesNotDiscardPossibleLoopWrites(string source, bool modules)
        => Assert.Equal("TS2322", Assert.Single(Check(source, modules)).TsCode);

    [Theory]
    [InlineData("let value:string|number='none'; while(false){value=1;} const good:string=value;")]
    [InlineData("let value:string|number='none'; while((false)){value=1;} const good:string=value;")]
    [InlineData("let value:string|number='none'; while(true){value=1;break;} const good:number=value;")]
    [InlineData("let value:string|number='none'; for(value=1;false;value='dead'){} const good:number=value;")]
    [InlineData("let value:string|number='none'; for(;;){value=1;break;} const good:number=value;")]
    public void LiteralBooleanAndOmittedConditionsKeepTheirCheckedExecutionEdges(string source)
        => Assert.Empty(Check(source, false));

    private static IReadOnlyList<SharpTS.Diagnostics.Diagnostic> Check(string source, bool modules)
    {
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        if (!modules) return checker.CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        string path = Path.Combine(Path.GetTempPath(), "loop-condition-flow-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source },
            new TypeScriptProgramOptions { NoLib = true });
        var entry = resolver.LoadModule(path);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        return checker.GetDiagnostics();
    }
}

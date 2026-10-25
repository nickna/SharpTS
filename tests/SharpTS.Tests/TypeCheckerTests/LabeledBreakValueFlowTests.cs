using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class LabeledBreakValueFlowTests
{
    public static IEnumerable<object[]> Programs()
    {
        foreach (var (statement, result, expectedCode) in new[]
        {
            ("outer:inner:{if(choose){value=1;break outer;}value='b';}", "string", "TS2322"),
            ("outer:inner:{if(choose){value=1;break inner;}value='b';}", "string", "TS2322"),
            ("outer:inner:while(choose){value=1;break outer;}", "string", "TS2322"),
            ("outer:inner:{if(choose){value=1;break outer;}value=2;}", "number", ""),
            ("outer:inner:{value=1;break outer;value='dead';}", "number", ""),
            ("outer:inner:while(true){value=1;break outer;}", "number", "")
        })
            foreach (bool modules in new[] { false, true })
                yield return ["declare const choose:boolean;let value:string|number='a';" + statement +
                    "const result:" + result + "=value;", expectedCode, modules];
    }

    [Theory, MemberData(nameof(Programs))]
    public void ChainedLabelsRetainTheActualStatementsBreakExits(string source, string expectedCode, bool modules)
    {
        var diagnostics = Check(source, modules);
        if (expectedCode.Length == 0) Assert.Empty(diagnostics);
        else Assert.Equal(expectedCode, Assert.Single(diagnostics).TsCode);
    }

    private static IReadOnlyList<SharpTS.Diagnostics.Diagnostic> Check(string source, bool modules)
    {
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        if (!modules) return checker.CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        string path = Path.Combine(Path.GetTempPath(), "labeled-break-flow-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source },
            new TypeScriptProgramOptions { NoLib = true });
        var entry = resolver.LoadModule(path);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        return checker.GetDiagnostics();
    }
}

using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class ExactOptionalWriteDiagnosticTests
{
    public static IEnumerable<object[]> ExactWrites()
    {
        foreach (string source in new[]
        {
            "const config:{value?:number}={};config.value=undefined;",
            "interface Config{value?:number;}declare const config:Config;config.value=undefined;",
            "interface Base<T>{value?:T;}interface Config extends Base<number>{}declare const config:Config;config.value=undefined;",
            "class Config{value?:number;}new Config().value=undefined;",
            "class Base<T>{value?:T;}class Config extends Base<number>{}new Config().value=undefined;",
            "function set<T>(config:T&{value?:number}){config.value=undefined;}",
            "function set<T>(config:{value?:T}){config.value=undefined;}",
            "declare const config:{value?:number};declare const value:number|undefined;config.value=value;",
            "declare const config:{value?:number};declare const value:string|undefined;config.value=value;",
            "declare const config:{value?:number};function set<T extends number|undefined>(value:T){config.value=value;}",
            "declare const config:{value?:number};declare const other:{value?:number};config.value=other.value;",
        })
            foreach (bool modules in new[] { false, true }) yield return [source, modules];
    }

    [Theory, MemberData(nameof(ExactWrites))]
    public void DottedUndefinedWritesToExactOptionalSlotsHaveTheCanonicalDiagnostic(string source, bool modules)
    {
        var diagnostic = Assert.Single(Check(source, modules, exact: true));
        Assert.Equal("TS2412", diagnostic.TsCode);
        Assert.Contains("exactOptionalPropertyTypes: true", diagnostic.Message);
    }

    [Theory]
    [InlineData("declare const config:{value?:number};config['value']=undefined;", "TS2322")]
    [InlineData("declare const config:{value:number};config.value=undefined;", "TS2322")]
    [InlineData("declare const config:{value?:number};config.value='bad';", "TS2322")]
    [InlineData("declare const config:{value?:number};config.value=null;", "TS2322")]
    [InlineData("declare const config:{value?:number};declare const value:unknown;config.value=value;", "TS2322")]
    [InlineData("function set<T>(config:T&{value?:number}&{value:number}){config.value=undefined;}", "TS2322")]
    [InlineData("declare const config:{readonly value?:number};config.value=undefined;", "TS2540")]
    [InlineData("class Config{static value?:number;}Config.value=undefined;", "TS2412")]
    [InlineData("declare const config:{value?:number}|{value:number};config.value=undefined;", "TS2412")]
    public void OtherAssignmentAndReadonlyDiagnosticsKeepTheirCanonicalCode(string source, string code)
    {
        foreach (bool modules in new[] { false, true })
            Assert.Equal(code, Assert.Single(Check(source, modules, exact: true)).TsCode);
    }

    [Theory]
    [InlineData("declare const config:{value?:number};config.value=undefined;", false)]
    [InlineData("class Config{value?:number;}new Config().value=undefined;", false)]
    [InlineData("function set<T>(config:T&{value?:number}){config.value=undefined;}", false)]
    [InlineData("declare const config:{value?:number|undefined};config.value=undefined;", true)]
    [InlineData("declare const config:{value?:number};declare const value:any;config.value=value;", true)]
    [InlineData("class Config{constructor(public value?:number){}}new Config().value=undefined;", true)]
    [InlineData("declare const config:{value?:number};config.value=1;const read:number|undefined=config.value;", true)]
    public void OrdinaryOptionalWritesExplicitUndefinedAndCompatibleValuesRemainAccepted(string source, bool exact)
    {
        foreach (bool modules in new[] { false, true }) Assert.Empty(Check(source, modules, exact));
    }

    private static IReadOnlyList<SharpTS.Diagnostics.Diagnostic> Check(string source, bool modules, bool exact)
    {
        var checker = new TypeChecker(TypeCheckerOptions.Strict with { ExactOptionalPropertyTypes = exact });
        if (!modules) return checker.CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        string path = Path.Combine(Path.GetTempPath(), "exact-write-diagnostic-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source }, new TypeScriptProgramOptions { NoLib = true });
        var entry = resolver.LoadModule(path);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        return checker.GetDiagnostics();
    }
}

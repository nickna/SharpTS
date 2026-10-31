using SharpTS.Diagnostics;
using SharpTS.Modules;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class CommonJsCallableImport3223Tests
{
    [Fact]
    public void UntypedCommonJsImportEqualsCanCallTheExportedValue()
    {
        var diagnostics = Check(new Dictionary<string, string>
        {
            ["a.cjs"] = "let value=7;function outer(){function inner(){return value;}return inner;}module.exports=outer();",
            ["b.cjs"] = "let value=9;function outer(){function inner(){return value;}return inner;}module.exports=outer();",
            ["main.ts"] = "import first=require('./a.cjs');import second=require('./b.cjs');console.log(first(),second());",
        });
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData("const value: number=7;export = value;", "const text:string=value;")]
    [InlineData("function read():number{return 7;}export = read;", "const text:string=value();")]
    public void TypedExportAssignmentKeepsItsActualType(string producer, string consumer)
    {
        var diagnostics = Check(new Dictionary<string, string>
        {
            ["producer.ts"] = producer,
            ["main.ts"] = "import value=require('./producer');" + consumer,
        });
        Assert.Contains(diagnostics, diagnostic => diagnostic.TsCode == "TS2322");
    }

    [Fact]
    public void EsModuleNamespaceDoesNotBecomeCallable()
    {
        var diagnostics = Check(new Dictionary<string, string>
        {
            ["producer.ts"] = "export const value=7;",
            ["main.ts"] = "import value=require('./producer');value();",
        });
        Assert.Contains(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static IReadOnlyList<Diagnostic> Check(Dictionary<string, string> sources)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"sharpts_commonjs_callable_{Guid.NewGuid():N}");
        string entry = Path.Combine(directory, "main.ts");
        var files = sources.ToDictionary(pair => Path.Combine(directory, pair.Key), pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
        var resolver = new ModuleResolver(entry, files);
        var module = resolver.LoadModule(entry);
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        checker.CheckModules(resolver.GetModulesInOrder(module), resolver);
        return checker.GetDiagnostics();
    }
}

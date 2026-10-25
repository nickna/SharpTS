using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class IntersectionIndexDiagnosticLocationTests
{
    [Theory]
    [InlineData("'missing'", false)]
    [InlineData("'missing'", true)]
    [InlineData("0", false)]
    [InlineData("0", true)]
    public void MissingLiteralIndexPointsToTheWrittenAssignment(string key, bool modules)
    {
        string source = "function write<T>(box:T&{value:number}) {\n  box[" + key + "] = 1;\n}";
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        IReadOnlyList<SharpTS.Diagnostics.Diagnostic> diagnostics;
        if (!modules)
            diagnostics = checker.CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        else
        {
            string path = Path.Combine(Path.GetTempPath(), "intersection-index-location-" + Guid.NewGuid().ToString("N"), "main.ts");
            var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source },
                new TypeScriptProgramOptions { NoLib = true });
            var entry = resolver.LoadModule(path);
            checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
            diagnostics = checker.GetDiagnostics();
        }
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("TS7053", diagnostic.TsCode);
        Assert.Equal(2, diagnostic.Line);
    }
}

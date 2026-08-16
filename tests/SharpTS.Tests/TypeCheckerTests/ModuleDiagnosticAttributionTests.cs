using SharpTS.Diagnostics;
using SharpTS.Modules;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// A type error in a module-mode file (one containing <c>import</c>/<c>export</c>) must be reported
/// ONCE and with a source location, matching script mode (#468). Two prior defects made module mode
/// worse than script mode:
/// <list type="bullet">
///   <item>The preparatory export-collection pass (<c>CollectModuleExports</c>) fully type-checked
///   every statement and recorded its errors, then the authoritative second pass recorded them
///   again — so each error appeared twice.</item>
///   <item><c>CheckModules</c> never set the <c>_currentStatementLine</c> fallback that the script
///   path uses, so an error whose throw-site carries no line rendered with no location at all.</item>
/// </list>
/// These drive <see cref="TypeChecker.CheckModules"/> through the in-memory resolver and assert on
/// the collected diagnostics directly (the CLI just prints <c>GetDiagnostics()</c>).
/// </summary>
public class ModuleDiagnosticAttributionTests
{
    private static IReadOnlyList<Diagnostic> CheckModule(string entry, string source)
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"sharpts_moddiag_{Guid.NewGuid():N}");
        var entryPath = Path.GetFullPath(Path.Combine(baseDir, entry));
        var virtualFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [entryPath] = source,
        };

        var resolver = new ModuleResolver(entryPath, virtualFiles);
        var entryModule = resolver.LoadModule(entryPath);
        var allModules = resolver.GetModulesInOrder(entryModule);

        var checker = new TypeChecker();
        checker.CheckModules(allModules, resolver);
        return checker.GetDiagnostics();
    }

    [Fact]
    public void ExportModule_LocalTypeError_ReportedOnceWithLocation()
    {
        // The verbatim #468 repro: an `export` routes the file through module mode.
        var diags = CheckModule("main.ts", "export let z: number = 5;\nz = \"hello\";\n");

        var errors = diags.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Single(errors);
        Assert.NotNull(errors[0].Location);
        Assert.Equal(2, errors[0].Line);
    }

    [Fact]
    public void ImportModule_LocalTypeError_ReportedOnceWithLocation()
    {
        // An `import` also routes through module mode; a plain local error reproduces the same defects.
        var diags = CheckModule(
            "main.ts",
            "import { readFileSync } from \"fs\";\nlet q: number = 5;\nq = \"hello\";\n");

        var errors = diags.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Single(errors);
        Assert.NotNull(errors[0].Location);
        Assert.Equal(3, errors[0].Line);
    }
}

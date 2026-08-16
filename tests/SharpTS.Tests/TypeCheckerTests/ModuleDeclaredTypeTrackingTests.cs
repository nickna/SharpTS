using SharpTS.Diagnostics;
using SharpTS.Modules;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// <see cref="TypeChecker.CheckModules"/> must keep at least one declared-type frame on the stack
/// while checking module bodies, like <c>Check</c>/<c>CheckWithRecovery</c> do (#743). Without one,
/// <c>RecordDeclaredType</c> silently drops every binding and <c>GetDeclaredType</c> falls back to
/// the environment binding — which control-flow narrowing mutates — so a reassignment inside a
/// narrowed branch was checked against the narrowed literal type instead of the declared one
/// (#1218: every CLI import of 'url' failed with "Cannot assign type 'true' to variable 'found' of
/// type 'false'", from <c>URLSearchParams._setKey</c>). Plain functions were immune because their
/// body check pushes its own frame; class methods and module top-level code were not. These assert
/// on <c>GetDiagnostics()</c> directly because CheckModules records errors with recovery — the run
/// itself succeeds, which is exactly how the test suite missed the regression.
/// </summary>
public class ModuleDeclaredTypeTrackingTests
{
    private static IReadOnlyList<Diagnostic> CheckModule(string source)
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"sharpts_decltrack_{Guid.NewGuid():N}");
        var entryPath = Path.GetFullPath(Path.Combine(baseDir, "main.ts"));
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

    private static void AssertNoErrors(IReadOnlyList<Diagnostic> diags)
    {
        var errors = diags.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, string.Join("\n", errors.Select(e => e.ToString())));
    }

    [Fact]
    public void UrlImport_TypeChecksClean()
    {
        // The verbatim #1218 symptom: any import of 'url' pulled in URLSearchParams._setKey,
        // whose `let found = false; … found = true;` was rejected.
        AssertNoErrors(CheckModule(
            "import { parse } from 'url';\nconst p = parse('http://example.com/a?b=1');\nconsole.log(p.host);\n"));
    }

    [Fact]
    public void ModuleTopLevel_NarrowedLetReassignment_TypeChecksClean()
    {
        // Module-mode top-level facet of the same root cause: `!x` narrows x to `false`, and the
        // reassignment must still check against the declared (widened) boolean.
        AssertNoErrors(CheckModule("export {};\nlet x = false;\nif (!x) { x = true; }\n"));
    }

    [Fact]
    public void ClassMethodLocal_NarrowedLetReassignment_TypeChecksClean()
    {
        // Class-method facet, independent of the stdlib facade: method bodies don't push their own
        // declared-type frame, so the local's widened type must be recorded in the module frame.
        AssertNoErrors(CheckModule(
            """
            export class Pairs {
                private _pairs: string[][] = [];
                setKey(key: string, val: string): void {
                    let found = false;
                    const next: string[][] = [];
                    for (let i = 0; i < this._pairs.length; i++) {
                        if (this._pairs[i][0] === key) {
                            if (!found) { next.push([key, val]); found = true; }
                        } else {
                            next.push(this._pairs[i]);
                        }
                    }
                    if (!found) next.push([key, val]);
                    this._pairs = next;
                }
            }
            """));
    }
}

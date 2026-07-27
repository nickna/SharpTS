using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Tests for #216: narrowing after an early-return `||` guard
/// (`if (x == null || x.length === 0) return;` must narrow x afterward),
/// the events-module import that exposed it, and module-file attribution
/// on diagnostics raised inside module sources.
/// </summary>
public class DisjunctionEarlyReturnNarrowingTests
{
    [Theory, ModeData]
    public void DisjunctionNullGuard_EarlyReturn_NarrowsAfter(ExecutionMode mode)
    {
        var source = """
            function f(arr: string[] | null): number {
                if (arr == null || arr.length === 0) return 0;
                return arr.length;
            }
            console.log(f(["a", "b"]));
            console.log(f(null));
            console.log(f([]));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n0\n0\n", output);
    }

    [Theory, ModeData]
    public void NestedDisjunctionGuard_EarlyReturn_NarrowsAll(ExecutionMode mode)
    {
        var source = """
            function f(a: string | null, b: string | null): number {
                if (a == null || b == null || a.length === 0) return 0;
                return a.length + b.length;
            }
            console.log(f("ab", "c"));
            console.log(f(null, "c"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n0\n", output);
    }

    [Theory, ModeData]
    public void EventsModuleImport_TypeChecksAndRuns(ExecutionMode mode)
    {
        // The original #216 repro: importing EventEmitter triggered a
        // null-property-access diagnostic inside the events module source.
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EventEmitter } from "events";
                const ee: any = new EventEmitter();
                ee.on("x", () => {});
                console.log(ee.eventNames().length);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("1\n", output);
    }

    [Fact]
    public void ModuleDiagnostics_CarryFileAttribution()
    {
        // Sub-problem 2 of #216: diagnostics raised inside a module source
        // must name the file, not report a bare line number.
        var tempDir = Path.Combine(Path.GetTempPath(), $"sharpts_i216_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "main.ts"), """
                import { f } from "./dep";
                console.log(f(["a"]));
                """);
            File.WriteAllText(Path.Combine(tempDir, "dep.ts"), """
                export function f(arr: string[] | null): number {
                    return arr.length;
                }
                """);

            var entryPath = Path.Combine(tempDir, "main.ts");
            var resolver = new ModuleResolver(entryPath);
            var entryModule = resolver.LoadModule(entryPath);
            var modules = resolver.GetModulesInOrder(entryModule);

            var checker = new TypeChecker();
            checker.CheckModules(modules, resolver);

            // (The diagnostic is recorded once per checking pass — assert on
            // attribution, not cardinality.)
            var diagnostics = checker.GetDiagnostics()
                .Where(d => d.Message.Contains("length")).ToList();
            Assert.NotEmpty(diagnostics);
            Assert.All(diagnostics, d =>
            {
                Assert.NotNull(d.Location?.FilePath);
                Assert.Contains("dep.ts", d.Location!.FilePath!);
            });
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}

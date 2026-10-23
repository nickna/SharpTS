using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class AsyncSynchronousCaptureLivenessTests
{
    // The original cross-module pair stays in CrossModuleNameCollisionTests.
    // The separately owned expired-block control remains in the external seven-case proof.
    internal static IEnumerable<object[]> Cases()
    {
        yield return ["no-other-module-driver", new Dictionary<string, string>
        {
            ["lib.ts"] = """
                export async function run(): Promise<void> {
                    for (const theme of ['light', 'dark']) {
                        const driver = { text: 'local' };
                        await Promise.resolve();
                        const capture = (): void => console.log(theme + ': ' + driver.text);
                        capture();
                    }
                }
                """.ReplaceLineEndings("\n"),
            ["main.ts"] = """
                import { run } from './lib';
                run().then(() => console.log('done'));
                """.ReplaceLineEndings("\n") + "\n",
        }, "light: local\ndark: local\ndone\n"];
        yield return ["single-module-after-fulfilled", new Dictionary<string, string>
        {
            ["main.ts"] = """
                async function run(): Promise<void> {
                    for (const theme of ['light', 'dark']) {
                        const driver = { text: 'local' };
                        await Promise.resolve();
                        const capture = (): void => console.log(theme + ': ' + driver.text);
                        capture();
                    }
                }
                run();
                """.ReplaceLineEndings("\n") + "\n",
        }, "light: local\ndark: local\n"];
        yield return ["pending-retained-iterations", new Dictionary<string, string>
        {
            ["main.ts"] = """
                let release: (value: number) => void = (_value: number) => {};
                const gate = new Promise<number>(resolve => { release = resolve; });
                const reads: any[] = [];
                async function run() {
                    for (const theme of ['light', 'dark']) {
                        const driver = { text: 'local' };
                        await gate;
                        reads.push(() => theme + ': ' + driver.text);
                    }
                    console.log(reads[0](), reads[1]());
                }
                run().catch((error: any) => console.log('rejected', error.name));
                release(0);
                """.ReplaceLineEndings("\n") + "\n",
        }, "light: local dark: local\n"];
        yield return ["renamed-shadow-after-await", new Dictionary<string, string>
        {
            ["main.ts"] = """
                async function run() {
                    const value = 'outer';
                    const outer = () => value;
                    {
                        const value = 'inner';
                        await Promise.resolve();
                        const inner = () => value;
                        console.log(inner(), outer());
                    }
                    console.log(outer());
                }
                run();
                """.ReplaceLineEndings("\n") + "\n",
        }, "inner outer\nouter\n"];
        yield return ["shared-mutable-capture-control", new Dictionary<string, string>
        {
            ["main.ts"] = """
                async function run() {
                    let value = 1;
                    const read = () => value;
                    await Promise.resolve();
                    value = 2;
                    console.log(read());
                }
                run();
                """.ReplaceLineEndings("\n") + "\n",
        }, "2\n"];
    }

    public static IEnumerable<object[]> Names() => Cases().Select(row => new object[] { row[0] });

    public static IEnumerable<object[]> ModeCases() => Names().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], mode }));

    internal static (Dictionary<string, string> Files, string Expected) GetCase(string name)
    {
        var row = Cases().Single(row => (string)row[0] == name);
        return ((Dictionary<string, string>)row[1], (string)row[2]);
    }

    [Theory, MemberData(nameof(ModeCases))]
    public void SynchronousClosuresKeepTheirOrdinaryAsyncCaptureSources(
        string name, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        var (files, expected) = GetCase(name);
        AssertStrictModules(files);
        if (mode == ExecutionMode.Compiled)
        {
            // This existing module verifier filters assembly-load diagnostics.
            // Independent Saved rows below require full CLI verification without omissions.
            Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
        }
        Assert.Equal(expected, TestHarness.RunModules(files, "main.ts", mode));
    }

    internal static void AssertStrictModules(Dictionary<string, string> files)
    {
        var root = Path.Combine(Path.GetTempPath(), "async-sync-capture-" + Guid.NewGuid().ToString("N"));
        var modules = files.ToDictionary(pair => Path.GetFullPath(Path.Combine(root, pair.Key)), pair => pair.Value);
        var entryPath = Path.Combine(root, "main.ts");
        var resolver = new ModuleResolver(entryPath, modules);
        var entry = resolver.LoadModule(entryPath);
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        Assert.Empty(checker.GetDiagnostics());
    }
}

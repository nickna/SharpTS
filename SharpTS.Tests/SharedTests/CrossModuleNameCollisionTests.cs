using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regression tests for cross-module top-level name collisions. The compiler
/// previously shared a single <c>_topLevelStaticVars</c> dict across every
/// module — so a <c>const foo</c> in one module would shadow an exported
/// <c>function foo()</c> in another, even when the importer aliased the
/// named import. Any stdlib migration whose exports match common user
/// variable names (e.g. <c>os.platform</c>, <c>querystring.parse</c>) would
/// hit this.
/// </summary>
public class CrossModuleNameCollisionTests
{
    [Theory, ModeData]
    public void ExportedFunctionSurvivesConstShadowingInImporter(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["lib.ts"] = """
                export function foo(): string { return 'from-lib'; }
                """,
            ["main.ts"] = """
                import { foo as libFoo } from './lib';
                const foo = libFoo();
                console.log(foo);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("from-lib\n", output);
    }

    [Theory, ModeData]
    public void LibModuleReferencesOwnExportedFunctionDespiteImporterConflict(ExecutionMode mode)
    {
        // lib.ts should see its OWN `foo` via hoisting even when main.ts has
        // an unrelated `const foo`. The test passes only when the scoping
        // fix correctly isolates each module's top-level bindings.
        var files = new Dictionary<string, string>
        {
            ["lib.ts"] = """
                export function foo(): string { return 'ok'; }
                console.log('lib:', typeof foo);
                """,
            ["main.ts"] = """
                import { foo as libFoo } from './lib';
                const foo = libFoo();
                console.log('main:', foo);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("lib: function\nmain: ok\n", output);
    }

    [Theory, ModeData]
    public void TwoModulesDeclaringSameConstName(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["a.ts"] = """
                const x = 'from-a';
                export function getX(): string { return x; }
                """,
            ["b.ts"] = """
                const x = 'from-b';
                export function getX(): string { return x; }
                """,
            ["main.ts"] = """
                import { getX as getA } from './a';
                import { getX as getB } from './b';
                console.log(getA(), getB());
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("from-a from-b\n", output);
    }

    // Regression for #46: os.ts and process.ts both alias their respective
    // primitives as `__platform` / `__arch`. The compiler's binding dictionary
    // was a flat map keyed by local name, so the second module to load
    // clobbered the first's binding. With `os` imported before `process`,
    // os.ts's `__platform()` call was mis-routed to process's binding.
    [Theory, ModeData]
    public void OsBeforeProcess_PlatformAndArchResolveCorrectly(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as os from 'os';
                import process from 'process';
                const p = os.platform();
                const a = os.arch();
                console.log('platform-len:', p.length > 0);
                console.log('arch-len:', a.length > 0);
                console.log('pid-typeof:', typeof process.pid);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("platform-len: true", output);
        Assert.Contains("arch-len: true", output);
        Assert.Contains("pid-typeof: number", output);
    }

    [Theory, ModeData]
    public void ProcessBeforeOs_PlatformAndArchResolveCorrectly(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import process from 'process';
                import * as os from 'os';
                const p = os.platform();
                const a = os.arch();
                console.log('platform-len:', p.length > 0);
                console.log('arch-len:', a.length > 0);
                console.log('pid-typeof:', typeof process.pid);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("platform-len: true", output);
        Assert.Contains("arch-len: true", output);
        Assert.Contains("pid-typeof: number", output);
    }

    // ---- #418: same-named async / generator / async-generator functions across modules ----
    // Sync top-level functions register their stub/builders under the module-qualified name
    // (`$M_<module>_<name>`), so two modules never collide. Pre-#418 the state-machine flavors
    // (async / generator / async-generator) registered every piece of their state — stub
    // MethodBuilder, state-machine builder, AST node, _functionDefinitionModule — under the
    // SIMPLE name. A second `async function dup` in another module overwrote the first's stub,
    // orphaning it (no body emitted) and crashing emission with "The invoked member is not
    // supported before the type is created." #418 module-qualifies these registries to match
    // sync. These run in BOTH modes: compiled pins the fix, interpreted guards the (already
    // correct) reference behavior.

    [Theory, ModeData]
    public void TwoModulesDeclaringSameAsyncFunctionName(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["a.ts"] = """
                export async function dup(): Promise<string> { return 'A'; }
                """,
            ["b.ts"] = """
                export async function dup(): Promise<string> { return 'B'; }
                """,
            ["main.ts"] = """
                import { dup as dupA } from './a';
                import { dup as dupB } from './b';
                async function main() { console.log(await dupA(), await dupB()); }
                main();
                """
        };

        Assert.Equal("A B\n", TestHarness.RunModules(files, "main.ts", mode));
    }

    [Theory, ModeData]
    public void TwoModulesDeclaringSameGeneratorFunctionName(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["a.ts"] = """
                export function* dup(): Generator<string> { yield 'A1'; yield 'A2'; }
                """,
            ["b.ts"] = """
                export function* dup(): Generator<string> { yield 'B1'; yield 'B2'; }
                """,
            ["main.ts"] = """
                import { dup as dupA } from './a';
                import { dup as dupB } from './b';
                console.log([...dupA()].join(','));
                console.log([...dupB()].join(','));
                """
        };

        Assert.Equal("A1,A2\nB1,B2\n", TestHarness.RunModules(files, "main.ts", mode));
    }

    [Theory, ModeData]
    public void TwoModulesDeclaringSameAsyncGeneratorFunctionName(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["a.ts"] = """
                export async function* dup(): AsyncGenerator<string> { yield 'A1'; yield 'A2'; }
                """,
            ["b.ts"] = """
                export async function* dup(): AsyncGenerator<string> { yield 'B1'; yield 'B2'; }
                """,
            ["main.ts"] = """
                import { dup as dupA } from './a';
                import { dup as dupB } from './b';
                async function main() {
                    for await (const v of dupA()) console.log(v);
                    for await (const v of dupB()) console.log(v);
                }
                main();
                """
        };

        Assert.Equal("A1\nA2\nB1\nB2\n", TestHarness.RunModules(files, "main.ts", mode));
    }

    [Theory, ModeData]
    public void TwoModulesDefaultExportingSameAsyncFunctionName(ExecutionMode mode)
    {
        // The `export default` store branch is distinct from named exports; cover the
        // collision there too.
        var files = new Dictionary<string, string>
        {
            ["a.ts"] = """
                export default async function dup(): Promise<string> { return 'DA'; }
                """,
            ["b.ts"] = """
                export default async function dup(): Promise<string> { return 'DB'; }
                """,
            ["main.ts"] = """
                import dupA from './a';
                import dupB from './b';
                async function main() { console.log(await dupA(), await dupB()); }
                main();
                """
        };

        Assert.Equal("DA DB\n", TestHarness.RunModules(files, "main.ts", mode));
    }

    [Theory, ModeData]
    public void TwoModulesMixingAsyncAndSyncSameFunctionName(ExecutionMode mode)
    {
        // One module's `dup` is async (qualified state-machine stub), the other's is sync
        // (qualified plain stub). Each must resolve to its own module's definition.
        var files = new Dictionary<string, string>
        {
            ["a.ts"] = """
                export async function dup(): Promise<string> { return 'async'; }
                """,
            ["b.ts"] = """
                export function dup(): string { return 'sync'; }
                """,
            ["main.ts"] = """
                import { dup as dupA } from './a';
                import { dup as dupB } from './b';
                async function main() { console.log(await dupA(), dupB()); }
                main();
                """
        };

        Assert.Equal("async sync\n", TestHarness.RunModules(files, "main.ts", mode));
    }

    [Theory, ModeData]
    public void TwoModulesSameAsyncFunctionNameEachCapturingOwnModuleConst(ExecutionMode mode)
    {
        // Each colliding async function captures a module-level const of the same name. Beyond
        // disambiguating the stub, emission must restore the correct module context per function
        // so each reads its OWN module's binding (not the other's).
        var files = new Dictionary<string, string>
        {
            ["a.ts"] = """
                const tag = 'CA';
                export async function dup(): Promise<string> { return tag; }
                """,
            ["b.ts"] = """
                const tag = 'CB';
                export async function dup(): Promise<string> { return tag; }
                """,
            ["main.ts"] = """
                import { dup as dupA } from './a';
                import { dup as dupB } from './b';
                async function main() { console.log(await dupA(), await dupB()); }
                main();
                """
        };

        Assert.Equal("CA CB\n", TestHarness.RunModules(files, "main.ts", mode));
    }
}

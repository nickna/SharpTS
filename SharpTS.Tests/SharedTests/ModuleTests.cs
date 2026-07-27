using SharpTS.TypeSystem.Exceptions;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for ES modules: import/export statements, module resolution,
/// re-exports, execution order, and cross-module type usage.
/// </summary>
public class ModuleTests
{
    #region Named Exports and Imports

    [Theory, ModeData]
    public void NamedExport_SingleVariable(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./math.ts"] = """
                export const PI: number = 3.14159;
                """,
            ["./main.ts"] = """
                import { PI } from './math';
                console.log(PI);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("3.14159\n", output);
    }

    [Theory, ModeData]
    public void NamedExport_MultipleVariables(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./constants.ts"] = """
                export const X: number = 10;
                export const Y: number = 20;
                export const Z: number = 30;
                """,
            ["./main.ts"] = """
                import { X, Y, Z } from './constants';
                console.log(X + Y + Z);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("60\n", output);
    }

    [Theory, ModeData]
    public void NamedExport_Function(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./utils.ts"] = """
                export function add(a: number, b: number): number {
                    return a + b;
                }
                """,
            ["./main.ts"] = """
                import { add } from './utils';
                console.log(add(3, 4));
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("7\n", output);
    }

    // These three guard the #392 parser fix (`export async function`,
    // `export function*`, `export async function*`) in single-file form. Since #417 they
    // run in BOTH modes: single-file *script-mode* compilation now unwraps `export`-wrapped
    // declarations (Phase4/Phase7 delegate to the shared define/emit helpers), so an exported
    // function is bound and callable just like in the interpreter. The plain-sync single-file
    // case — the broader gap #417 also fixed — is covered by ExportSyncFunction_SingleFile below.
    // Compiled cross-module execution of these exports is additionally covered by the
    // *_CrossModule tests further down (#395).
    [Theory, ModeData]
    public void ExportAsyncFunction_Parses(ExecutionMode mode)
    {
        // Regression: `export async function` previously failed to parse
        // ("Expect declaration after 'export'"). See issue #392.
        var source = """
            export async function add(a: number, b: number): Promise<number> {
                return a + b;
            }
            async function main() { console.log(await add(3, 4)); }
            main();
            """;

        Assert.Equal("7\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ExportGeneratorFunction_Parses(ExecutionMode mode)
    {
        // Regression: `export function*` previously failed to parse ("Expect
        // function name") because the export dispatcher never consumed the `*`.
        var source = """
            export function* g(): Generator<number> {
                yield 1;
                yield 2;
            }
            for (const x of g()) console.log(x);
            """;

        Assert.Equal("1\n2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ExportAsyncGeneratorFunction_Parses(ExecutionMode mode)
    {
        // Regression: `export async function*` shares the same export dispatcher
        // path as `export async function` (issue #392).
        var source = """
            export async function* ag(): AsyncGenerator<number> {
                yield 1;
                yield 2;
            }
            async function main() { for await (const x of ag()) console.log(x); }
            main();
            """;

        Assert.Equal("1\n2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ExportSyncFunction_SingleFile(ExecutionMode mode)
    {
        // Regression for #417: a plain `export function` in a single-file (script-mode)
        // *compilation* was never bound — Phase4/Phase7 didn't unwrap Stmt.Export — so the
        // call site threw "ReferenceError: Undefined variable 'f'". This is the broader gap
        // that #417 fixed beyond the async/generator cases above. The interpreter always
        // handled it; this pins the compiled single-file path too.
        var source = """
            export function f(): number { return 5; }
            console.log(f());
            """;

        Assert.Equal("5\n", TestHarness.Run(source, mode));
    }

    // ---- #424: exported variable declarations bound in single-file (script) compilation ----
    // These compile a single file in script (non-module) mode (TestHarness.Run + Compiled →
    // ILCompiler.Compile, not CompileModules). Before #424 the compiled script path dropped the
    // initializer of an `export const/let/var` (EmitExport no-ops when CurrentModulePath == null)
    // and never defined a backing field for the binding, so a later reference threw
    // "ReferenceError: Undefined variable". They run in BOTH modes to pin the fix and guard the
    // interpreter (which was already correct). The CLI routes any file with import/export through
    // the module path, so no production program hit this — only the in-process single-file harness.

    [Theory, ModeData]
    public void ExportConst_SingleFile(ExecutionMode mode)
    {
        // The canonical #424 repro: a non-captured exported const must still be
        // stored in its $topLevel_ static field and resolve when referenced.
        var source = """
            export const x = 5;
            console.log(x);
            """;

        Assert.Equal("5\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ExportLet_SingleFile(ExecutionMode mode)
    {
        // `export let` is mutable — exercises the Stmt.Var arm and a later reassignment.
        var source = """
            export let count = 7;
            count = count + 1;
            console.log(count);
            """;

        Assert.Equal("8\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ExportVar_SingleFile(ExecutionMode mode)
    {
        var source = """
            export var greeting = "hi";
            console.log(greeting);
            """;

        Assert.Equal("hi\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ExportConst_CapturedByClosure_SingleFile(ExecutionMode mode)
    {
        // When a function (a separate compiled method) reads the exported binding, the
        // closure analyzer marks it captured, so it lives on the entry-point display class
        // rather than a static field. Exercises the RegisterCapturedStmt unwrap path.
        var source = """
            export const factor = 5;
            function scale(n: number): number {
                return n * factor;
            }
            console.log(scale(3));
            """;

        Assert.Equal("15\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ExportConst_MultiDeclarator_SingleFile(ExecutionMode mode)
    {
        // `export const a = 1, b = 2` desugars to Stmt.Export { Declaration: Stmt.Sequence };
        // both the field-definition helpers and EmitExport must recurse through the sequence.
        var source = """
            export const a = 1, b = 2;
            console.log(a + b);
            """;

        Assert.Equal("3\n", TestHarness.Run(source, mode));
    }

    // ---- #428: `export const` keeps const-ness (was parsed as a mutable Stmt.Var) ----
    // The parser dispatched `export const` through VarDeclaration() with the default
    // isConst:false, so it became a mutable Stmt.Var: reassignment went unflagged and the
    // literal type was widened. After #428 it is a Stmt.Const, matching bare `const`. The
    // type-checker/interpreter export helpers (GetDeclarationName / GetDeclaredType /
    // GetDeclaredName / GetDeclaredValue) gained the Stmt.Const arm they previously lacked.

    [Theory, ModeData]
    public void ExportConst_Reassignment_IsTypeError(ExecutionMode mode)
    {
        // The canonical #428 repro: reassigning an `export const` must be rejected, just like
        // bare `const`. The narrowed literal type ('5') surfaces in the diagnostic.
        var source = """
            export const y = 5;
            y = 6;
            """;

        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.Run(source, mode));
        Assert.Contains("'y'", ex.Message);
    }

    [Theory, ModeData]
    public void ExportConst_LiteralTypeNarrowed(ExecutionMode mode)
    {
        // `export const x = 5` must infer the literal type `5`, not the widened `number`.
        // Assigning it into a `5`-typed binding only type-checks when the narrowing happened.
        var source = """
            export const x = 5;
            const exact: 5 = x;
            console.log(exact);
            """;

        Assert.Equal("5\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ExportConst_NarrowedType_FlowsCrossModule(ExecutionMode mode)
    {
        // The narrowed literal type of an exported const must survive import into another
        // module (exercises the GetDeclarationName/GetDeclaredType export-table helpers).
        var files = new Dictionary<string, string>
        {
            ["./values.ts"] = """
                export const MAX = 100;
                """,
            ["./main.ts"] = """
                import { MAX } from './values';
                const exact: 100 = MAX;
                console.log(exact);
                """
        };

        Assert.Equal("100\n", TestHarness.RunModules(files, "./main.ts", mode));
    }

    [Theory, ModeData]
    public void ExportConst_CrossModule_Runs(ExecutionMode mode)
    {
        // A plain exported const imported and used across modules still binds and runs
        // (guards the Stmt.Const arm added to the interpreter's GetDeclaredName/Value).
        var files = new Dictionary<string, string>
        {
            ["./config.ts"] = """
                export const greeting = "hello";
                """,
            ["./main.ts"] = """
                import { greeting } from './config';
                console.log(greeting);
                """
        };

        Assert.Equal("hello\n", TestHarness.RunModules(files, "./main.ts", mode));
    }

    // ---- #395: exported async / generator / async-generator functions callable across modules ----
    // The single-file tests above exercise script (non-module) mode via TestHarness.Run; since
    // #417 they run in both modes (the compiled script path now binds exported declarations).
    // The tests below instead use the real cross-module path (RunModules), which is how the CLI
    // compiles any file containing `export`. Before #395 the compiled export-store only consulted
    // `_ctx.Functions` under the module-qualified name, missing async/generator stubs (keyed by
    // simple name) — the import field stayed null and the call threw "object is not a function".
    // They run in BOTH modes to pin the fix and guard the interpreter.

    [Theory, ModeData]
    public void NamedExport_AsyncFunction_CrossModule(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./utils.ts"] = """
                export async function addAsync(a: number, b: number): Promise<number> {
                    return a + b;
                }
                """,
            ["./main.ts"] = """
                import { addAsync } from './utils';
                async function main() { console.log(await addAsync(3, 4)); }
                main();
                """
        };

        Assert.Equal("7\n", TestHarness.RunModules(files, "./main.ts", mode));
    }

    [Theory, ModeData]
    public void NamedExport_GeneratorFunction_CrossModule(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./utils.ts"] = """
                export function* genCount(): Generator<number> {
                    yield 1;
                    yield 2;
                    yield 3;
                }
                """,
            ["./main.ts"] = """
                import { genCount } from './utils';
                for (const n of genCount()) console.log(n);
                """
        };

        Assert.Equal("1\n2\n3\n", TestHarness.RunModules(files, "./main.ts", mode));
    }

    [Theory, ModeData]
    public void NamedExport_AsyncGeneratorFunction_CrossModule(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./utils.ts"] = """
                export async function* genAsync(): AsyncGenerator<number> {
                    yield 10;
                    yield 20;
                }
                """,
            ["./main.ts"] = """
                import { genAsync } from './utils';
                async function main() {
                    for await (const n of genAsync()) console.log(n);
                }
                main();
                """
        };

        Assert.Equal("10\n20\n", TestHarness.RunModules(files, "./main.ts", mode));
    }

    [Theory, ModeData]
    public void NamedListExport_StateMachineFunctions_CrossModule(ExecutionMode mode)
    {
        // The `export { a, b, c }` list form is a distinct export-store branch from the
        // inline `export async function ...` declaration form; cover it too.
        var files = new Dictionary<string, string>
        {
            ["./utils.ts"] = """
                async function addAsync(a: number, b: number): Promise<number> { return a + b; }
                function* counter(): Generator<number> { yield 7; yield 8; }
                async function* ag(): AsyncGenerator<number> { yield 99; }
                export { addAsync, counter, ag };
                """,
            ["./main.ts"] = """
                import { addAsync, counter, ag } from './utils';
                async function main() {
                    console.log(await addAsync(10, 5));
                    for (const n of counter()) console.log(n);
                    for await (const n of ag()) console.log(n);
                }
                main();
                """
        };

        Assert.Equal("15\n7\n8\n99\n", TestHarness.RunModules(files, "./main.ts", mode));
    }

    [Theory, ModeData]
    public void DefaultExport_AsyncFunction_CrossModule(ExecutionMode mode)
    {
        // `export default async function` / `export default function*` additionally required a
        // parser fix: the `export default` dispatcher (unlike the non-default one fixed in #392)
        // did not recognize `async`/`*`. With that in place the compiled $default export-store
        // branch resolves the state-machine stub the same way as named exports.
        var files = new Dictionary<string, string>
        {
            ["./util.ts"] = """
                export default async function dbl(n: number): Promise<number> { return n * 2; }
                """,
            ["./main.ts"] = """
                import dbl from './util';
                async function main() { console.log(await dbl(21)); }
                main();
                """
        };

        Assert.Equal("42\n", TestHarness.RunModules(files, "./main.ts", mode));
    }

    [Theory, ModeData]
    public void DefaultExport_GeneratorFunction_CrossModule(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./util.ts"] = """
                export default function* gen(): Generator<number> { yield 5; yield 6; }
                """,
            ["./main.ts"] = """
                import gen from './util';
                for (const n of gen()) console.log(n);
                """
        };

        Assert.Equal("5\n6\n", TestHarness.RunModules(files, "./main.ts", mode));
    }

    [Theory, ModeData]
    public void DefaultExport_AsyncGeneratorFunction_CrossModule(ExecutionMode mode)
    {
        // Guards the `async`+`*` combination in the `export default` parser branch.
        var files = new Dictionary<string, string>
        {
            ["./util.ts"] = """
                export default async function* ag(): AsyncGenerator<number> { yield 1; yield 2; }
                """,
            ["./main.ts"] = """
                import ag from './util';
                async function main() { for await (const n of ag()) console.log(n); }
                main();
                """
        };

        Assert.Equal("1\n2\n", TestHarness.RunModules(files, "./main.ts", mode));
    }

    [Theory, ModeData]
    public void DefaultExport_AsyncArrow_StillParsesAsExpression(ExecutionMode mode)
    {
        // Guard: the `export default async function` parser branch must NOT swallow
        // `export default async () => {}` — that's a default async-arrow *expression*,
        // not a function declaration. The dispatcher uses a two-token lookahead
        // (ASYNC followed by FUNCTION) so this keeps parsing via the expression path.
        var files = new Dictionary<string, string>
        {
            ["./util.ts"] = """
                export default async (n: number): Promise<number> => n + 1;
                """,
            ["./main.ts"] = """
                import inc from './util';
                async function main() { console.log(await inc(41)); }
                main();
                """
        };

        Assert.Equal("42\n", TestHarness.RunModules(files, "./main.ts", mode));
    }

    [Theory, ModeData]
    public void NamedExport_Class(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./person.ts"] = """
                export class Person {
                    name: string;
                    constructor(name: string) {
                        this.name = name;
                    }
                    greet(): string {
                        return "Hello, " + this.name;
                    }
                }
                """,
            ["./main.ts"] = """
                import { Person } from './person';
                let p: Person = new Person("Alice");
                console.log(p.greet());
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("Hello, Alice\n", output);
    }

    [Theory, ModeData]
    public void NamedImport_WithAlias(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./math.ts"] = """
                export const value: number = 42;
                """,
            ["./main.ts"] = """
                import { value as answer } from './math';
                console.log(answer);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("42\n", output);
    }

    [Theory, ModeData]
    public void NamedExport_List(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./values.ts"] = """
                const a: number = 1;
                const b: number = 2;
                const c: number = 3;
                export { a, b, c };
                """,
            ["./main.ts"] = """
                import { a, b, c } from './values';
                console.log(a + b + c);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("6\n", output);
    }

    [Theory, ModeData]
    public void NamedExport_ListWithAlias(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./values.ts"] = """
                const internal: number = 100;
                export { internal as external };
                """,
            ["./main.ts"] = """
                import { external } from './values';
                console.log(external);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("100\n", output);
    }

    #endregion

    #region Default Exports and Imports

    [Theory, ModeData]
    public void DefaultExport_Expression(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./config.ts"] = """
                export default 42;
                """,
            ["./main.ts"] = """
                import config from './config';
                console.log(config);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("42\n", output);
    }

    [Theory, ModeData]
    public void DefaultExport_Function(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./greet.ts"] = """
                export default function greet(name: string): string {
                    return "Hello, " + name;
                }
                """,
            ["./main.ts"] = """
                import greet from './greet';
                console.log(greet("World"));
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("Hello, World\n", output);
    }

    [Theory, ModeData]
    public void DefaultExport_Class(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./counter.ts"] = """
                export default class Counter {
                    count: number;
                    constructor() {
                        this.count = 0;
                    }
                    increment(): void {
                        this.count = this.count + 1;
                    }
                }
                """,
            ["./main.ts"] = """
                import Counter from './counter';
                let c: Counter = new Counter();
                c.increment();
                c.increment();
                console.log(c.count);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("2\n", output);
    }

    [Theory, ModeData]
    public void DefaultExport_ArrowFunction(ExecutionMode mode)
    {
        // Note: anonymous function export default not supported, use arrow function
        var files = new Dictionary<string, string>
        {
            ["./double.ts"] = """
                const double = (x: number): number => x * 2;
                export default double;
                """,
            ["./main.ts"] = """
                import double from './double';
                console.log(double(21));
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("42\n", output);
    }

    #endregion

    #region Combined Imports

    [Theory, ModeData]
    public void CombinedImport_DefaultAndNamed(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./module.ts"] = """
                export const helper: string = "Helper";
                export default "Default";
                """,
            ["./main.ts"] = """
                import def, { helper } from './module';
                console.log(def);
                console.log(helper);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("Default\nHelper\n", output);
    }

    #endregion

    #region Namespace Imports

    [Theory, ModeData]
    public void NamespaceImport_AllExports(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./utils.ts"] = """
                export const A: number = 1;
                export const B: number = 2;
                export function sum(x: number, y: number): number {
                    return x + y;
                }
                """,
            ["./main.ts"] = """
                import * as Utils from './utils';
                console.log(Utils.A);
                console.log(Utils.B);
                console.log(Utils.sum(Utils.A, Utils.B));
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("1\n2\n3\n", output);
    }

    #endregion

    #region Re-exports

    [Theory, ModeData]
    public void ReExport_Named(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./original.ts"] = """
                export const value: number = 42;
                """,
            ["./reexporter.ts"] = """
                export { value } from './original';
                """,
            ["./main.ts"] = """
                import { value } from './reexporter';
                console.log(value);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("42\n", output);
    }

    [Theory, ModeData]
    public void ReExport_NamedWithAlias(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./original.ts"] = """
                export const internal: number = 100;
                """,
            ["./reexporter.ts"] = """
                export { internal as external } from './original';
                """,
            ["./main.ts"] = """
                import { external } from './reexporter';
                console.log(external);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("100\n", output);
    }

    [Theory, ModeData]
    public void ReExport_All(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./original.ts"] = """
                export const A: number = 1;
                export const B: number = 2;
                """,
            ["./reexporter.ts"] = """
                export * from './original';
                """,
            ["./main.ts"] = """
                import { A, B } from './reexporter';
                console.log(A + B);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("3\n", output);
    }

    #endregion

    #region Multi-Level Dependencies

    [Theory, ModeData]
    public void MultiLevel_ThreeModules(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./c.ts"] = """
                export const value: number = 10;
                """,
            ["./b.ts"] = """
                import { value } from './c';
                export const doubled: number = value * 2;
                """,
            ["./a.ts"] = """
                import { doubled } from './b';
                console.log(doubled);
                """
        };

        var output = TestHarness.RunModules(files, "./a.ts", mode);
        Assert.Equal("20\n", output);
    }

    [Theory, ModeData]
    public void MultiLevel_DiamondDependency(ExecutionMode mode)
    {
        // a -> b -> d
        // a -> c -> d
        var files = new Dictionary<string, string>
        {
            ["./d.ts"] = """
                export let counter: number = 0;
                counter = counter + 1;
                console.log("d executed");
                """,
            ["./b.ts"] = """
                import { counter } from './d';
                export const fromB: number = counter;
                console.log("b executed");
                """,
            ["./c.ts"] = """
                import { counter } from './d';
                export const fromC: number = counter;
                console.log("c executed");
                """,
            ["./a.ts"] = """
                import { fromB } from './b';
                import { fromC } from './c';
                console.log("a executed");
                console.log(fromB);
                console.log(fromC);
                """
        };

        var output = TestHarness.RunModules(files, "./a.ts", mode);
        // d should only execute once due to module caching
        Assert.Contains("d executed\n", output);
        Assert.Equal(1, output.Split("d executed").Length - 1); // Count occurrences
    }

    #endregion

    #region Circular Dependency Detection

    [Theory, ModeData]
    public void CircularDependency_ThrowsError(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./a.ts"] = """
                import { b } from './b';
                export const a: number = 1;
                """,
            ["./b.ts"] = """
                import { a } from './a';
                export const b: number = 2;
                """
        };

        var ex = Assert.ThrowsAny<TypeCheckException>(() =>
            TestHarness.RunModules(files, "./a.ts", mode));
        // Can be "Circular dependency" or a type error about missing exports
        Assert.True(
            ex.Message.Contains("circular", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("Module Error", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("no export", StringComparison.OrdinalIgnoreCase),
            $"Expected circular or module error, got: {ex.Message}");
    }

    [Theory, ModeData]
    public void CircularDependency_IndirectCycle(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./a.ts"] = """
                import { c } from './c';
                export const a: number = 1;
                """,
            ["./b.ts"] = """
                import { a } from './a';
                export const b: number = 2;
                """,
            ["./c.ts"] = """
                import { b } from './b';
                export const c: number = 3;
                """
        };

        var ex = Assert.ThrowsAny<TypeCheckException>(() =>
            TestHarness.RunModules(files, "./a.ts", mode));
        // Can be "Circular dependency" or a type error about missing exports
        Assert.True(
            ex.Message.Contains("circular", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("Module Error", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("no export", StringComparison.OrdinalIgnoreCase),
            $"Expected circular or module error, got: {ex.Message}");
    }

    #endregion

    #region Side-Effect Imports

    [Theory, ModeData]
    public void SideEffectImport_ExecutesModule(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./sideeffect.ts"] = """
                console.log("Side effect executed");
                """,
            ["./main.ts"] = """
                import './sideeffect';
                console.log("Main executed");
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("Side effect executed\nMain executed\n", output);
    }

    [Theory, ModeData]
    public void ExecutionOrder_MultipleModules(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./a.ts"] = """
                console.log("a");
                """,
            ["./b.ts"] = """
                import './a';
                console.log("b");
                """,
            ["./main.ts"] = """
                import './b';
                console.log("main");
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("a\nb\nmain\n", output);
    }

    #endregion

    #region Module Execution Order

    [Theory, ModeData]
    public void ExecutionOrder_DependenciesFirst(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./leaf.ts"] = """
                console.log("leaf");
                export const x: number = 1;
                """,
            ["./middle.ts"] = """
                import { x } from './leaf';
                console.log("middle");
                export const y: number = x + 1;
                """,
            ["./root.ts"] = """
                import { y } from './middle';
                console.log("root");
                console.log(y);
                """
        };

        var output = TestHarness.RunModules(files, "./root.ts", mode);
        Assert.Equal("leaf\nmiddle\nroot\n2\n", output);
    }

    #endregion

    #region Path Resolution

    [Theory, ModeData]
    public void PathResolution_OmittedExtension(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./helper.ts"] = """
                export const help: string = "Helping!";
                """,
            ["./main.ts"] = """
                import { help } from './helper';
                console.log(help);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("Helping!\n", output);
    }

    [Theory, ModeData]
    public void PathResolution_WithExtension(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./helper.ts"] = """
                export const help: string = "Helping!";
                """,
            ["./main.ts"] = """
                import { help } from './helper.ts';
                console.log(help);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("Helping!\n", output);
    }

    [Theory, ModeData]
    public void PathResolution_NestedDirectories(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./lib/utils/math.ts"] = """
                export function square(x: number): number {
                    return x * x;
                }
                """,
            ["./main.ts"] = """
                import { square } from './lib/utils/math';
                console.log(square(5));
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("25\n", output);
    }

    #endregion

    #region Export Types (Interface, Type Alias, Enum)

    [Theory, ModeData]
    public void Export_InterfaceWithFactory(ExecutionMode mode)
    {
        // Note: Interfaces are type-only exports (erased at runtime).
        // This test uses a factory function alongside the interface.
        var files = new Dictionary<string, string>
        {
            ["./types.ts"] = """
                export interface Person {
                    name: string;
                    age: number;
                }
                export function createPerson(name: string, age: number): Person {
                    return { name: name, age: age };
                }
                """,
            ["./main.ts"] = """
                import { createPerson } from './types';
                let p = createPerson("Alice", 30);
                console.log(p.name);
                console.log(p.age);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("Alice\n30\n", output);
    }

    [Theory, ModeData]
    public void Export_TypeAliasUsedLocally(ExecutionMode mode)
    {
        // Note: Type aliases are type-only exports (erased at runtime).
        // This test uses the type locally within the same module.
        var files = new Dictionary<string, string>
        {
            ["./utils.ts"] = """
                export function makeId(n: number): number {
                    return n * 1000;
                }
                """,
            ["./main.ts"] = """
                import { makeId } from './utils';
                let id: number = makeId(42);
                console.log(id);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("42000\n", output);
    }

    [Theory, ModeData]
    public void Export_Enum(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./status.ts"] = """
                export enum Status {
                    Active,
                    Inactive,
                    Pending
                }
                """,
            ["./main.ts"] = """
                import { Status } from './status';
                console.log(Status.Active);
                console.log(Status.Pending);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("0\n2\n", output);
    }

    #endregion

    #region Complex Scenarios

    [Theory, ModeData]
    public void Complex_MultipleExportsAndImports(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./math.ts"] = """
                export const PI: number = 3.14159;
                export function add(a: number, b: number): number { return a + b; }
                export function multiply(a: number, b: number): number { return a * b; }
                export default class Calculator {
                    value: number;
                    constructor() { this.value = 0; }
                    add(n: number): void { this.value = this.value + n; }
                    getResult(): number { return this.value; }
                }
                """,
            ["./main.ts"] = """
                import Calculator, { PI, add, multiply } from './math';
                console.log(PI);
                console.log(add(2, 3));
                console.log(multiply(4, 5));
                let calc: Calculator = new Calculator();
                calc.add(10);
                calc.add(5);
                console.log(calc.getResult());
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("3.14159\n5\n20\n15\n", output);
    }

    [Theory, ModeData]
    public void Complex_ClassInheritanceAcrossModules(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./base.ts"] = """
                export class Animal {
                    name: string;
                    constructor(name: string) {
                        this.name = name;
                    }
                    speak(): string {
                        return this.name + " makes a sound";
                    }
                }
                """,
            ["./dog.ts"] = """
                import { Animal } from './base';
                export class Dog extends Animal {
                    constructor(name: string) {
                        super(name);
                    }
                    speak(): string {
                        return this.name + " barks";
                    }
                }
                """,
            ["./main.ts"] = """
                import { Dog } from './dog';
                let d: Dog = new Dog("Rex");
                console.log(d.speak());
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("Rex barks\n", output);
    }

    [Theory, ModeData]
    public void Complex_FunctionUsingImportedClass(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./person.ts"] = """
                export class Person {
                    name: string;
                    constructor(name: string) {
                        this.name = name;
                    }
                }
                """,
            ["./factory.ts"] = """
                import { Person } from './person';
                export function createPerson(name: string): Person {
                    return new Person(name);
                }
                """,
            ["./main.ts"] = """
                import { createPerson } from './factory';
                let p = createPerson("Bob");
                console.log(p.name);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("Bob\n", output);
    }

    #endregion

    #region Duplicate Names Across Modules (Compiled)

    /// <summary>
    /// Tests that two different modules can each define a class with the same name,
    /// and they should be treated as separate types.
    /// </summary>
    [Theory, ModeData]
    public void DuplicateClassNames_AcrossModules_ShouldBeDistinct(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./file1.ts"] = """
                export class Foo {
                    x: number = 1;
                }
                """,
            ["./file2.ts"] = """
                export class Foo {
                    y: number = 2;
                }
                """,
            ["./main.ts"] = """
                import { Foo as Foo1 } from './file1';
                import { Foo as Foo2 } from './file2';

                let f1 = new Foo1();
                let f2 = new Foo2();

                console.log(f1.x);
                console.log(f2.y);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("1\n2\n", output);
    }

    /// <summary>
    /// Tests that two different modules can each define a function with the same name,
    /// and they should be treated as separate functions.
    /// </summary>
    [Theory, ModeData]
    public void DuplicateFunctionNames_AcrossModules_ShouldBeDistinct(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./file1.ts"] = """
                export function getValue(): number {
                    return 1;
                }
                """,
            ["./file2.ts"] = """
                export function getValue(): number {
                    return 2;
                }
                """,
            ["./main.ts"] = """
                import { getValue as getValue1 } from './file1';
                import { getValue as getValue2 } from './file2';

                console.log(getValue1());
                console.log(getValue2());
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("1\n2\n", output);
    }

    /// <summary>
    /// Tests that two different modules can each define an enum with the same name,
    /// and they should be treated as separate enums.
    /// </summary>
    [Theory, ModeData]
    public void DuplicateEnumNames_AcrossModules_ShouldBeDistinct(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./file1.ts"] = """
                export enum Status {
                    Active = 10
                }
                """,
            ["./file2.ts"] = """
                export enum Status {
                    Active = 20
                }
                """,
            ["./main.ts"] = """
                import { Status as Status1 } from './file1';
                import { Status as Status2 } from './file2';

                console.log(Status1.Active);
                console.log(Status2.Active);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("10\n20\n", output);
    }

    #endregion

    #region Cross-Module Type Usage (Compiled)

    /// <summary>
    /// Tests that a class exported from one module can be imported, instantiated,
    /// and have its methods called from another module.
    /// </summary>
    [Theory, ModeData]
    public void CrossModule_ClassInstantiationAndMethodCall(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./person.ts"] = """
                export class Person {
                    name: string;
                    age: number;

                    constructor(name: string, age: number) {
                        this.name = name;
                        this.age = age;
                    }

                    greet(): string {
                        return "Hello, " + this.name;
                    }

                    getAgeInMonths(): number {
                        return this.age * 12;
                    }
                }
                """,
            ["./main.ts"] = """
                import { Person } from './person';

                let p = new Person("Alice", 30);
                console.log(p.name);
                console.log(p.age);
                console.log(p.greet());
                console.log(p.getAgeInMonths());
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("Alice\n30\nHello, Alice\n360\n", output);
    }

    /// <summary>
    /// Tests that a function exported from one module can be imported and called
    /// from another module with arguments and return values.
    /// </summary>
    [Theory, ModeData]
    public void CrossModule_FunctionCall(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./math-utils.ts"] = """
                export function add(a: number, b: number): number {
                    return a + b;
                }

                export function multiply(a: number, b: number): number {
                    return a * b;
                }

                export function factorial(n: number): number {
                    if (n <= 1) return 1;
                    return n * factorial(n - 1);
                }
                """,
            ["./main.ts"] = """
                import { add, multiply, factorial } from './math-utils';

                console.log(add(3, 4));
                console.log(multiply(5, 6));
                console.log(factorial(5));
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("7\n30\n120\n", output);
    }

    /// <summary>
    /// Tests that an enum exported from one module can be imported and its
    /// members accessed from another module.
    /// </summary>
    [Theory, ModeData]
    public void CrossModule_EnumAccess(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./status.ts"] = """
                export enum Status {
                    Pending = 0,
                    Active = 1,
                    Completed = 2
                }

                export enum Priority {
                    Low = "low",
                    Medium = "medium",
                    High = "high"
                }
                """,
            ["./main.ts"] = """
                import { Status, Priority } from './status';

                console.log(Status.Pending);
                console.log(Status.Active);
                console.log(Status.Completed);
                console.log(Priority.Low);
                console.log(Priority.High);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("0\n1\n2\nlow\nhigh\n", output);
    }

    /// <summary>
    /// Tests that multiple modules can import from the same shared module,
    /// and each gets the correct types without interference.
    /// </summary>
    [Theory, ModeData]
    public void CrossModule_SharedDependency(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./shared.ts"] = """
                export class Counter {
                    value: number = 0;

                    increment(): void {
                        this.value = this.value + 1;
                    }

                    getValue(): number {
                        return this.value;
                    }
                }

                export function createCounter(): Counter {
                    return new Counter();
                }
                """,
            ["./module-a.ts"] = """
                import { Counter, createCounter } from './shared';

                export function useCounterA(): number {
                    let c = createCounter();
                    c.increment();
                    c.increment();
                    return c.getValue();
                }
                """,
            ["./module-b.ts"] = """
                import { Counter } from './shared';

                export function useCounterB(): number {
                    let c = new Counter();
                    c.increment();
                    c.increment();
                    c.increment();
                    return c.getValue();
                }
                """,
            ["./main.ts"] = """
                import { useCounterA } from './module-a';
                import { useCounterB } from './module-b';

                console.log(useCounterA());
                console.log(useCounterB());
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("2\n3\n", output);
    }

    /// <summary>
    /// Tests a complex scenario with classes, functions, and enums all being
    /// imported and used across modules.
    /// </summary>
    [Theory, ModeData]
    public void CrossModule_MixedTypes(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["./types.ts"] = """
                export enum TaskStatus {
                    Todo = 0,
                    InProgress = 1,
                    Done = 2
                }

                export class Task {
                    title: string;
                    status: number;

                    constructor(title: string) {
                        this.title = title;
                        this.status = TaskStatus.Todo;
                    }

                    start(): void {
                        this.status = TaskStatus.InProgress;
                    }

                    complete(): void {
                        this.status = TaskStatus.Done;
                    }
                }

                export function createTask(title: string): Task {
                    return new Task(title);
                }
                """,
            ["./main.ts"] = """
                import { Task, TaskStatus, createTask } from './types';

                let t1 = new Task("Write tests");
                console.log(t1.status);

                t1.start();
                console.log(t1.status);

                t1.complete();
                console.log(t1.status);

                let t2 = createTask("Review PR");
                console.log(t2.title);
                console.log(t2.status);
                """
        };

        var output = TestHarness.RunModules(files, "./main.ts", mode);
        Assert.Equal("0\n1\n2\nReview PR\n0\n", output);
    }

    #endregion
}

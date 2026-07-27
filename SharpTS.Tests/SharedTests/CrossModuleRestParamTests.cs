using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regression tests for rest-parameter functions imported across module
/// boundaries in compiled output. The first two cases cover plain functions
/// and object-literal methods (surfaced by the Phase 3c `path` migration).
/// The state-machine cases (generator / async / async-generator) cover #426:
/// when invoked indirectly via <c>$TSFunction.Invoke</c>, the stub's trailing
/// rest argument must be packed into the rest list. Before the fix the first
/// raw argument was dropped straight into the rest slot, crashing the body's
/// <c>for...of</c> with an InvalidCastException (scalar → IEnumerable). The
/// stubs now type the rest parameter <c>List&lt;object&gt;</c> (the marker
/// <c>$TSFunction.AdjustArgs</c> recognizes), mirroring the sync path.
/// </summary>
public class CrossModuleRestParamTests
{
    [Theory, ModeData]
    public void RestParam_AcrossModuleImport(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["lib.ts"] = """
                export function joinIt(...parts: string[]): string {
                    return parts.join(',');
                }
                """,
            ["main.ts"] = """
                import { joinIt } from './lib';
                console.log(joinIt('a', 'b', 'c'));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("a,b,c\n", output);
    }

    [Theory, ModeData]
    public void ObjectLiteralMethod_AcrossModuleImport(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["lib.ts"] = """
                export const ns = {
                    val: 'hi',
                    greet(name: string): string { return 'hello ' + name; },
                };
                """,
            ["main.ts"] = """
                import { ns } from './lib';
                console.log(ns.val);
                console.log(ns.greet('world'));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("hi\nhello world\n", output);
    }

    // ---- #426: state-machine functions (generator/async/async-generator) ----

    [Theory, ModeData]
    public void GeneratorRestParam_AcrossModuleImport(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["gen.ts"] = """
                export function* genA(...xs: number[]): Generator<number> {
                    for (const x of xs) yield x * 10;
                }
                """,
            ["main.ts"] = """
                import { genA } from './gen';
                let s = "";
                for (const v of genA(1, 2)) s += v + ",";
                console.log(s);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("10,20,\n", output);
    }

    [Theory, ModeData]
    public void AsyncRestParam_AcrossModuleImport(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["asum.ts"] = """
                export async function sum(...xs: number[]): Promise<number> {
                    let t = 0;
                    for (const x of xs) t += x;
                    return t;
                }
                """,
            ["main.ts"] = """
                import { sum } from './asum';
                sum(1, 2, 3).then(v => console.log("sum=" + v));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("sum=6\n", output);
    }

    [Theory, ModeData]
    public void AsyncGeneratorRestParam_AcrossModuleImport(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["agen.ts"] = """
                export async function* agenA(...xs: number[]): AsyncGenerator<number> {
                    for (const x of xs) yield x + 100;
                }
                """,
            // Consumed from a regular async function — a `for await...of` inside an
            // async *arrow* hits an unrelated pre-existing emit bug (#430), not #426.
            ["main.ts"] = """
                import { agenA } from './agen';
                async function consume(): Promise<void> {
                    let s = "";
                    for await (const v of agenA(1, 2, 3)) s += v + ",";
                    console.log("agen=" + s);
                }
                consume();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("agen=101,102,103,\n", output);
    }

    [Theory, ModeData]
    public void GeneratorLeadingRegularParamThenRest_AcrossModuleImport(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["mixed.ts"] = """
                export function* tagged(prefix: string, ...xs: number[]): Generator<string> {
                    for (const x of xs) yield prefix + x;
                }
                """,
            ["main.ts"] = """
                import { tagged } from './mixed';
                let s = "";
                for (const v of tagged("n", 1, 2)) s += v + ",";
                console.log(s);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("n1,n2,\n", output);
    }

    [Theory, ModeData]
    public void GeneratorEmptyRest_AcrossModuleImport(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["gen.ts"] = """
                export function* genA(...xs: number[]): Generator<number> {
                    for (const x of xs) yield x * 10;
                }
                """,
            ["main.ts"] = """
                import { genA } from './gen';
                let s = "[";
                for (const v of genA()) s += v + ",";
                s += "]";
                console.log(s);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("[]\n", output);
    }

    [Theory, ModeData]
    public void SpreadOfImportedGeneratorRestResult(ExecutionMode mode)
    {
        // Secondary #426 symptom: spreading the imported rest-param generator's
        // result ([...genA(1, 2)]) previously failed IL verification ($Initialize
        // BackwardBranch) before even reaching the runtime cast.
        var files = new Dictionary<string, string>
        {
            ["gen.ts"] = """
                export function* genA(...xs: number[]): Generator<number> {
                    for (const x of xs) yield x * 10;
                }
                """,
            ["main.ts"] = """
                import { genA } from './gen';
                console.log([...genA(1, 2)].join(","));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("10,20\n", output);
    }
}

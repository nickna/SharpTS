using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regressions for a cross-module closure-capture codegen bug that surfaced in
/// compiled mode: two sibling module-level functions (typically in a stdlib
/// file) that each returned an inner closure capturing a same-named parameter
/// aliased onto the same display-class field slot, so the later function's
/// closure read back null (or crashed in IsInstanceOfClass) at runtime.
/// </summary>
/// <remarks>
/// Root cause was in the arrow-to-function <c>$functionDC</c> source resolver:
/// it matched by "any enclosing function whose display class happens to
/// contain a field with this name" rather than "the specific enclosing
/// function for this arrow." Fixed by tracking each collected arrow's
/// enclosing <c>Stmt.Function</c> AST node at collection time
/// (<c>ILCompiler.ArrowFunctions._arrowEnclosingFunction</c>) and resolving
/// display-class sources strictly through that lookup in
/// <c>PropagateFunctionDCRequirements</c>. A parallel fix in
/// <c>PropagateArrowDCRequirements</c> now walks the parent-arrow chain
/// instead of iterating every registered arrow-scope DC by field name, and
/// <c>LocalVariableResolver</c> separates "current arrow's own DC" from
/// "parent arrow's DC reached through <c>$arrowDC</c>" so variables captured
/// from a parent route through the chain while the arrow's own locals still
/// use the direct local.
/// </remarks>
public class StdlibClosureCaptureTests
{
    [Theory, ModeData]
    public void SiblingFunctions_SameParamName_CapturedByClosures(ExecutionMode mode)
    {
        // Minimal repro: two module-level functions with `fn` params, each
        // returning a closure that captures and invokes its own `fn`. Before
        // the fix, the second closure's `fn` read back null because both
        // captures aliased onto the first function's display class.
        var files = new Dictionary<string, string>
        {
            ["lib.ts"] = """
                export function first(fn: any): any {
                    let warned = false;
                    return (): any => {
                        if (!warned) warned = true;
                        return fn();
                    };
                }

                export function second(fn: any): any {
                    return function (cb: any): any {
                        const result = fn();
                        cb(null, result);
                    };
                }
                """,
            ["main.ts"] = """
                import { second } from './lib';
                const wrapped = second(() => 42);
                wrapped((err: any, r: any) => { console.log(r); });
                """,
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("42\n", output);
    }

    [Theory, ModeData]
    public void NestedArrow_CapturesParentArrowRestParam(ExecutionMode mode)
    {
        // Second-order repro: a Promise-executor pattern where the innermost
        // arrow captures BOTH a function-level variable (`fn`) and an
        // arrow-scope rest-param (`args`) from a parent arrow. Before the fix,
        // the executor's context overwrote the parent-scope field map with its
        // own, dropping `args` from the resolver's view — throwing
        // "Undefined variable 'args'" at runtime.
        var source = """
            function promisify(fn: any): any {
                return function (...args: any[]): Promise<any> {
                    return new Promise((resolve: any, _reject: any) => {
                        const cb = (_err: any, value: any) => {
                            resolve(value);
                        };
                        fn(args[0], cb);
                    });
                };
            }
            async function main() {
                const doubled = promisify((n: number, cb: any) => cb(null, n * 2));
                const r = await doubled(21);
                console.log(r);
            }
            main();
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory, ModeData]
    public void ThreeSiblings_SameParamName_AllClosuresWork(ExecutionMode mode)
    {
        // Extends the two-sibling case to three, verifying the resolver still
        // picks the correct enclosing function for each nested closure.
        var files = new Dictionary<string, string>
        {
            ["lib.ts"] = """
                export function a(fn: any): any { return (): any => fn() + '-a'; }
                export function b(fn: any): any { return (): any => fn() + '-b'; }
                export function c(fn: any): any { return (): any => fn() + '-c'; }
                """,
            ["main.ts"] = """
                import { a, b, c } from './lib';
                console.log(a(() => 'x')());
                console.log(b(() => 'y')());
                console.log(c(() => 'z')());
                """,
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("x-a\ny-b\nz-c\n", output);
    }
}

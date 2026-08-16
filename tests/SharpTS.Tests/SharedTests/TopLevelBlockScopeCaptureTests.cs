using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regressions for #1201: compiled closures capturing a top-level BLOCK-scoped
/// <c>let</c>/<c>const</c> (e.g. inside <c>if (true) { … }</c>) had no shared storage —
/// the entry-point display class only registered direct top-level declarations. A closure
/// created inside another closure's body couldn't reach the entry method's local at all,
/// so its capture-populate fell through to <c>Ldnull</c>: the inner closure saw
/// <c>null</c> (silently — <c>typeof null === "object"</c>), mutations vanished, and
/// object references were lost. The fix lifts such bindings onto the entry-point display
/// class (mirroring what function display classes do for block-scoped locals inside
/// functions), guarded by a declared-exactly-once rule so shadowing patterns keep their
/// existing behavior, and never lifting from loop bodies (per-iteration bindings).
/// </summary>
public class TopLevelBlockScopeCaptureTests
{
    [Theory, ModeData]
    public void BlockScopedCapture_ClosureInLoopInClosure_SharesEnvironment(ExecutionMode mode)
    {
        // The issue's minimal repro: closures created in a loop inside another closure,
        // capturing block-scoped outer state. Before the fix, compiled printed
        // "set=0 n=1" twice (each inner callback saw null-backed fresh state).
        var source = """
            if (true) {
                const served = new Set<string>();
                let n = 0;
                setTimeout(() => {
                    for (let i = 0; i < 2; i++) {
                        setTimeout(() => {
                            served.add('x' + i);
                            n++;
                            console.log('set=' + served.size + ' n=' + n);
                        }, 5);
                    }
                }, 5);
            }
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("set=1 n=1\nset=2 n=2\n", output);
    }

    [Theory, ModeData]
    public void BlockScopedCapture_RelayedThroughIntermediateClosure_NoLoop(ExecutionMode mode)
    {
        // No loop involved: the intermediate closure doesn't reference the captured
        // names itself, so the inner closure's capture must be relayed. Before the
        // fix the inner closure captured null for both.
        var source = """
            if (true) {
                const items = new Set<string>();
                let count = 0;
                setTimeout(() => {
                    setTimeout(() => {
                        items.add('x');
                        count++;
                        console.log('set=' + items.size + ' n=' + count);
                    }, 5);
                }, 5);
            }
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("set=1 n=1\n", output);
    }

    [Theory, ModeData]
    public void BlockScopedCapture_MutationsSharedBothWays(ExecutionMode mode)
    {
        // Two-way sharing between the block body and a closure: block writes after
        // closure creation must be visible inside the closure, and closure writes
        // must be visible to later block code.
        var source = """
            if (true) {
                let counter = 0;
                const bump = () => { counter++; };
                counter = 10;
                bump();
                console.log(counter);
            }
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("11\n", output);
    }

    [Theory, ModeData]
    public void TopLevelLoopBody_Capture_StaysPerIteration(ExecutionMode mode)
    {
        // Bindings declared inside a top-level loop body are per-iteration and must
        // NOT be lifted onto the shared display class — each closure snapshots its
        // own iteration's value.
        var source = """
            const fns: (() => number)[] = [];
            for (let i = 0; i < 3; i++) {
                const x = i * 10;
                fns.push(() => x);
            }
            console.log(fns.map(f => f()).join(','));
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("0,10,20\n", output);
    }

    [Theory, ModeData]
    public void BlockInsideTopLevelLoop_Capture_StaysPerIteration(ExecutionMode mode)
    {
        // A block nested inside a loop is still loop territory: its bindings are
        // per-iteration, so the lift must not fire for them either.
        var source = """
            const fns: (() => string)[] = [];
            for (let k = 0; k < 2; k++) {
                if (true) {
                    const y = 'iter' + k;
                    fns.push(() => y);
                }
            }
            console.log(fns.map(f => f()).join(','));
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("iter0,iter1\n", output);
    }

    [Theory, ModeData]
    public void SiblingBlocks_SameName_EachClosureReadsOwnBinding(ExecutionMode mode)
    {
        // Same name declared in two sibling blocks fails the declared-exactly-once
        // rule, so neither is lifted — each closure keeps its own snapshot and the
        // bindings stay distinct.
        var source = """
            if (true) {
                const dup = 'first';
                const g1 = () => dup;
                console.log(g1());
            }
            if (true) {
                const dup = 'second';
                const g2 = () => dup;
                console.log(g2());
            }
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("first\nsecond\n", output);
    }

    [Theory, ModeData]
    public void SwitchCase_BlockScopedCapture_Relayed(ExecutionMode mode)
    {
        var source = """
            switch (1) {
                case 1: {
                    const bag = new Set<string>();
                    setTimeout(() => {
                        setTimeout(() => {
                            bag.add('a');
                            console.log('size=' + bag.size);
                        }, 5);
                    }, 5);
                    break;
                }
            }
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("size=1\n", output);
    }

    [Theory, ModeData]
    public void TryBlock_BlockScopedCapture_Relayed(ExecutionMode mode)
    {
        var source = """
            try {
                const bag = new Set<string>();
                setTimeout(() => {
                    setTimeout(() => {
                        bag.add('a');
                        bag.add('b');
                        console.log('size=' + bag.size);
                    }, 5);
                }, 5);
            } catch (e) {
                console.log('err');
            }
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("size=2\n", output);
    }

    [Theory, ModeData]
    public void BlockScopedCapture_InImportedModule(ExecutionMode mode)
    {
        // Module-init methods reach the entry-point display class through its static
        // field rather than a local — the lift must work there too.
        var files = new Dictionary<string, string>
        {
            ["lib.ts"] = """
                export function start(): void {}
                if (true) {
                    const libSet = new Set<string>();
                    let libN = 0;
                    setTimeout(() => {
                        for (let i = 0; i < 2; i++) {
                            setTimeout(() => {
                                libSet.add('m' + i);
                                libN++;
                                console.log('lib set=' + libSet.size + ' n=' + libN);
                            }, 5);
                        }
                    }, 5);
                }
                """,
            ["main.ts"] = """
                import { start } from './lib';
                start();
                console.log('main ran');
                """,
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("main ran\nlib set=1 n=1\nlib set=2 n=2\n", output);
    }

    // ── #1222: block-scoped shadow of a captured module-level binding ────────────
    // When a top-level block declares a let/const that SHADOWS a same-named captured
    // module-level binding, the name fails #1201's declared-exactly-once rule, so the
    // block binding stays a plain local. Closures created in the block must capture
    // that local — before the fix, name-keyed routing sent the capture to the module
    // binding's entry-point-DC field, so compiled closures read the OUTER value.

    [Theory, ModeData]
    public void ShadowedModuleBinding_ClosureCapturesBlockLocal(ExecutionMode mode)
    {
        // The issue's exact reproducer: compiled printed "outer\nouter".
        var source = """
            let sh = 'outer';
            const getOuter = () => sh;
            if (true) {
                let sh = 'inner';
                const getInner = () => sh;
                console.log(getInner());
            }
            console.log(getOuter());
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("inner\nouter\n", output);
    }

    [Theory, ModeData]
    public void ShadowedModuleBinding_AsyncArrowReadsShadow(ExecutionMode mode)
    {
        // Standalone async arrows deliberately read captured top-level names LIVE from
        // the entry-DC static field; for a shadow that live read hit the OUTER binding.
        // The shadow must come from the arrow's by-value snapshot field instead.
        var source = """
            let sh = 'outer';
            const getOuter = () => sh;
            if (true) {
                let sh = 'inner';
                const getInner = async () => sh;
                getInner().then(v => console.log('async=' + v));
            }
            setTimeout(() => console.log('module=' + getOuter()), 10);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("async=inner\nmodule=outer\n", output);
    }

    [Theory, ModeData]
    public void ShadowedModuleBinding_AsyncArrowWriteDoesNotClobberModule(ExecutionMode mode)
    {
        // A write to the shadow inside the async arrow must land on the arrow's
        // snapshot field — before the fix it overwrote the module binding's DC field.
        var source = """
            let sh = 'outer';
            const getOuter = () => sh;
            if (true) {
                let sh = 'inner';
                const clobber = async () => { sh = 'clobbered'; return sh; };
                clobber().then(v => console.log('ret=' + v));
            }
            setTimeout(() => console.log('module=' + getOuter()), 10);
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("ret=clobbered\nmodule=outer\n", output);
    }

    [Theory, ModeData]
    public void ShadowedModuleBinding_IncrementAndCompoundTargetShadow(ExecutionMode mode)
    {
        // ++ and += store-backs resolved the entry-DC field before locals, so they
        // wrote the module binding while plain reads saw the block local.
        var source = """
            let n = 100;
            const getN = () => n;
            if (true) {
                let n = 1;
                n++;
                n += 2;
                const g = () => n;
                console.log('block=' + g() + ',' + n);
            }
            console.log('module=' + getN());
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("block=4,4\nmodule=100\n", output);
    }

    [Theory, ModeData]
    public void ShadowedModuleBinding_FunctionDeclarationInBlock(ExecutionMode mode)
    {
        // Function DECLARATIONS in top-level blocks are lambda-lifted with by-value
        // capture forwarding (#605/#622); the forwarded argument is resolved at the
        // call site, locals first, so it picks up the shadow.
        var source = """
            let f = 'outer';
            const getOuter = () => f;
            if (true) {
                let f = 'inner';
                function getInner(): string { return f; }
                console.log(getInner());
            }
            console.log(getOuter());
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("inner\nouter\n", output);
    }

    [Theory, ModeData]
    public void ShadowedModuleBinding_InImportedModule(ExecutionMode mode)
    {
        // Module-init methods reach the entry-point DC through its static field;
        // the shadow decision must hold there too. (The module binding and its
        // reader are deliberately NOT exported directly to keep this test focused
        // on shadowing — the distinct exported-arrow-capturing-exported-let case is
        // #1229, covered by ExportedArrow_CapturingExportedLet_IsCallableCrossModule.)
        var files = new Dictionary<string, string>
        {
            ["lib.ts"] = """
                let tag = 'outer';
                const getOuter = () => tag;
                export function readOuter(): string { return getOuter(); }
                if (true) {
                    let tag = 'inner';
                    const getInner = () => tag;
                    console.log('lib=' + getInner());
                }
                """,
            ["main.ts"] = """
                import { readOuter } from './lib';
                console.log('main=' + readOuter());
                """,
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("lib=inner\nmain=outer\n", output);
    }

    [Theory, ModeData]
    public void ExportedArrow_CapturingExportedLet_IsCallableCrossModule(ExecutionMode mode)
    {
        // #1229: an `export const` arrow capturing an `export let` from the same module.
        // The non-escaping-arrow optimization (#858) flagged `getOuter` as a direct-call-only
        // local and stored the bare display-class instance instead of a $TSFunction wrapper —
        // but the exported binding escapes through the module's export field and is invoked
        // cross-module via generic reflective dispatch, which then saw a plain object
        // ("TypeError: object is not a function"). Exported names must be disqualified.
        var files = new Dictionary<string, string>
        {
            ["lib.ts"] = """
                export let tag = 'outer';
                export const getOuter = () => tag;
                """,
            ["main.ts"] = """
                import { getOuter } from './lib';
                console.log('main=' + getOuter());
                """,
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("main=outer\n", output);
    }

    [Theory, ModeData]
    public void ExportedArrow_WithParam_CapturingExportedLet_IsCallableCrossModule(ExecutionMode mode)
    {
        // #1229 variant: a capturing exported arrow that also takes a parameter, exported via a
        // separate `export { … }` statement (the specifier-list escape route, not the inline form).
        var files = new Dictionary<string, string>
        {
            ["lib.ts"] = """
                export let base = 10;
                const add = (x: number) => x + base;
                export { add };
                """,
            ["main.ts"] = """
                import { add } from './lib';
                console.log('add=' + add(5));
                """,
        };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("add=15\n", output);
    }

    [Theory, ModeData]
    public void BlockScopedCapture_FunctionExpression_SharesEnvironment(ExecutionMode mode)
    {
        // Function EXPRESSIONS route through the arrow display-class machinery and
        // must share the lifted binding like arrows do. (A function DECLARATION in a
        // top-level block is different territory: NestedFunctionLifter lambda-lifts
        // it with by-value capture forwarding — its leading parameter reuses the
        // binding's name, which fails the declared-exactly-once rule, so the lift
        // correctly stays out of that pre-existing #605/#622 limitation.)
        var source = """
            if (true) {
                let total = 0;
                const addOne = function (): void { total += 1; };
                const addTwo = () => { total += 2; };
                addOne();
                addTwo();
                console.log(total);
            }
            """;
        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n", output);
    }
}

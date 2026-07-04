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
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
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

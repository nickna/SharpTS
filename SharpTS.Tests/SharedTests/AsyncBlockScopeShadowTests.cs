using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Block-scope shadowing inside async functions and async generators (#766), and the
/// interpreter async-generator nested-block binding-before-await bug (#768). Mirrors the
/// compiled-generator coverage in <see cref="GeneratorTests"/> (#711), extended to the
/// suspension-by-await/yield state machines. Runs against both interpreter and compiler.
/// </summary>
public class AsyncBlockScopeShadowTests
{
    #region Async function (#766)

    [Theory, ModeData]
    public void AsyncFunction_NestedBlockConstShadow_DoesNotLeakToOuter(ExecutionMode mode)
    {
        // A const in a nested block that shadows an outer body-level const must get its own slot
        // instead of clobbering the outer binding's hoisted state-machine field (#766, async #711).
        var source = """
            async function af(): Promise<number> {
              const r = 100;
              { const r = 0; await Promise.resolve(r); }
              return r;
            }
            async function main() { console.log(await af()); }
            main();
            """;

        Assert.Equal("100\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncFunction_NestedBlockShadow_LiveAcrossAwait_GetsOwnField(ExecutionMode mode)
    {
        // The inner shadow is itself read after an await, so it must hoist to its OWN field, distinct
        // from the outer binding's field — both must survive the suspension independently (#766).
        var source = """
            async function af(): Promise<string> {
              const r = 100;
              let inner = 0;
              {
                const r = 5;
                await Promise.resolve(0);
                inner = r;
              }
              return inner + "/" + r;
            }
            async function main() { console.log(await af()); }
            main();
            """;

        Assert.Equal("5/100\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncFunction_NestedBlockLetShadow_ReassignedAcrossAwait(ExecutionMode mode)
    {
        // A let shadow compound-assigned across awaits keeps its own value, separate from the outer
        // binding (#766).
        var source = """
            async function af(): Promise<string> {
              let r = 7;
              let captured = 0;
              {
                let r = 1;
                r += 1;
                await Promise.resolve(0);
                r += 10;
                captured = r;
              }
              return captured + "/" + r;
            }
            async function main() { console.log(await af()); }
            main();
            """;

        Assert.Equal("12/7\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncFunction_NestedBlockShadowsParameter(ExecutionMode mode)
    {
        // An inner block const may shadow a (hoisted) parameter without clobbering it (#766).
        var source = """
            async function af(r: number): Promise<string> {
              let captured = 0;
              { const r = 99; await Promise.resolve(0); captured = r; }
              return captured + "/" + r;
            }
            async function main() { console.log(await af(5)); }
            main();
            """;

        Assert.Equal("99/5\n", TestHarness.Run(source, mode));
    }

    #endregion

    #region Async arrow (#766)

    [Theory, ModeData]
    public void AsyncArrow_NestedBlockConstShadow_DoesNotLeakToOuter(ExecutionMode mode)
    {
        // Same shadow leak in a compiled async arrow's state machine (#766).
        var source = """
            const af = async (): Promise<number> => {
              const r = 100;
              { const r = 0; await Promise.resolve(r); }
              return r;
            };
            async function main() { console.log(await af()); }
            main();
            """;

        Assert.Equal("100\n", TestHarness.Run(source, mode));
    }

    #endregion

    #region Async generator (#766 + #768)

    [Theory, ModeData]
    public void AsyncGenerator_NestedBlockConstShadow_DoesNotLeakToOuter(ExecutionMode mode)
    {
        // Compiled: the nested-block shadow leaked onto the outer binding's hoisted field → [0, 0] (#766).
        // Interpreter: the inner block const yielded before any await read as undefined → [undefined, 100] (#768).
        var source = """
            async function* ag(): AsyncGenerator<number> {
              const r = 100;
              { const r = 0; yield r; }
              yield r;
            }
            async function main() {
              const g = ag();
              let a = await g.next();
              let b = await g.next();
              console.log(a.value + "," + b.value);
            }
            main();
            """;

        Assert.Equal("0,100\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncGenerator_NestedBlockShadow_LiveAcrossYield_GetsOwnField(ExecutionMode mode)
    {
        // Both the inner shadow and the outer binding survive the suspension on their own slots (#766).
        var source = """
            async function* ag(): AsyncGenerator<number> {
              const r = 100;
              {
                const r = 0;
                yield r;
                yield r + 1;
              }
              yield r;
            }
            async function main() {
              const g = ag();
              let out = "";
              for (let i = 0; i < 3; i++) out += (await g.next()).value + (i < 2 ? "," : "");
              console.log(out);
            }
            main();
            """;

        Assert.Equal("0,1,100\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncGenerator_NestedBlockBindingBeforeAwait_YieldsValue(ExecutionMode mode)
    {
        // #768: distinct names (no shadowing). The interpreter lost a nested-block binding's value when
        // it was yielded before the generator first suspended on an await → [undefined, 100].
        var source = """
            async function* ag(): AsyncGenerator<number> {
              const a = 100;
              { const b = 0; yield b; }
              yield a;
            }
            async function main() {
              const g = ag();
              let x = await g.next();
              let y = await g.next();
              console.log(x.value + "," + y.value);
            }
            main();
            """;

        Assert.Equal("0,100\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncGenerator_NestedBlockLetBeforeAwait_Reassigned(ExecutionMode mode)
    {
        // #768 variant: a nested-block let read/updated before any await must keep its value.
        var source = """
            async function* ag(): AsyncGenerator<number> {
              { let b = 1; b += 4; yield b; }
              yield 9;
            }
            async function main() {
              const g = ag();
              let x = await g.next();
              let y = await g.next();
              console.log(x.value + "," + y.value);
            }
            main();
            """;

        Assert.Equal("5,9\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncGenerator_NestedBlockShadow_CapturedByArrow_DoesNotLeak(ExecutionMode mode)
    {
        // An inner block const that shadows an outer binding AND is read by a nested arrow gets its own
        // slot; the arrow reads the inner value while the outer keeps its own (#767, async-gen analog).
        // Async generators lift only captured-AND-mutated locals into the function DC, so the read-only
        // arrow capture flows through the per-arrow snapshot path the capture pivot redirects.
        var source = """
            async function* ag(): AsyncGenerator<number> {
              const r = 100;
              {
                const r = 7;
                const f = () => r;
                await Promise.resolve(0);
                yield f();
              }
              yield r;
            }
            async function main() {
              const g = ag();
              let x = await g.next();
              let y = await g.next();
              console.log(x.value + "," + y.value);
            }
            main();
            """;

        Assert.Equal("7,100\n", TestHarness.Run(source, mode));
    }

    #endregion

    #region Read-capture of a shadow by an inner arrow (#837, async analog of #767)

    [Theory, ModeData]
    public void AsyncFunction_NestedBlockShadow_CapturedByArrow(ExecutionMode mode)
    {
        // An inner block const that shadows an outer binding AND is read by a nested arrow gets its own
        // slot; the arrow reads the inner value while the outer keeps its own (#837). Compiled async
        // FUNCTIONS used to lift every captured local into a name-keyed function display class, so a
        // read-only arrow capture of the shadow collided with the outer binding on one DC field (leaked
        // 5,5). The fix renames the shadow (#767 pivot) and excludes it from the function DC so the
        // arrow's read flows through the per-arrow snapshot path the pivot redirects.
        var source = """
            async function af(): Promise<string> {
              const out: string[] = [];
              const r = 100;
              {
                const r = 5;
                const f = () => r;
                await Promise.resolve(0);
                out.push(String(f()));
              }
              out.push(String(r));
              return out.join(",");
            }
            async function main() { console.log(await af()); }
            main();
            """;

        Assert.Equal("5,100\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncArrow_NestedBlockShadow_CapturedByArrow(ExecutionMode mode)
    {
        // Async-ARROW analog of the above (#837): the shadow read by an inner arrow gets its own slot.
        var source = """
            const af = async (): Promise<string> => {
              const out: string[] = [];
              const r = 100;
              {
                const r = 5;
                const f = () => r;
                await Promise.resolve(0);
                out.push(String(f()));
              }
              out.push(String(r));
              return out.join(",");
            };
            async function main() { console.log(await af()); }
            main();
            """;

        Assert.Equal("5,100\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncFunction_TwoShadowsReadCapturedByDistinctArrows(ExecutionMode mode)
    {
        // Two independent nested-block shadows, each read by its own arrow, must each get their own slot
        // without cross-contamination (#837). Confirms the exclusion handles more than one renamed shadow.
        var source = """
            async function af(): Promise<string> {
              const out: string[] = [];
              const r = 100;
              const s = 200;
              {
                const r = 5;
                const f = () => r;
                await Promise.resolve(0);
                out.push(String(f()));
              }
              {
                const s = 9;
                const g = () => s;
                await Promise.resolve(0);
                out.push(String(g()));
              }
              out.push(String(r));
              out.push(String(s));
              return out.join(",");
            }
            async function main() { console.log(await af()); }
            main();
            """;

        Assert.Equal("5,9,100,200\n", TestHarness.Run(source, mode));
    }

    #endregion

    #region Write-capture of a shadow by an inner sync arrow (#838, write analog of #767)

    [Theory, ModeData]
    public void AsyncFunction_NestedBlockShadow_WriteCapturedByArrow(ExecutionMode mode)
    {
        // An inner block let that shadows an outer binding AND is WRITTEN by a nested sync arrow gets its
        // own renamed shared $functionDC slot; the arrow's read/write land on the inner binding while the
        // outer keeps its own (#838). Compiled previously collided on a single name-keyed DC field.
        var source = """
            async function af(): Promise<string> {
              const out: string[] = [];
              const r = 100;
              {
                let r = 5;
                const f = () => { r = r + 1; return r; };
                await Promise.resolve(0);
                out.push(String(f()));
                out.push(String(r));
              }
              out.push(String(r));
              return out.join(",");
            }
            async function main() { console.log(await af()); }
            main();
            """;

        Assert.Equal("6,6,100\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncGenerator_NestedBlockShadow_WriteCapturedByArrow(ExecutionMode mode)
    {
        // Async-GENERATOR analog (#838): the write-captured shadow gets its own renamed DC field, shared
        // between the generator body and the sync arrow, without colliding with the outer same-named const.
        var source = """
            async function* mut(): AsyncGenerator<number> {
              const r = 100;
              {
                let r = 5;
                const f = () => { r = r + 1; return r; };
                yield f();
                yield r;
              }
              yield r;
            }
            async function main() {
              const out: number[] = [];
              for await (const x of mut()) out.push(x);
              console.log(out.join(","));
            }
            main();
            """;

        Assert.Equal("6,6,100\n", TestHarness.Run(source, mode));
    }

    #endregion
}

using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regression tests for #313: when two (or more) closures created inside the
/// same invocation of an inner function declaration capture the same mutable
/// local of that function, they must share storage — mutations through one
/// closure are visible to the others. Compiled mode gives inner function
/// declarations the same per-invocation scope display class arrows get
/// (instantiated at the top of the Invoke body; nested closures hold a live
/// reference to it). Same family as #307, distinct mechanism: #307 was the
/// inner function READING an ancestor arrow's scope; this is the inner
/// function PROVIDING a scope to its own nested closures.
/// </summary>
public class InnerFunctionScopeDisplayClassTests
{
    [Theory, ModeData]
    public void SiblingClosures_ShareInnerFunctionLocal(ExecutionMode mode)
    {
        // Minimal repro from #313: get() must see inc()'s mutations.
        var source = """
            const outer = () => {
              function make() {
                let n = 0;
                const inc = () => { n = n + 1; return n; };
                const get = () => n;
                return { inc: inc, get: get };
              }
              const c = make();
              c.inc();
              c.inc();
              return c.get();
            };
            console.log(outer());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n", output);
    }

    [Theory, ModeData]
    public void EachInvocation_GetsFreshScope(ExecutionMode mode)
    {
        var source = """
            const outer = () => {
              function make() {
                let n = 0;
                const inc = () => { n = n + 1; return n; };
                const get = () => n;
                return { inc: inc, get: get };
              }
              const a = make();
              const b = make();
              a.inc(); a.inc(); a.inc();
              b.inc();
              return a.get() * 10 + b.get();
            };
            console.log(outer());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("31\n", output);
    }

    [Theory, ModeData]
    public void CapturedParameter_ReassignedAndMutatedBySiblings(ExecutionMode mode)
    {
        // Captured typed parameter: reassignment in the function body must be
        // visible to nested closures, and a closure's mutation must be visible
        // to same-body reads (scope-DC store + arg-slot dual-write).
        var source = """
            const outer = () => {
              function counter(start: number) {
                start = start + 100;
                const bump = () => { start = start + 1; };
                const read = () => start;
                bump();
                return read();
              }
              return counter(5);
            };
            console.log(outer());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("106\n", output);
    }

    [Theory, ModeData]
    public void NestedInnerFunction_OwnsItsOwnScope(ExecutionMode mode)
    {
        // The scope-owning function is itself nested inside another inner
        // function declaration.
        var source = """
            const outer = () => {
              function level1() {
                function level2() {
                  let v = 1;
                  const dbl = () => { v = v * 2; };
                  const readV = () => v;
                  dbl();
                  dbl();
                  return readV();
                }
                return level2();
              }
              return level1();
            };
            console.log(outer());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("4\n", output);
    }

    [Theory, ModeData]
    public void Closure_CapturesInnerFunctionScopeAndAncestorArrowScope(ExecutionMode mode)
    {
        // One closure captures from TWO scopes: the inner function's own
        // scope DC (funcLocal) and the enclosing arrow's scope DC (arrowLocal).
        var source = """
            const outer = () => {
              let arrowLocal = 7;
              const touch = () => { arrowLocal = arrowLocal + 1; };
              function mix() {
                let funcLocal = 100;
                const both = () => { funcLocal = funcLocal + 1; return funcLocal + arrowLocal; };
                both();
                touch();
                return both();
              }
              return mix();
            };
            console.log(outer());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("110\n", output);
    }

    [Theory, ModeData]
    public void HoistedSiblingFunctions_ShareInnerFunctionLocal(ExecutionMode mode)
    {
        // The sibling closures are hoisted function declarations, not arrows —
        // they route through the inner-function $arrowScopeDC reference.
        var source = """
            const outer = () => {
              function host() {
                let shared = 1;
                function add() { shared = shared + 10; }
                function read() { return shared; }
                add();
                add();
                return read();
              }
              return host();
            };
            console.log(outer());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("21\n", output);
    }

    [Theory, ModeData]
    public void InnerFunctionInTopLevelFunction_SiblingsShare(ExecutionMode mode)
    {
        // Enclosing callable is a top-level function declaration, not an arrow.
        var source = """
            function topHost() {
              function make() {
                let k = 0;
                const up = () => { k = k + 2; return k; };
                const peek = () => k;
                up();
                up();
                return peek();
              }
              return make();
            }
            console.log(topHost());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("4\n", output);
    }

    [Theory, ModeData]
    public void EscapedClosures_MutateAfterReturn(ExecutionMode mode)
    {
        // Closures escape the inner function and mutate later — storage is
        // genuinely shared, not call-order luck.
        var source = """
            const outer = () => {
              function cell() {
                let value = 0;
                return {
                  set: (x: number) => { value = x; },
                  get: () => value
                };
              }
              const c1 = cell();
              const c2 = cell();
              c1.set(42);
              c2.set(9);
              return c1.get() * 100 + c2.get();
            };
            console.log(outer());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("4209\n", output);
    }
}

using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regression tests for #954: a heterogeneous array literal whose inferred element
/// type is a union (e.g. <c>[nn(), x]</c> → <c>(number[] | number)[]</c>) made the
/// compiler map the array to <c>List&lt;Union_*&gt;</c>. A generated <c>Union_*</c> struct
/// is a <c>TypeBuilder</c>, so the closed generic is a <c>TypeBuilderInstantiation</c>, and a
/// reflection probe on it (<c>ResolveReturnType</c>'s <c>IsSubclassOf(Delegate)</c>) threw
/// <c>NotSupportedException: "Specified method is not supported."</c> at emit time — compiled
/// only; the interpreter was always fine. Fixed by collapsing union collection-elements (and
/// <c>Promise&lt;union&gt;</c> inners) to <c>object</c> at the type-mapping choke points.
/// Runs in both interpreter and compiled modes.
/// </summary>
public class UnionArrayLiteralTests
{
    [Theory, ModeData]
    public void IssueRepro_ArrowReturningArrayMixingFreeFnCallAndParam(ExecutionMode mode)
    {
        // Exact issue repro: array literal mixing a free-function call (number[]) and the
        // arrow's own number parameter → element type number[] | number.
        var source = """
            function nn(): number[] { const a: number[] = []; a[0] = 1; return a; }
            const f = (x: number) => [nn(), x];
            console.log(f(1).length);
            """;
        Assert.Equal("2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void MapCallback_ArrayMixingFreeFnCallAndParam(ExecutionMode mode)
    {
        var source = """
            function nn(): number[] { const a: number[] = []; a[0] = 1; return a; }
            console.log([1, 2].map(x => [nn(), x]).length);
            """;
        Assert.Equal("2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void FlatMapCallback_ArrayMixingFreeFnCallAndParam(ExecutionMode mode)
    {
        var source = """
            function nn(): number[] { const a: number[] = []; a[0] = 1; return a; }
            console.log([1, 2].flatMap(x => [nn(), x]).length);
            """;
        Assert.Equal("4\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ScalarUnionArrayLiteral(ExecutionMode mode)
    {
        // No collection member, but number | string still generates a Union_* struct, so the
        // array is List<Union_number_STRING> — the same TypeBuilderInstantiation hazard.
        var source = """
            const g = (x: number, y: string) => [x, y];
            console.log(g(1, "a").length);
            """;
        Assert.Equal("2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void PromiseUnionReturn(ExecutionMode mode)
    {
        // Promise<number | string> maps to Task<Union_*> — the analogous choke point.
        var source = """
            async function h(): Promise<number | string> { return 5; }
            async function main() {
                const v = await h();
                console.log(typeof v);
                console.log(v);
            }
            main();
            """;
        Assert.Equal("number\n5\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void MapWithUnionValueType(ExecutionMode mode)
    {
        // Map<string, number[] | number> exercises MapMapTypeStrict's value element via the
        // same MapCollectionElementStrict choke point.
        var source = """
            function nn(): number[] { const a: number[] = []; a[0] = 1; return a; }
            const m = new Map<string, number[] | number>();
            m.set("k", nn());
            m.set("j", 7);
            console.log(m.size);
            """;
        Assert.Equal("2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Control_HomogeneousNestedNumberArray(ExecutionMode mode)
    {
        // No union (number[][]) — must remain unaffected by the fix.
        var source = """
            function nn(): number[] { const a: number[] = []; a[0] = 1; return a; }
            const f = (x: number) => [nn()];
            console.log(f(1).length);
            """;
        Assert.Equal("1\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Control_HomogeneousNumberArray(ExecutionMode mode)
    {
        // No union (number[]) — must remain unaffected by the fix.
        var source = """
            const f = (x: number) => [x, x];
            console.log(f(1).length);
            """;
        Assert.Equal("2\n", TestHarness.Run(source, mode));
    }
}

using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regression for #951: in compiled mode the variadic <c>Math.max</c>,
/// <c>Math.min</c>, and <c>Math.hypot</c> emitters iterated their arguments and
/// emitted each via <c>EmitExpression</c>, but a spread argument (<c>...arr</c>)
/// is an <c>Expr.Spread</c> whose emit just produces the inner array — so
/// <c>Math.max(...arr)</c> fed the array object to <c>ToNumber</c> and returned
/// <c>NaN</c>. The fix routes spread calls through the variadic <c>object[]</c>
/// adapters with a spread-expanded argument array (the same expansion the
/// generic <c>f(...arr)</c> call site uses).
/// </summary>
public class MathSpreadArgumentTests
{
    [Theory, ModeData]
    public void Math_Max_Min_Hypot_BasicSpread(ExecutionMode mode)
    {
        var source = @"
            console.log(Math.max(...[1, 2, 3]));
            console.log(Math.min(...[1, 2, 3]));
            console.log(Math.hypot(...[3, 4]));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n1\n5\n", output);
    }

    [Theory, ModeData]
    public void Math_Spread_MixedWithRegularArgs(ExecutionMode mode)
    {
        var source = @"
            console.log(Math.max(1, ...[5, 2], 4));
            console.log(Math.hypot(...[3, 4], 12));
            console.log(Math.min(...[3], ...[1, 9]));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\n13\n1\n", output);
    }

    [Theory, ModeData]
    public void Math_Spread_EmptyArray(ExecutionMode mode)
    {
        // Spreading an empty array yields the no-argument result: max → -Infinity,
        // min → Infinity, hypot → 0.
        var source = @"
            console.log(Math.max(...[]));
            console.log(Math.min(...[]));
            console.log(Math.hypot(...[]));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("-Infinity\nInfinity\n0\n", output);
    }

    [Theory, ModeData]
    public void Math_Spread_BoxedAnyArray(ExecutionMode mode)
    {
        // A spread of an `any[]` (boxed-element representation) must expand the
        // same as an inline numeric array literal.
        var source = @"
            const arr: any[] = [10, 20, 5];
            console.log(Math.max(...arr));
            console.log(Math.min(...arr));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("20\n5\n", output);
    }

    [Theory, ModeData]
    public void Math_Spread_NaNAndInfinitySemantics(ExecutionMode mode)
    {
        // max/min short-circuit to NaN on any NaN; hypot's Infinity check fires
        // BEFORE its NaN check (ECMA-262 21.3.2.16), so a mix returns Infinity.
        var source = @"
            console.log(Math.max(...[1, NaN, 3]));
            console.log(Math.min(...[1, NaN, 3]));
            console.log(Math.hypot(...[NaN, Infinity]));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("NaN\nNaN\nInfinity\n", output);
    }

    [Theory, ModeData]
    public void Math_Spread_InsideAsyncFunction(ExecutionMode mode)
    {
        // Exercises the async built-in call path (CallBuiltInWithPooledArgs /
        // the async MoveNext emitter): a spread whose element list is produced by
        // an awaited value must still expand correctly.
        var source = @"
            async function f() {
                const arr = [3, 1, 4, 1, 5];
                console.log(Math.max(...arr));
                console.log(Math.max(...[await Promise.resolve(10)], 2));
            }
            f();
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\n10\n", output);
    }
}

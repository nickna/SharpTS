using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regression for #952: in compiled mode the array-method emitters built
/// <c>concat</c>'s argument array by emitting each AST arg via
/// <c>EmitExpression</c>, but a spread argument (<c>...arr</c>) is an
/// <c>Expr.Spread</c> whose emit just yields the inner array — so
/// <c>[0].concat(...[[1, 2]])</c> reached the runtime <c>ArrayConcat</c> as a
/// single nested item and produced <c>[0,[1,2]]</c> instead of <c>[0,1,2]</c>.
/// The fix routes <c>concat</c>'s args through <c>EmitArgsArrayWithSpread</c>
/// (the same spread expansion the generic <c>f(...arr)</c> call site uses), so
/// the spread is flattened before <c>concat</c> applies its per-argument
/// one-level flatten. The interpreter already expanded spreads correctly, so
/// each case is asserted against Node-identical output in BOTH modes.
/// </summary>
public class ArrayConcatSpreadTests
{
    [Theory, ModeData]
    public void Concat_SpreadOfArrayOfArrays_FlattensOneLevel(ExecutionMode mode)
    {
        // The issue repro: ...[[1, 2]] spreads into concat([1, 2]), which then
        // flattens that one level → [0, 1, 2].
        var source = @"
            console.log(JSON.stringify([0].concat(...[[1, 2]])));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("[0,1,2]\n", output);
    }

    [Theory, ModeData]
    public void Concat_SpreadOfFlatArray_AppendsElements(ExecutionMode mode)
    {
        // ...[1, 2] spreads into concat(1, 2); neither arg is an array, so each
        // is appended individually.
        var source = @"
            console.log(JSON.stringify([0].concat(...[1, 2])));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("[0,1,2]\n", output);
    }

    [Theory, ModeData]
    public void Concat_SpreadOfMultipleArrays_FlattensEach(ExecutionMode mode)
    {
        // ...[[1,2],[3]] spreads into concat([1,2], [3]) → each flattened once.
        var source = @"
            console.log(JSON.stringify([0].concat(...[[1, 2], [3]])));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("[0,1,2,3]\n", output);
    }

    [Theory, ModeData]
    public void Concat_MixedSpreadAndPlainArgs(ExecutionMode mode)
    {
        // concat(2, ...[[3, 4]], 5) → append 2, flatten [3,4], append 5.
        var source = @"
            console.log(JSON.stringify([1].concat(2, ...[[3, 4]], 5)));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("[1,2,3,4,5]\n", output);
    }

    [Theory, ModeData]
    public void Concat_SpreadOfBoxedStringArrays(ExecutionMode mode)
    {
        // The bug is not numeric-specific — boxed (string) element arrays spread
        // the same way.
        var source = @"
            console.log(JSON.stringify([""a""].concat(...[[""b"", ""c""]])));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("[\"a\",\"b\",\"c\"]\n", output);
    }

    [Theory, ModeData]
    public void Concat_SpreadOfVariable(ExecutionMode mode)
    {
        // Spread source as a named variable (not an inline literal) routes
        // through the same expansion.
        var source = @"
            const x = [[1, 2]];
            console.log(JSON.stringify([0].concat(...x)));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("[0,1,2]\n", output);
    }

    [Theory, ModeData]
    public void Concat_SpreadOfEmptyArray_NoOp(ExecutionMode mode)
    {
        // Spreading an empty array contributes no arguments.
        var source = @"
            console.log(JSON.stringify([0].concat(...[])));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("[0]\n", output);
    }

    [Theory, ModeData]
    public void Concat_SpreadInsideAsyncFunction(ExecutionMode mode)
    {
        // Exercises the async emitter: the spread element list is produced by an
        // awaited value. EmitArgsArrayWithSpread evaluates args into temps with a
        // clear stack, so the await-suspending spread still expands correctly.
        var source = @"
            async function f() {
                console.log(JSON.stringify([0].concat(...await Promise.resolve([[1, 2]]))));
            }
            f();
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("[0,1,2]\n", output);
    }
}

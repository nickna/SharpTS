using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regression tests for issue #681: <c>Function.prototype.call</c> / <c>.apply</c> /
/// <c>.bind</c> on async functions (async arrows, async function expressions, async
/// function declarations).
/// </summary>
/// <remarks>
/// Two defects were fixed:
/// (A) Interpreter — async callables didn't classify as <c>TypeCategory.Function</c>, so
///     member access never reached <c>FunctionBuiltIns</c> and <c>aarrow.call(...)</c> threw
///     "undefined is not a function". Fixed by implementing <c>ITypeCategorized</c> on
///     <c>SharpTSAsyncFunction</c>/<c>SharpTSAsyncArrowFunction</c> plus async this-binding
///     branches in <c>InvokeWithThis</c>/<c>BoundFunction</c>.
/// (B) Compiled — a standalone async function expression (HasOwnThis) dropped the supplied
///     <c>this</c>: it had no field for a dynamically-bound receiver. Fixed by adding
///     <c>OwnThisField</c> to the async-arrow state machine, snapshotting the thread-local
///     <c>_currentFunctionThis</c> into it at stub entry, and reading it from <c>EmitThis</c>.
/// </remarks>
public class AsyncCallApplyBindTests
{
    [Theory, ModeData]
    public void AsyncArrow_Call_PassesArguments(ExecutionMode mode)
    {
        // The headline repro from the issue: used to throw "undefined is not a function".
        var source = """
            const aarrow = async (x: number) => x + 1;
            aarrow.call(null, 5).then((r: number) => console.log(r));
            """;

        Assert.Equal("6\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncFunctionExpression_Call_BindsThis(ExecutionMode mode)
    {
        var source = """
            const af = async function (this: any) { return this.x; };
            af.call({ x: 42 }).then((r: number) => console.log(r));
            """;

        Assert.Equal("42\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncFunctionExpression_Apply_BindsThis(ExecutionMode mode)
    {
        var source = """
            const af = async function (this: any) { return this.x; };
            af.apply({ x: 7 }, []).then((r: number) => console.log(r));
            """;

        Assert.Equal("7\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncFunctionExpression_Bind_BindsThis(ExecutionMode mode)
    {
        var source = """
            const af = async function (this: any) { return this.x; };
            const b = af.bind({ x: 9 });
            b().then((r: number) => console.log(r));
            """;

        Assert.Equal("9\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncArrow_Bind_IgnoresThisAppliesPartialArgs(ExecutionMode mode)
    {
        // True async arrows ignore the bound `this` (lexical) but still honor partial args.
        var source = """
            const add = async (a: number, b: number) => a + b;
            add.bind(null, 10)(5).then((r: number) => console.log(r));
            """;

        Assert.Equal("15\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncFunctionExpression_CallBindsThis_AcrossAwait(ExecutionMode mode)
    {
        // The bound `this` must survive a state-machine suspension/resume.
        var source = """
            const af = async function (this: any, n: number) {
                await Promise.resolve(0);
                return this.x + n;
            };
            af.call({ x: 100 }, 5).then((r: number) => console.log(r));
            """;

        Assert.Equal("105\n", TestHarness.Run(source, mode));
    }
}

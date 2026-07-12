using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regression for the previously-missing JS <c>arguments</c> array-like binding.
/// In both interpreter and compiled mode, referencing <c>arguments</c> inside a
/// non-arrow function body used to throw <c>Undefined variable 'arguments'</c>.
/// </summary>
/// <remarks>
/// Fix: interpreter binds <c>arguments</c> in <c>SharpTSFunction.Call</c> /
/// <c>CallV2</c>; compiled mode emits a prologue (<c>ILCompiler.Functions
/// .EmitArgumentsLocalPrologue</c>) that builds a <c>List&lt;object&gt;</c>
/// from the declared parameters — spreading rest-param collections so each
/// caller value occupies its own index. Arrow functions deliberately do NOT
/// bind <c>arguments</c>: per JS spec they inherit from the enclosing
/// non-arrow function via normal lexical scope.
/// </remarks>
public class ArgumentsMagicVariableTests
{
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Arguments_LengthAndIndexing(ExecutionMode mode)
    {
        var source = @"
            function f(a: number, b: number): void {
                console.log(arguments.length);
                console.log(arguments[0]);
                console.log(arguments[1]);
            }
            f(10, 20);
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n10\n20\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Arguments_WithRestParam_SpreadsToDistinctIndices(ExecutionMode mode)
    {
        // Rest params aggregate trailing args into a collection, but the
        // `arguments` array-like must still surface each caller value at its
        // own index.
        var source = @"
            function sum(...nums: number[]): number {
                let total = 0;
                for (let i = 0; i < arguments.length; i++) {
                    total += arguments[i];
                }
                return total;
            }
            console.log(sum());
            console.log(sum(1));
            console.log(sum(1, 2, 3, 4));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n1\n10\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Arguments_InsideArrow_InheritsFromEnclosingFunction(ExecutionMode mode)
    {
        // Per JS spec: `arguments` in an arrow is a lexical reference to the
        // enclosing non-arrow function's binding — arrows never introduce
        // their own `arguments`. Fixed for compiled mode in #64 by declaring
        // `arguments` as a synthetic local in the closure analyzer and writing
        // it into the enclosing function's display class so the arrow can read
        // it through the standard captured-local pathway.
        var source = @"
            function outer(a: number, b: number): number {
                const inner = (): number => {
                    let total = 0;
                    for (let i = 0; i < arguments.length; i++) {
                        total += arguments[i];
                    }
                    return total;
                };
                return inner();
            }
            console.log(outer(7, 35));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Arguments_CapturesExtrasPastDeclaredArity(ExecutionMode mode)
    {
        // The lodash `overRest` pattern: a wrapper function declares zero (or
        // fewer) parameters but is called with several — the only way to reach
        // those extras from inside the body is `arguments`. Before #64 the
        // compiled-mode prologue materialized `arguments` from declared params
        // only, so extras silently disappeared. Fix: publish the raw caller
        // args to `$TSFunction._currentArguments` (via either
        // `$TSFunction.Invoke` or direct-call emitter) and copy them into the
        // arguments list on entry.
        var source = @"
            function wrap(): any {
                return function () {
                    let total = 0;
                    for (let i = 0; i < arguments.length; i++) {
                        total += arguments[i] as number;
                    }
                    return total;
                };
            }
            const f = wrap();
            console.log(f(1, 2, 3));
            console.log(f(10, 20, 30, 40));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("6\n100\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Arguments_DirectCallWithZeroDeclaredParams_SeesExtras(ExecutionMode mode)
    {
        // Direct-call path (not through `$TSFunction.Invoke`): the emitter needs
        // to publish caller args to the thread-static before `OpCodes.Call`
        // because the callee's declared signature has no slots for the extras.
        // Also exercises #65 — previously this call shape crashed with
        // `InvalidProgramException` because every arg was pushed before a call
        // to a zero-param method.
        var source = @"
            function test() {
                console.log(arguments.length);
                console.log(arguments[0]);
                console.log(arguments[1]);
            }
            test('hello', 'world');
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\nhello\nworld\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Arguments_InMethodOfClass(ExecutionMode mode)
    {
        var source = @"
            class Widget {
                label(prefix: string, suffix: string): string {
                    // arguments[0] is the prefix; [1] the suffix.
                    return arguments[0] + '-' + arguments[1];
                }
            }
            const w = new Widget();
            console.log(w.label('foo', 'bar'));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("foo-bar\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Arguments_IndexedTraversal(ExecutionMode mode)
    {
        // Indexed access through `arguments.length` is the legacy-JS variadic
        // pattern. We use `...rest` so the TypeScript checker is happy with
        // varying call arities; the prologue's rest-param spread means
        // `arguments` exposes each caller value at its own index, not as one
        // nested array at [0].
        var source = @"
            function collect(...items: string[]): string {
                let acc = '';
                for (let i = 0; i < arguments.length; i++) {
                    acc = acc + String(arguments[i]);
                }
                return acc;
            }
            console.log(collect());
            console.log(collect('a'));
            console.log(collect('a', 'b', 'c'));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("\na\nabc\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Arguments_FunctionExpression_ApplyForwarding(ExecutionMode mode)
    {
        // Lodash-style wrapper: the returned function EXPRESSION binds its own
        // `arguments` and forwards them via apply. Guards the lazy-materialization
        // scan (interpreter only materializes `arguments` for bodies that observe
        // it): the inner expression's usage must not be attributed to `outer`,
        // and must still be materialized for the inner function itself.
        var source = @"
            function sum(a: number, b: number): number { return a + b; }
            function outer(func: any): any {
                return function (): any {
                    return func.apply(this, arguments);
                };
            }
            const wrapped = outer(sum);
            console.log(wrapped(3, 4));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("7\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void Arguments_VisibleToDirectEval(ExecutionMode mode)
    {
        // Direct eval runs against the live scope chain, so `arguments` must be
        // materialized even though the identifier never appears in the AST —
        // the lazy-materialization scan treats any `eval` reference in the body
        // as a conservative use. Interpreter-only: compiled eval is a separate
        // soft-dependency feature.
        var source = @"
            function probe(a: number, b: number): any {
                return eval('arguments.length');
            }
            console.log(probe(1, 2));
        ";
        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n", output);
    }
}

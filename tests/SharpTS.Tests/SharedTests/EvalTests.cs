using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for the global <c>eval()</c> function (issue #107).
/// <para>
/// Interpreter mode performs <b>direct eval</b>. Compiled mode lowers statically known,
/// expression-only source into the caller's scope and uses the indirect <c>EvalBridge</c>
/// fallback for dynamic or declaration-bearing source.
/// </para>
/// </summary>
public class EvalTests
{
    [Theory, ModeData]
    public void SuspendedEvalFunctionsCallLaterSiblings(ExecutionMode mode)
    {
        const string source = """
            function make() {
                return eval('function* entries() { yield later(); } async function read() { await 0; return later(); } function later() { return 9; } [entries, read];');
            }
            const pair = make();
            console.log(pair[0]().next().value);
            pair[1]().then(value => console.log(value));
            """;
        Assert.Equal("9\n9\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void EvalFunctionsShareForwardAndRecursiveBindings(ExecutionMode mode)
    {
        const string source = """
            function make() {
                return eval('function first() { return second; } function second(n) { return n ? first()(n-1) : second; } [first, second, value => { second = value; }];');
            }
            var pair = make();
            console.log(pair[0]() === pair[1], pair[1](2) === pair[1]);
            pair[2](() => 9);
            console.log(pair[0]()());
            """;
        Assert.Equal("true true\n9\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void GlobalEvalFunctionReadsTheSameArrayAsItsCaller(ExecutionMode mode)
    {
        const string source = """
            eval('var values = []; function add() { values.push(1); } add(); console.log(values.length);');
            console.log(globalThis.values === values);
            """;
        Assert.Equal("1\ntrue\n", TestHarness.RunUnchecked(source, mode));
    }

    // The interpreter's global eval write-through behavior is tracked by #3735.
    [Theory, CompiledOnlyData]
    public void GlobalEvalClosureObservesWritesThroughEitherReference(ExecutionMode mode)
    {
        const string source = """
            var action = eval('var n = 1; function bump() { n = n + 1; return n; } bump;');
            console.log(action(), n, globalThis.n);
            globalThis.n = 10;
            console.log(action(), n, globalThis.n);
            eval('var n;');
            console.log(n);
            """;
        Assert.Equal("2 2 2\n11 11 11\n11\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void StrictEvalOwnsDeclarationsAndEscapingClosures(ExecutionMode mode)
    {
        const string source = """
            var x = 10;
            function run() { 'use strict'; eval('var x = 3;'); console.log(x); }
            run(); console.log(x);
            console.log(eval('"use strict"; x; var x;') === undefined);
            function make(seed) { return eval('"use strict"; var x = seed; () => ++x;'); }
            var a = make(1), b = make(10);
            console.log(a(), b(), a(), x);
            """;
        Assert.Equal("10\n10\ntrue\n2 11 3 10\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void Eval_WhitespaceTolerance_ResolvesGlobals(ExecutionMode mode)
    {
        // Test262 S11.2.1_A1.1-style: eval tolerates interior whitespace and resolves globals.
        var source = """
            console.log(eval("Number\t.\tPOSITIVE_INFINITY") === Number.POSITIVE_INFINITY);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void Eval_ReturnsCompletionValueOfExpression(ExecutionMode mode)
    {
        var source = """
            console.log(eval("1 + 2 * 3"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("7\n", output);
    }

    [Theory, ModeData]
    public void Eval_NonStringArgument_ReturnedUnchanged(ExecutionMode mode)
    {
        // ECMA-262 §19.2.1: eval(non-string) returns the argument unchanged.
        var source = """
            console.log(eval(42));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    [Theory, InterpretedOnlyData]
    public void Eval_IsAvailableAsAFirstClassCallable(ExecutionMode mode)
    {
        var source = """
            const indirect = eval;
            console.log(typeof indirect);
            console.log(indirect(42));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("function\n42\n", output);
    }

    [Theory, ModeData]
    public void Eval_StatementsThenCompletionValue(ExecutionMode mode)
    {
        var source = """
            console.log(eval("var x = 10; x * x"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("100\n", output);
    }

    [Theory, ModeData]
    public void Eval_BuiltinMethodCalls(ExecutionMode mode)
    {
        var source = """
            console.log(eval("'abc'.toUpperCase()"));
            console.log(eval("Math.max(3, 7, 2)"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("ABC\n7\n", output);
    }

    [Theory, ModeData]
    public void Eval_StaticDirectEval_SeesCallerLocals(ExecutionMode mode)
    {
        // A literal expression source is compiled into the caller's lexical scope.
        var source = """
            function outer(): number {
                const secret: number = 7;
                return eval("secret + 1") as number;
            }
            console.log(outer());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("8\n", output);
    }

    [Theory, ModeData]
    public void Eval_StaticIndirectAlias_SeesScriptGlobalFunctions(ExecutionMode mode)
    {
        var source = """
            const indirect = eval;
            console.log(indirect("\"use strict\"; globalFunction();"));

            function globalFunction(): string {
                return "visible";
            }
            """;

        Assert.Equal("visible\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Eval_ReassignedAlias_RemainsAnOrdinaryFunctionCall(ExecutionMode mode)
    {
        var source = """
            let indirect: any = eval;
            indirect = (text: string): string => "ordinary:" + text;
            console.log(indirect("globalFunction()"));

            function globalFunction(): string {
                return "wrong";
            }
            """;

        Assert.Equal("ordinary:globalFunction()\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Eval_EvaluatesExtraArgumentsButUsesOnlyFirstSource(ExecutionMode mode)
    {
        var source = """
            let x: number = 0;
            let observed: number = 0;
            eval("x = 1", observed = 2);
            console.log(x);
            console.log(observed);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n", output);
    }

    [Theory, ModeData]
    public void Eval_EvaluatesCallArgumentsBeforeCallabilityCheck(ExecutionMode mode)
    {
        var source = """
            let called: boolean = false;
            function mark(): void { called = true; }
            const target = {};
            try { eval("target.missing(mark())"); } catch {}
            console.log(called);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }
}

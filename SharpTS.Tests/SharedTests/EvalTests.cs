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

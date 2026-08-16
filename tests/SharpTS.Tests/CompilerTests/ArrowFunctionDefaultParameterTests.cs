using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// #646: in compiled mode, function expressions and arrow functions must honor parameter defaults
/// for omitted arguments. They have no OverloadGenerator lower-arity forwarding, so a value-type
/// default slot (e.g. <c>number</c>) previously emitted invalid <c>ldarg; brfalse</c> IL and left
/// the parameter at its CLR zero value. The fix widens defaulted arrow/function-expression params
/// to object so the runtime entry prologue applies the default; async arrows additionally now emit
/// that prologue at all. These run in both modes to pin interpreter/compiler parity.
/// </summary>
public class ArrowFunctionDefaultParameterTests
{
    [Theory, ModeData]
    public void FunctionExpression_NumericDefault_OmittedArg(ExecutionMode mode)
    {
        var source = """
            const f = function (x: number, y: number = 3) { return x + y; };
            console.log(f(4));
            """;
        Assert.Equal("7\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Arrow_NumericDefault_OmittedArg(ExecutionMode mode)
    {
        var source = """
            const f = (x: number, y: number = 3) => x + y;
            console.log(f(4));
            """;
        Assert.Equal("7\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Arrow_NumericDefault_ArgPresent_DefaultNotApplied(ExecutionMode mode)
    {
        var source = """
            const f = (x: number, y: number = 3) => x + y;
            console.log(f(4, 10));
            """;
        Assert.Equal("14\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Arrow_BooleanDefault_OmittedArg(ExecutionMode mode)
    {
        var source = """
            const h = (on: boolean = true) => on ? "Y" : "N";
            console.log(h() + h(false));
            """;
        Assert.Equal("YN\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Arrow_StringDefault_OmittedArg(ExecutionMode mode)
    {
        var source = """
            const s = (a: string, b: string = "B") => a + b;
            console.log(s("A"));
            """;
        Assert.Equal("AB\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void CapturingArrow_NumericDefault_OmittedArg(ExecutionMode mode)
    {
        var source = """
            const base = 100;
            const cap = (n: number = 7) => base + n;
            console.log(cap() + "," + cap(1));
            """;
        Assert.Equal("107,101\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncArrow_NumericDefault_OmittedArg(ExecutionMode mode)
    {
        var source = """
            const af = async (x: number, y: number = 3) => x + y;
            af(4).then(v => console.log(v));
            """;
        Assert.Equal("7\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncArrow_DefaultWithAwait_OmittedArg(ExecutionMode mode)
    {
        var source = """
            const af = async (x: number, y: number = 9) => { await Promise.resolve(0); return x * y; };
            af(2).then(v => console.log(v));
            """;
        Assert.Equal("18\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void Arrow_ValueTypeDefault_ProducesVerifiableIL()
    {
        // Regression guard for the StackUnexpected IL error: a value-type defaulted arrow
        // parameter must not emit `ldarg; brfalse` on a double slot. (Behavioral correctness
        // is covered by the both-mode theory tests above via RunCompiled.)
        var source = """
            const f = function (x: number, y: number = 3) { return x + y; };
            console.log(f(4));
            """;
        var errors = TestHarness.CompileAndVerifyOnly(source);
        Assert.Empty(errors);
    }
}

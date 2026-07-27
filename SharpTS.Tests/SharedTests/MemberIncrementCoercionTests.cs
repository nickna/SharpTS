using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regression tests for #471: a prefix/postfix <c>++</c>/<c>--</c> on a member (<c>o.x</c>) or
/// index (<c>arr[i]</c>) l-value whose current value is not already a number applies ECMA-262
/// ToNumber — numeric strings parse (<c>"5"</c>→5), <c>undefined</c> becomes NaN — matching the
/// variable path and compiled mode. The interpreter previously hard-cast the boxed value to
/// <c>double</c>, throwing <c>InvalidCastException</c> on any non-number member/element (reachable
/// when the static type is <c>any</c> or otherwise widened to carry a non-number).
///
/// Runs against both modes: compiled mode already coerced via <c>ConvertToNumber</c>, so each test
/// doubles as an interpreter↔compiler parity check.
/// </summary>
public class MemberIncrementCoercionTests
{
    [Theory, ModeData]
    public void PostfixIncrement_NumericStringMember_ReturnsOldNumber_StoresIncremented(ExecutionMode mode)
    {
        // o.x = "5": (o.x++) is ToNumber("5") === 5; o.x becomes 6.
        var source = """
            const o: any = { x: "5" };
            const old = o.x++;
            console.log(old + "|" + o.x);
            """;
        Assert.Equal("5|6\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void PostfixIncrement_UndefinedMember_IsNaN(ExecutionMode mode)
    {
        // ToNumber(undefined) is NaN; NaN + 1 is NaN (no longer throws).
        var source = """
            const o: any = { x: undefined };
            o.x++;
            console.log(o.x);
            """;
        Assert.Equal("NaN\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void PrefixIncrement_NumericStringMember_ReturnsNewNumber(ExecutionMode mode)
    {
        var source = """
            const o: any = { x: "5" };
            const r = ++o.x;
            console.log(r + "|" + o.x);
            """;
        Assert.Equal("6|6\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void PostfixDecrement_NumericStringMember_Coerces(ExecutionMode mode)
    {
        var source = """
            const o: any = { x: "5" };
            o.x--;
            console.log(o.x);
            """;
        Assert.Equal("4\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void PostfixIncrement_NumericStringElement_Coerces(ExecutionMode mode)
    {
        // arr[i]++ has the identical divergence as o.x++.
        var source = """
            const a: any[] = ["7"];
            const old = a[0]++;
            console.log(old + "|" + a[0]);
            """;
        Assert.Equal("7|8\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void PostfixIncrement_NumericStringVariable_Coerces(ExecutionMode mode)
    {
        // The variable l-value path shared the same latent gap (it asserted a boxed double via
        // RuntimeValue.AsNumber); #471 routes all three l-value kinds through ToNumber.
        var source = """
            let v: any = "3";
            const old = v++;
            console.log(old + "|" + v);
            """;
        Assert.Equal("3|4\n", TestHarness.Run(source, mode));
    }
}

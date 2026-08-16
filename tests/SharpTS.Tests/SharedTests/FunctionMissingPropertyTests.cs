using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Reading a missing property off a function or arrow value yields JS
/// <c>undefined</c>, not CLR null — functions are ordinary objects (#651).
/// Plain objects were already correct in both modes; the gap was compiled-mode
/// only, where the <c>$TSFunction</c>/<c>$BoundTSFunction</c> property path
/// (<c>$Runtime.GetFunctionMethod</c>) fell through to null on a miss. The fix
/// returns the <c>undefined</c> sentinel from that miss path, so
/// <c>typeof fn.absent === "undefined"</c> matches the interpreter and Node.
/// </summary>
public class FunctionMissingPropertyTests
{
    [Theory, ModeData]
    public void MissingProperty_OffFunctionDeclaration_IsUndefined(ExecutionMode mode)
    {
        var source = """
            function fn() {}
            console.log(typeof (fn as any).missing);
            console.log((fn as any).missing === undefined);
            console.log((fn as any).missing === null);
            """;
        Assert.Equal("undefined\ntrue\nfalse\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void MissingProperty_OffArrow_IsUndefined(ExecutionMode mode)
    {
        var source = """
            const arrow: any = () => 1;
            console.log(typeof arrow.missing);
            console.log(arrow.missing === undefined);
            """;
        Assert.Equal("undefined\ntrue\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void UserAssignedProperty_StillRoundTrips_MissIsUndefined(ExecutionMode mode)
    {
        // The miss path must not disturb genuine user-assigned properties:
        // functions are objects, so `fn.x = v` writes/reads back, while an
        // absent sibling key still reads undefined.
        var source = """
            function fn() {}
            (fn as any).x = 42;
            console.log((fn as any).x);
            console.log(typeof (fn as any).y);
            """;
        Assert.Equal("42\nundefined\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void MissingProperty_OffConstructorReturnedArrow_IsUndefined(ExecutionMode mode)
    {
        // The discovery case from #651: a constructor returning an arrow yields
        // the arrow (a function value); a missing property off it is undefined.
        var source = """
            function Make(this: any, v: string): any {
                return () => "fn:" + v;
            }
            const f: any = new (Make as any)("X");
            console.log(typeof f);
            console.log(typeof f.someMissingProp);
            """;
        Assert.Equal("function\nundefined\n", TestHarness.Run(source, mode));
    }
}

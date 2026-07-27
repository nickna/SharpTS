using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Return-value semantics for plain (non-generator) functions and arrows: a function that
/// completes without an explicit <c>return &lt;expr&gt;</c> — a bare <c>return;</c> or falling off
/// the end — has return value <c>undefined</c>, distinct from <c>return null;</c> (which is null).
///
/// Both modes are spec-correct for synchronous functions/arrows:
/// <list type="bullet">
/// <item>Interpreter: a bare <c>return;</c> produces the <c>undefined</c> sentinel at its source
/// (<c>VisitReturn</c>) rather than C# null — the #480 root-cause fix.</item>
/// <item>Compiled: a bare <c>return;</c> into an object slot emits the <c>$Undefined</c> sentinel
/// (<c>ILEmitter.EmitReturn</c>), and an off-the-end body compiled to a <c>void</c> slot is
/// materialized as <c>undefined</c> at the call site (<c>BoxReturnValueIfNeeded</c>) — the #563 fix.</item>
/// </list>
///
/// Async (non-generator) functions are a separate path: the interpreter is correct for a bare
/// <c>return;</c> (<c>ExecuteReturnAsyncVT</c>, #480) but the compiled async state machine and the
/// off-the-end completion still yield <c>null</c>, tracked by #587. Async generators are #540.
/// </summary>
public class FunctionReturnValueTests
{
    [Theory, ModeData]
    public void BareReturn_IsUndefined(ExecutionMode mode)
    {
        // Bare `return;` is equivalent to `return undefined` — undefined, not null.
        var source = """
            function f() { return; }
            console.log(typeof f());
            console.log(f() === undefined);
            console.log(f() === null);
            """;
        Assert.Equal("undefined\ntrue\nfalse\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void OffEndCompletion_IsUndefined(ExecutionMode mode)
    {
        // Falling off the end of the body completes with undefined. In compiled mode such a
        // body is emitted into a `void` slot, so this also covers the call-site materialization
        // of a void return as undefined (#563).
        var source = """
            function f() {}
            console.log(typeof f());
            console.log(f() === undefined);
            """;
        Assert.Equal("undefined\ntrue\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ArrowBareReturn_IsUndefined(ExecutionMode mode)
    {
        // A block-bodied arrow shares the same return emission as a function declaration, so a
        // bare `return;` is undefined in both modes too.
        var source = """
            const f = () => { return; };
            console.log(typeof f());
            console.log(f() === undefined);
            """;
        Assert.Equal("undefined\ntrue\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ReturnNull_IsNull(ExecutionMode mode)
    {
        // `return null;` is distinct from a bare `return;`: the value is JS null, not undefined.
        var source = """
            function f() { return null; }
            console.log(typeof f());
            console.log(f() === null);
            """;
        Assert.Equal("object\ntrue\n", TestHarness.Run(source, mode));
    }

    [Theory, InterpretedOnlyData]
    public void AsyncFunction_BareReturn_ResolvesUndefined(ExecutionMode mode)
    {
        // The async return path (ExecuteReturnAsyncVT) got the same fix as the sync VisitReturn:
        // a bare `return;` resolves the promise with undefined, not null. The compiled async state
        // machine (and async off-the-end completion in both modes) still yields null — tracked by
        // #587 — so this is interpreter-only. Async generators are a separate path tracked by #540.
        var source = """
            async function f() { return; }
            f().then(v => console.log(typeof v + ":" + (v === undefined)));
            """;
        Assert.Equal("undefined:true\n", TestHarness.Run(source, mode));
    }
}

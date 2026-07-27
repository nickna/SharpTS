using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regression tests for the guest-error identity fix at the interpreter's catch binding.
///
/// Two failure modes are pinned:
/// <list type="bullet">
/// <item><b>Over-typing</b>: a genuine guest <c>throw "RangeError: ..."</c> — a bare string that
/// merely looks like a runtime error message — must be caught verbatim as a string, never
/// re-typed into an <c>Error</c>. Coercion now applies only to translated <em>host</em>
/// exceptions, not to guest <c>throw</c> values.</item>
/// <item><b>Under-typing</b>: an internal runtime error (surfaced as a <c>"Runtime Error: ..."</c>
/// host message) caught by guest code must present as an <c>Error</c> instance so
/// <c>instanceof</c>/<c>.message</c> hold, matching JS and compiled mode — the prefix table
/// previously omitted the <c>"Runtime Error: "</c> wrapper.</item>
/// </list>
/// </summary>
public class CaughtErrorIdentityTests
{
    [Theory, ModeData]
    public void GuestThrownErrorShapedString_IsCaughtVerbatim_NotReTyped(ExecutionMode mode)
    {
        // A guest throw of an error-prefixed string is a plain string value in JS — it must NOT
        // be reconstructed into a typed Error by the catch binding's host-error recovery.
        var source = """
            try { throw "RangeError: hand-rolled, not a real error"; }
            catch (e: any) {
              console.log(typeof e);
              console.log(e);
              console.log(e instanceof Error);
            }
            """;

        Assert.Equal("string\nRangeError: hand-rolled, not a real error\nfalse\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void InternalRuntimeError_CaughtByGuest_IsErrorInstance(ExecutionMode mode)
    {
        // `1n ** -1n` is a runtime error in both modes; caught by guest code it must be an Error
        // instance (not a raw string), at parity between interpreter and compiled output.
        var source = """
            try { const r = 1n ** -1n; console.log("unreachable", r); }
            catch (e: any) { console.log(e instanceof Error); }
            """;

        Assert.Equal("true\n", TestHarness.Run(source, mode));
    }

    [Theory, InterpretedOnlyData]
    public void InternalRuntimeError_CaughtByGuest_RecoversTypedError(ExecutionMode mode)
    {
        // Interpreter-specific: the "Runtime Error: " wrapper with no inner JS error name is
        // recovered as a generic Error carrying the unwrapped message. (Compiled mode surfaces a
        // different underlying .NET message, so the exact text is asserted for the interpreter only.)
        var source = """
            try { const r = 1n ** -1n; console.log("unreachable", r); }
            catch (e: any) {
              console.log(e.name);
              console.log(e.message);
            }
            """;

        Assert.Equal("Error\nBigInt exponent must be non-negative.\n", TestHarness.Run(source, mode));
    }

    // ---- Cross-boundary residual (#694 follow-up) ----------------------------------------------
    // A guest `throw "TypeError: x"` that crosses a host C# frame (a plain function-call return,
    // a host built-in callback, a Promise executor) must still be caught verbatim as a string —
    // not flattened to a host Exception and re-typed at the downstream catch. These crossed the
    // boundary correctly in compiled mode already; the fix brings the interpreter to parity.

    [Theory, ModeData]
    public void GuestStringThrow_AcrossFunctionCall_IsCaughtVerbatim(ExecutionMode mode)
    {
        var source = """
            function f(): void { throw "TypeError: via-fn"; }
            try { f(); }
            catch (e: any) {
              console.log(typeof e);
              console.log(e instanceof Error);
              console.log(e);
            }
            """;

        Assert.Equal("string\nfalse\nTypeError: via-fn\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void GuestStringThrow_AcrossHostCallback_IsCaughtVerbatim(ExecutionMode mode)
    {
        // Array.forEach invokes the guest callback through a host frame — the throw must cross it
        // without losing its string identity.
        var source = """
            try { [1].forEach(() => { throw "TypeError: in-forEach"; }); }
            catch (e: any) {
              console.log(typeof e);
              console.log(e instanceof Error);
              console.log(e);
            }
            """;

        Assert.Equal("string\nfalse\nTypeError: in-forEach\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void GuestErrorObject_AcrossHostCallback_KeepsIdentity(ExecutionMode mode)
    {
        // Anchor: a genuine Error object thrown across the same host frame must remain an Error
        // (the object path was never broken — this guards against the fix over-correcting).
        var source = """
            try { [1].forEach(() => { throw new RangeError("boom"); }); }
            catch (e: any) {
              console.log(typeof e);
              console.log(e instanceof Error);
              console.log(e instanceof RangeError);
            }
            """;

        Assert.Equal("object\ntrue\ntrue\n", TestHarness.Run(source, mode));
    }
}

using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// ECMA-262 RequireObjectCoercible (§13.3.2 / §13.3.3) and PutValue (§13.15.5):
/// a non-optional member access on <c>undefined</c> or <c>null</c> must throw a
/// guest <c>TypeError</c> ("Cannot read|set properties of &lt;undefined|null&gt;
/// (reading|setting '&lt;key&gt;')"), not silently yield <c>undefined</c>,
/// silently no-op, or surface a raw host "Runtime Error" string.
///
/// <para>#676 (interpreter reads): the property-access fallback threw a host
/// <c>InterpreterException</c> that a guest <c>catch</c> bound as a plain string.</para>
/// <para>#701 (compiled reads on <c>undefined</c>): the emitted property/index read
/// silently evaluated to <c>undefined</c> instead of throwing.</para>
/// <para>#735 (compiled reads on a genuine <c>null</c>): compiled mode represented
/// sloppy-mode <c>this</c> as CLR <c>null</c>, so the guards could not reject a
/// value-position <c>null</c> without breaking <c>this.x</c>. Compiled sloppy
/// <c>this</c> now resolves to the globalThis sentinel, so <c>null</c> is unambiguous
/// and reads on it throw — matching the interpreter on both <c>null</c> and
/// <c>undefined</c>.</para>
/// <para>#733 (writes on <c>null</c>/<c>undefined</c>): both the interpreter (which
/// threw a host <c>InterpreterException</c>) and the compiler (which silently
/// no-op'd) now throw a guest <c>TypeError</c> via PutValue's RequireObjectCoercible.</para>
/// </summary>
public class NullishMemberAccessTests
{
    // The guest harness prints the caught value's shape so we assert the guest
    // sees a real TypeError (object + instanceof + name), plus the message.
    private const string ProbeFull =
        "catch (e: any) { console.log(typeof e, e instanceof TypeError, e.name, \"|\" + e.message); }";
    private const string ProbeIdentity =
        "catch (e: any) { console.log(typeof e, e instanceof TypeError, e.name); }";

    // ----- undefined receiver: dot read (#676 / #701 core) -----

    [Theory, ModeData]
    public void UndefinedDotRead_ThrowsTypeError(ExecutionMode mode)
    {
        var source = $$"""
            const o: any = undefined;
            try { const y = o.foo; console.log("NOTHROW " + y); }
            {{ProbeFull}}
            """;
        Assert.Equal(
            "object true TypeError |Cannot read properties of undefined (reading 'foo')\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void UndefinedBracketRead_StringKey_ThrowsTypeError(ExecutionMode mode)
    {
        var source = $$"""
            const o: any = undefined;
            try { const y = o["bar"]; console.log("NOTHROW " + y); }
            {{ProbeFull}}
            """;
        Assert.Equal(
            "object true TypeError |Cannot read properties of undefined (reading 'bar')\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void UndefinedBracketRead_NumericKey_ThrowsTypeError(ExecutionMode mode)
    {
        var source = $$"""
            const o: any = undefined;
            try { const y = o[0]; console.log("NOTHROW " + y); }
            {{ProbeFull}}
            """;
        Assert.Equal(
            "object true TypeError |Cannot read properties of undefined (reading '0')\n",
            TestHarness.Run(source, mode));
    }

    // ----- null receiver: dot + bracket read (#735) -----

    [Theory, ModeData]
    public void NullDotRead_ThrowsTypeError(ExecutionMode mode)
    {
        var source = $$"""
            const o: any = null;
            try { const y = o.foo; console.log("NOTHROW " + y); }
            {{ProbeFull}}
            """;
        Assert.Equal(
            "object true TypeError |Cannot read properties of null (reading 'foo')\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NullBracketRead_ThrowsTypeError(ExecutionMode mode)
    {
        var source = $$"""
            const o: any = null;
            try { const y = o["bar"]; console.log("NOTHROW " + y); }
            {{ProbeFull}}
            """;
        Assert.Equal(
            "object true TypeError |Cannot read properties of null (reading 'bar')\n",
            TestHarness.Run(source, mode));
    }

    // ----- null/undefined receiver: method call (the #676 repro shape) -----

    [Theory, ModeData]
    public void UndefinedMethodCall_ThrowsTypeError(ExecutionMode mode)
    {
        var source = $$"""
            const o: any = undefined;
            try { o.foo(); console.log("NOTHROW"); }
            {{ProbeIdentity}}
            """;
        Assert.Equal("object true TypeError\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NullMethodCall_ThrowsTypeError(ExecutionMode mode)
    {
        var source = $$"""
            const o: any = null;
            try { o.foo(); console.log("NOTHROW"); }
            {{ProbeIdentity}}
            """;
        Assert.Equal("object true TypeError\n", TestHarness.Run(source, mode));
    }

    // ----- writes on null/undefined (#733) -----

    [Theory, ModeData]
    public void UndefinedDotWrite_ThrowsTypeError(ExecutionMode mode)
    {
        var source = $$"""
            const o: any = undefined;
            try { o.foo = 1; console.log("NOTHROW"); }
            {{ProbeFull}}
            """;
        Assert.Equal(
            "object true TypeError |Cannot set properties of undefined (setting 'foo')\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NullDotWrite_ThrowsTypeError(ExecutionMode mode)
    {
        var source = $$"""
            const o: any = null;
            try { o.foo = 1; console.log("NOTHROW"); }
            {{ProbeFull}}
            """;
        Assert.Equal(
            "object true TypeError |Cannot set properties of null (setting 'foo')\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NullBracketWrite_ThrowsTypeError(ExecutionMode mode)
    {
        var source = $$"""
            const o: any = null;
            try { o["bar"] = 1; console.log("NOTHROW"); }
            {{ProbeFull}}
            """;
        Assert.Equal(
            "object true TypeError |Cannot set properties of null (setting 'bar')\n",
            TestHarness.Run(source, mode));
    }

    // PutValue follows RHS evaluation: the RHS side effect must run before the throw.
    [Theory, ModeData]
    public void NullWrite_RHS_SideEffect_Runs_BeforeThrow(ExecutionMode mode)
    {
        var source = """
            const o: any = null;
            let ran = false;
            try { o.foo = (ran = true, 1); }
            catch (e: any) { console.log("threw ran=" + ran); }
            """;
        Assert.Equal("threw ran=true\n", TestHarness.Run(source, mode));
    }

    // ----- compound / logical writes on null/undefined (#733) -----
    // These read the property first (GetValue), so per spec they throw the
    // *read*-worded message, NOT the "setting" wording of a plain `o.x = v`.

    [Theory, ModeData]
    public void UndefinedDotCompound_ThrowsTypeError(ExecutionMode mode)
    {
        var source = $$"""
            const o: any = undefined;
            try { o.foo += 1; console.log("NOTHROW"); }
            {{ProbeFull}}
            """;
        Assert.Equal(
            "object true TypeError |Cannot read properties of undefined (reading 'foo')\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NullDotCompound_ThrowsTypeError(ExecutionMode mode)
    {
        var source = $$"""
            const o: any = null;
            try { o.foo += 1; console.log("NOTHROW"); }
            {{ProbeFull}}
            """;
        Assert.Equal(
            "object true TypeError |Cannot read properties of null (reading 'foo')\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void UndefinedDotLogicalNullish_ThrowsTypeError(ExecutionMode mode)
    {
        var source = $$"""
            const o: any = undefined;
            try { o.foo ??= 1; console.log("NOTHROW"); }
            {{ProbeFull}}
            """;
        Assert.Equal(
            "object true TypeError |Cannot read properties of undefined (reading 'foo')\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NullDotLogicalOr_ThrowsTypeError(ExecutionMode mode)
    {
        var source = $$"""
            const o: any = null;
            try { o.foo ||= 1; console.log("NOTHROW"); }
            {{ProbeFull}}
            """;
        Assert.Equal(
            "object true TypeError |Cannot read properties of null (reading 'foo')\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void UndefinedIndexCompound_ThrowsTypeError(ExecutionMode mode)
    {
        var source = $$"""
            const o: any = undefined;
            try { o[0] += 1; console.log("NOTHROW"); }
            {{ProbeFull}}
            """;
        Assert.Equal(
            "object true TypeError |Cannot read properties of undefined (reading '0')\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NullIndexLogicalNullish_ThrowsTypeError(ExecutionMode mode)
    {
        var source = $$"""
            const o: any = null;
            try { o[0] ??= 1; console.log("NOTHROW"); }
            {{ProbeFull}}
            """;
        Assert.Equal(
            "object true TypeError |Cannot read properties of null (reading '0')\n",
            TestHarness.Run(source, mode));
    }

    // Exercises the base (state-machine) compound/logical emitter + EvaluateLogicalSetAsync.
    [Theory, ModeData]
    public void UndefinedDotLogical_InsideAsync_ThrowsTypeError(ExecutionMode mode)
    {
        var source = $$"""
            async function run(): Promise<void> {
                const o: any = undefined;
                try { o.foo ??= 1; console.log("NOTHROW"); }
                {{ProbeFull}}
            }
            run();
            """;
        Assert.Equal(
            "object true TypeError |Cannot read properties of undefined (reading 'foo')\n",
            TestHarness.Run(source, mode));
    }

    // Regression: compound/logical assignment on a real receiver still works.
    [Theory, ModeData]
    public void DefinedReceiver_CompoundAndLogical_StillWork(ExecutionMode mode)
    {
        var source = """
            const o: any = { n: 1, arr: [10] };
            o.n += 4;
            o.missing ??= 7;
            o.arr[0] += 5;
            console.log(o.n, o.missing, o.arr[0]);
            """;
        Assert.Equal("5 7 15\n", TestHarness.Run(source, mode));
    }

    // ----- async context exercises the base (state-machine) emitter -----

    [Theory, ModeData]
    public void UndefinedDotRead_InsideAsync_ThrowsTypeError(ExecutionMode mode)
    {
        var source = $$"""
            async function run(): Promise<void> {
                const o: any = undefined;
                try { const y = o.foo; console.log("NOTHROW " + y); }
                {{ProbeFull}}
            }
            run();
            """;
        Assert.Equal(
            "object true TypeError |Cannot read properties of undefined (reading 'foo')\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NullDotRead_InsideAsync_ThrowsTypeError(ExecutionMode mode)
    {
        var source = $$"""
            async function run(): Promise<void> {
                const o: any = null;
                try { const y = o.foo; console.log("NOTHROW " + y); }
                {{ProbeFull}}
            }
            run();
            """;
        Assert.Equal(
            "object true TypeError |Cannot read properties of null (reading 'foo')\n",
            TestHarness.Run(source, mode));
    }

    // ----- sloppy-mode `this` must stay coercible (regression for the #735 fix) -----
    // Compiled sloppy `this` now resolves to the globalThis sentinel (mirroring the
    // interpreter's SharpTSGlobalThis.Instance binding), so `this.x` must NOT trip
    // the new null/undefined guard, and writes via `this.x = v` round-trip through
    // the global object.

    [Theory, ModeData]
    public void SloppyThis_Read_DoesNotThrow(ExecutionMode mode)
    {
        var source = """
            function probe(): any { return this.missing; }
            console.log(probe() === undefined ? "ok-undefined" : "WRONG:" + probe());
            """;
        Assert.Equal("ok-undefined\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void SloppyThis_Write_RoutesToGlobalThis(ExecutionMode mode)
    {
        var source = """
            function setViaThis(): void { (this as any).__sloppyProbe735 = 42; }
            setViaThis();
            console.log((globalThis as any).__sloppyProbe735);
            """;
        Assert.Equal("42\n", TestHarness.Run(source, mode));
    }

    // The async state-machine emitter resolves `this` via the base EmitThis path
    // (LoadThis → globalThis sentinel), so a sloppy `this.x` read inside async
    // must not trip the new null guard. Interpreter-only limitation: top-level
    // bare async calls don't bind `this` there (pre-existing, tracked separately),
    // so this is compiled-only.
    [Fact]
    public void SloppyThis_Read_InsideAsync_DoesNotThrow_Compiled()
    {
        var source = """
            async function probe(): Promise<any> { return (this as any).missing; }
            probe().then((v: any) => console.log(v === undefined ? "ok-undefined" : "WRONG:" + v));
            """;
        Assert.Equal("ok-undefined\n", TestHarness.RunCompiled(source));
    }

    // ----- regression: optional chaining still short-circuits (does NOT throw) -----

    [Theory, ModeData]
    public void OptionalDotChain_OnUndefined_ReturnsUndefined(ExecutionMode mode)
    {
        var source = """
            const o: any = undefined;
            const y = o?.foo;
            console.log(y === undefined ? "undefined-ok" : "WRONG:" + y);
            """;
        Assert.Equal("undefined-ok\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void OptionalBracketChain_OnUndefined_ReturnsUndefined(ExecutionMode mode)
    {
        var source = """
            const o: any = undefined;
            const y = o?.["bar"];
            console.log(y === undefined ? "undefined-ok" : "WRONG:" + y);
            """;
        Assert.Equal("undefined-ok\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void OptionalDotChain_OnNull_ReturnsUndefined(ExecutionMode mode)
    {
        var source = """
            const o: any = null;
            const y = o?.foo;
            console.log(y === undefined ? "undefined-ok" : "WRONG:" + y);
            """;
        Assert.Equal("undefined-ok\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void OptionalDotChain_OnUndefined_InsideAsync_ReturnsUndefined(ExecutionMode mode)
    {
        // Exercises the base EmitGet optional short-circuit added for the
        // async/generator state-machine emitters alongside the #701 guard.
        var source = """
            async function run(): Promise<void> {
                const o: any = undefined;
                const y = o?.foo;
                console.log(y === undefined ? "undefined-ok" : "WRONG:" + y);
            }
            run();
            """;
        Assert.Equal("undefined-ok\n", TestHarness.Run(source, mode));
    }

    // ----- regression: real reads still work after the guard -----

    [Theory, ModeData]
    public void DefinedReceiver_DotAndBracketRead_StillWork(ExecutionMode mode)
    {
        var source = """
            const o: any = { foo: 42, "1": "one" };
            console.log(o.foo, o["foo"], o[1]);
            """;
        Assert.Equal("42 42 one\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void DefinedReceiver_DotWrite_StillWorks(ExecutionMode mode)
    {
        var source = """
            const o: any = { foo: 1 };
            o.foo = 99;
            console.log(o.foo);
            """;
        Assert.Equal("99\n", TestHarness.Run(source, mode));
    }
}

using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regression tests for #443: a compiled generator must produce the JavaScript <c>undefined</c>
/// sentinel — not CLR <c>null</c> or a stale value — for
/// <list type="bullet">
/// <item>the value a resumed <c>yield</c> expression evaluates to when no value is threaded back
/// in (every <c>for…of</c> / no-argument <c>.next()</c> resume), and</item>
/// <item>the completion value of a delegating <c>yield* expr</c>.</item>
/// </list>
/// Before the fix the compiler emitted <c>ldnull</c> at the resume point and for the <c>yield*</c>
/// completion, so e.g. <c>"x:" + (yield 1)</c> resumed to <c>"x:null"</c> instead of
/// <c>"x:undefined"</c>, and <c>yield* inner()</c> over a no-return generator produced the last
/// yielded value instead of <c>undefined</c>. The interpreter was already correct for these cases,
/// so each test below asserts identical output across both modes.
/// </summary>
public class GeneratorUndefinedValueTests
{
    // ---- Resumed `yield` value (the issue's primary case) ----

    [Theory, ModeData]
    public void ResumedYield_ConcatenatedAsString_IsUndefined(ExecutionMode mode)
    {
        // The repro from #443: for…of resumes with no sent value, so `yield 1` evaluates to undefined.
        var source = """
            function* g() { console.log("x:" + (yield 1)); }
            for (const v of g()) {}
            """;
        Assert.Equal("x:undefined\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ResumedYield_StoredInLocal_IsUndefined(ExecutionMode mode)
    {
        var source = """
            function* g() { const r = yield 1; console.log("r=" + r); }
            for (const v of g()) {}
            """;
        Assert.Equal("r=undefined\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ResumedYield_InArithmetic_IsNaN(ExecutionMode mode)
    {
        // undefined + 10 === NaN; exercises numeric coercion of the resumed sentinel, not just string.
        var source = """
            function* g() { console.log((yield 1) + 10); }
            for (const v of g()) {}
            """;
        Assert.Equal("NaN\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void MultipleResumedYields_AreEachUndefined(ExecutionMode mode)
    {
        var source = """
            function* g() { console.log("t:" + (yield 1) + "|" + (yield 2)); }
            for (const v of g()) {}
            """;
        Assert.Equal("t:undefined|undefined\n", TestHarness.Run(source, mode));
    }

    // ---- `yield*` completion value ----

    [Theory, ModeData]
    public void YieldStarOverArray_CompletionIsUndefined(ExecutionMode mode)
    {
        // A plain array has no return value, so the yield* completion value is undefined.
        var source = """
            function* g() { console.log("x:" + (yield* [1, 2])); }
            for (const v of g()) {}
            """;
        Assert.Equal("x:undefined\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void YieldStarOverString_CompletionIsUndefined(ExecutionMode mode)
    {
        var source = """
            function* g() { console.log("x:" + (yield* "ab")); }
            for (const v of g()) {}
            """;
        Assert.Equal("x:undefined\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void YieldStarOverGeneratorNoReturn_CompletionIsUndefined(ExecutionMode mode)
    {
        // inner runs off the end (no `return`), so the yield* completion value is undefined —
        // not the last yielded value (2), which the compiler previously produced.
        var source = """
            function* inner() { yield 1; yield 2; }
            function* g() { console.log("x:" + (yield* inner())); }
            for (const v of g()) {}
            """;
        Assert.Equal("x:undefined\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void YieldStarOverGeneratorBareReturn_CompletionIsUndefined(ExecutionMode mode)
    {
        var source = """
            function* inner() { yield 1; return; }
            function* g() { console.log("x:" + (yield* inner())); }
            for (const v of g()) {}
            """;
        Assert.Equal("x:undefined\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void YieldStarOverGeneratorWithReturn_CompletionIsReturnValue(ExecutionMode mode)
    {
        // The fix must not regress the case that already worked: an explicit `return <value>`
        // is the yield* completion value.
        var source = """
            function* inner() { yield 1; return "RET"; }
            function* g() { const r = yield* inner(); console.log("got:" + r); }
            for (const v of g()) {}
            """;
        Assert.Equal("got:RET\n", TestHarness.Run(source, mode));
    }

    // ---- Direct `.next().value` completion value (#480) ----
    // A generator that completes without an explicit `return <expr>` has completion value
    // `undefined` (ECMA-262 27.5.1.x): both falling off the end and a bare `return;`. Both modes
    // are now spec-correct — the interpreter previously reported `null` for the bare-`return;` case
    // (#480), fixed by making a bare `return;` produce the `undefined` sentinel at its source
    // (VisitReturn) rather than C# null, while preserving `return null;` as null (see below).

    [Theory, ModeData]
    public void OffEndCompletionValue_IsUndefined(ExecutionMode mode)
    {
        var source = """
            function* g() { yield 1; }
            const it = g();
            console.log("a:" + it.next().value);
            console.log("b:" + it.next().value);
            """;
        Assert.Equal("a:1\nb:undefined\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void BareReturnCompletionValue_IsUndefined(ExecutionMode mode)
    {
        var source = """
            function* g() { yield 1; return; }
            const it = g();
            console.log("a:" + it.next().value);
            console.log("b:" + it.next().value);
            """;
        Assert.Equal("a:1\nb:undefined\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ReturnNullCompletionValue_IsNull(ExecutionMode mode)
    {
        // `return null;` is distinct from a bare `return;`: the completion value is JS null, not
        // undefined. Guards two things cross-mode: the interpreter #480 fix must not conflate "no
        // return value" with "returned null", and the compiled generator return path must preserve
        // null (#489 — compiled `return null` previously produced an undefined completion value;
        // fixed by reading the stored CurrentField on the done path rather than emitting null).
        var source = """
            function* g() { yield 1; return null; }
            const it = g();
            console.log("a:" + it.next().value);
            console.log("b:" + it.next().value);
            """;
        Assert.Equal("a:1\nb:null\n", TestHarness.Run(source, mode));
    }

    // ---- #499: `next()` on an already-completed generator yields undefined ----
    // After a generator completes, every subsequent `.next()` must report
    // `{ value: undefined, done: true }` (ECMA-262 27.5.1.2). The compiled state machine
    // previously left a stale value in its Current field on the already-completed re-entry path
    // (state == -2 → `_returnFalseLabel`), so `.next()` re-surfaced the last `return`ed or yielded
    // value. The interpreter is already correct for these cases, so they assert cross-mode parity.

    [Theory, ModeData]
    public void NextAfterExplicitReturn_IsUndefined(ExecutionMode mode)
    {
        // `return 42` is surfaced exactly once; the next `.next()` reports undefined, not 42.
        var source = """
            function* g() { yield 1; return 42; }
            const it = g();
            console.log("a:" + it.next().value);
            console.log("b:" + it.next().value);
            console.log("c:" + it.next().value);
            console.log("d:" + it.next().value);
            """;
        Assert.Equal("a:1\nb:42\nc:undefined\nd:undefined\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NextAfterReturnMethod_IsUndefined(ExecutionMode mode)
    {
        // `gen.return(99)` closes the generator with 99; the next `.next()` reports undefined,
        // not the stale last-yielded value (1) that the compiler previously leaked.
        var source = """
            function* g() { yield 1; yield 2; }
            const it = g();
            it.next();
            const r = it.return(99);
            console.log("ret:" + r.value + "," + r.done);
            console.log("after:" + it.next().value);
            """;
        Assert.Equal("ret:99,true\nafter:undefined\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NextAfterOffEndCompletion_StaysUndefined(ExecutionMode mode)
    {
        // Repeated `.next()` past a no-return completion keeps reporting undefined (never the
        // last yielded value). This is the issue's literal repro, extended past the first done.
        var source = """
            function* g() { yield 1; yield 11; }
            const it = g();
            it.next();
            it.next();
            console.log("a:" + it.next().value);
            console.log("b:" + it.next().value);
            """;
        Assert.Equal("a:undefined\nb:undefined\n", TestHarness.Run(source, mode));
    }
}

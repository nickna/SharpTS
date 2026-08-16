using System;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// The dedicated single/dual-argument built-in container records — <c>IterableIterator&lt;T&gt;</c> /
/// <c>Iterator&lt;T&gt;</c> (=> <c>TypeInfo.Iterator</c>), <c>WeakRef&lt;T&gt;</c>,
/// <c>FinalizationRegistry&lt;T&gt;</c> — now resolve from a type REFERENCE instead of degrading to
/// <c>any</c> (#456, completing the Map/Set/WeakMap/WeakSet work in #347/PR&#160;#455). Resolving them
/// exposed that the same-kind records lacked a compatibility arm: <c>Iterator</c>,
/// <c>Generator</c>/<c>AsyncGenerator</c> and <c>FinalizationRegistry</c> all fell through to
/// <c>return false</c>, so even <c>let g: Generator&lt;number&gt; = gen()</c> spuriously failed. The
/// arms added here close that, with a sync <c>Generator</c> accepted where an <c>Iterator</c> is
/// expected (it structurally IS one) so previously-<c>any</c> assignments do not regress.
/// </summary>
public class DedicatedContainerTypeReferenceTests
{
    // ---- IterableIterator / Iterator references are strongly typed (no longer `any`) ----

    [Fact]
    public void IterableIterator_FakeMemberRejected()
    {
        // The issue's headline example: `it` is now Iterator<number>, not `any`, so an unknown member
        // is a type error instead of silently passing.
        var source = """
            let it: IterableIterator<number> = [1, 2, 3].values();
            it.totallyFakeMethod();
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void Iterator_AltSpelling_FakeMemberRejected()
    {
        var source = """
            let it: Iterator<number> = [1, 2, 3].values();
            it.nope();
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void IterableIterator_FromArrayValues_Accepted()
    {
        var source = "let it: IterableIterator<number> = [1, 2, 3].values();";
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void IterableIterator_FromMapEntries_Accepted()
    {
        // Map.entries() yields IterableIterator<[K, V]>; the tuple element type must line up.
        var source = """
            const m = new Map<string, number>();
            let it: IterableIterator<[string, number]> = m.entries();
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void Iterator_ElementMismatch_Rejected()
    {
        // The element type is now real: an Iterator<number> source cannot satisfy IterableIterator<string>.
        var source = """
            function f(it: IterableIterator<number>): IterableIterator<string> { return it; }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void Iterator_ThreeTypeArgs_LibSpelling_Accepted()
    {
        // lib.d.ts is Iterator<T, TReturn = any, TNext = any>; SharpTS keeps only the element type but
        // must accept (and ignore) the optional TReturn/TNext, the same way Generator drops them.
        var source = "let it: Iterator<number, void, undefined> = [1].values();";
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void Iterator_TooManyTypeArgs_Rejected()
    {
        // The lib signature defaults TReturn/TNext, so an over-arity reference is the range error TS2707
        // ("requires between …"), the code tsc uses for a defaulted range — not the exact-count TS2314 (#487).
        var source = "let it: Iterator<number, void, undefined, string> = [1].values();";
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Equal("TS2707", ex.Diagnostic.TsCode);
    }

    // ---- Generator <-> Iterator direction (a sync Generator IS an IterableIterator) ----

    [Fact]
    public void Generator_SelfAssignment_Accepted()
    {
        // Regression of the bug resolving these records exposed: a same-record pair with no
        // compatibility arm fell through to `return false`, so this failed before #456.
        var source = """
            function* gen(): Generator<number> { yield 1; }
            let g: Generator<number> = gen();
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void AsyncGenerator_SelfAssignment_Accepted()
    {
        var source = """
            async function* gen(): AsyncGenerator<number> { yield 1; }
            let g: AsyncGenerator<number> = gen();
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void Generator_ThreeTypeArgs_LibSpelling_Accepted()
    {
        // lib.d.ts is Generator<T, TReturn = any, TNext = unknown>; SharpTS keeps only the yield type but
        // must accept (and ignore) the optional TReturn/TNext rather than rejecting "requires exactly 1"
        // (#487). This is the issue's exact repro.
        var source = """
            function* gen(): Generator<number, void, unknown> { yield 1; }
            console.log([...gen()]);
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void Generator_TooManyTypeArgs_Rejected()
    {
        // Beyond the defaulted range (T, TReturn, TNext) is the range error TS2707, not the exact-count
        // TS2314 (#487) — mirrors the Iterator arm.
        var source = "function* gen(): Generator<number, void, unknown, string> { yield 1; }";
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Equal("TS2707", ex.Diagnostic.TsCode);
    }

    [Fact]
    public void AsyncGenerator_ThreeTypeArgs_LibSpelling_Accepted()
    {
        // AsyncGenerator<T, TReturn = any, TNext = unknown> — same defaulted range as Generator (#487).
        var source = """
            async function* gen(): AsyncGenerator<number, void, unknown> { yield 1; }
            let g: AsyncGenerator<number> = gen();
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void Generator_AssignableToIterableIterator_Accepted()
    {
        // Generator<T> extends IterableIterator<T>; without this arm the assignment would regress now
        // that IterableIterator<number> is strongly typed rather than `any`.
        var source = """
            function* gen(): Generator<number> { yield 1; }
            let it: IterableIterator<number> = gen();
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void StructuralIteratorObject_AssignableToIterator_Accepted()
    {
        // TS types Iterator<T> structurally: a hand-written object with a `next` method satisfies it.
        // Guards the regression where making Iterator<T> a real type (was `any`) rejected such objects.
        var source = """
            function make(): Iterator<number> {
              let i = 0;
              return {
                next(): IteratorResult<number> {
                  return i < 3 ? { value: ++i, done: false } : { value: 0, done: true };
                }
              };
            }
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void Iterator_NotAssignableToGenerator_Rejected()
    {
        // The relation is asymmetric: a plain Iterator is not a Generator.
        var source = """
            function f(it: IterableIterator<number>): Generator<number> { return it; }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void AsyncGenerator_NotAssignableToSyncIterator_Rejected()
    {
        // Sync and async iterator hierarchies are distinct: an AsyncGenerator is not a sync Iterator.
        var source = """
            async function* agen(): AsyncGenerator<number> { yield 1; }
            function f(): IterableIterator<number> { return agen(); }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    // ---- WeakRef references are strongly typed ----

    [Fact]
    public void WeakRef_DerefReturnsTargetOrUndefined()
    {
        // deref() returns T | undefined; reading a property without narrowing must be rejected under
        // strictNullChecks because `undefined` has no members.
        var source = """
            let wr: WeakRef<{ name: string }> = new WeakRef({ name: "x" });
            let n: string = wr.deref().name;
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void WeakRef_DerefWithNarrowing_Accepted()
    {
        var source = """
            let wr: WeakRef<{ name: string }> = new WeakRef({ name: "x" });
            let n: string = wr.deref()!.name;
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void WeakRef_TargetMismatch_Rejected()
    {
        var source = """
            function f(a: WeakRef<{ a: number }>): WeakRef<{ b: number }> { return a; }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void WeakRef_TooManyTypeArgs_Rejected()
    {
        var source = "let wr: WeakRef<object, string> = new WeakRef({});";
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Equal("TS2314", ex.Diagnostic.TsCode);
    }

    // ---- FinalizationRegistry references are strongly typed ----

    [Fact]
    public void FinalizationRegistry_FakeMemberRejected()
    {
        var source = """
            let fr: FinalizationRegistry<string> = new FinalizationRegistry((v: string) => {});
            fr.nope();
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void FinalizationRegistry_RealMembersResolve()
    {
        // The strongly-typed registry exposes register/unregister with the spec signature
        // register(target: WeakKey, heldValue: T, unregisterToken?: WeakKey): the FIRST argument is the
        // GC target (an object) and the SECOND is the held value of type T (#482, which corrected the
        // earlier signature that mis-modelled the held value as the first and only required parameter).
        // Kept in an UNCALLED function so only the type checker runs.
        var source = """
            function use(fr: FinalizationRegistry<string>): void {
              const target = { id: 1 };
              fr.register(target, "held");
              fr.register(target, "held", target);
              fr.unregister(target);
            }
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void FinalizationRegistry_Register_MissingHeldValue_Rejected()
    {
        // register requires BOTH target and heldValue, so a single argument no longer type-checks.
        // Under the old signature `register("held")` type-checked (held value as the sole required
        // param) but threw "target must be an object" at runtime — no call satisfied both (#482).
        var source = """
            function use(fr: FinalizationRegistry<string>): void {
              fr.register("held");
            }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void FinalizationRegistry_Register_WrongHeldValueType_Rejected()
    {
        // The held value (second argument) must be T; a number is not a string.
        var source = """
            function use(fr: FinalizationRegistry<string>): void {
              fr.register({ id: 1 }, 42);
            }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void FinalizationRegistry_Register_PrimitiveTarget_Rejected()
    {
        // The target must be an object (WeakKey); a primitive is rejected at compile time, matching the
        // runtime's "target must be an object" validation.
        var source = """
            function use(fr: FinalizationRegistry<string>): void {
              fr.register("not-an-object", "held");
            }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void FinalizationRegistry_HeldValueMismatch_Rejected()
    {
        var source = """
            function f(a: FinalizationRegistry<string>): FinalizationRegistry<number> { return a; }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void FinalizationRegistry_TooManyTypeArgs_Rejected()
    {
        var source = "let fr: FinalizationRegistry<string, number> = new FinalizationRegistry((v: string) => {});";
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Equal("TS2314", ex.Diagnostic.TsCode);
    }

    // ---- conditional-type `infer` extraction (DecomposeBuiltInContainer) ----

    [Fact]
    public void ConditionalInfer_FromIterableIterator_BindsElement()
    {
        var source = """
            type ElemOf<T> = T extends IterableIterator<infer V> ? V : "none";
            function f(): ElemOf<IterableIterator<number>> { return "hello"; }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void ConditionalInfer_FromIterableIterator_CorrectBranchAccepted()
    {
        var source = """
            type ElemOf<T> = T extends IterableIterator<infer V> ? V : "none";
            let x: ElemOf<IterableIterator<number>> = 5;
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void ConditionalInfer_FromWeakRef_BindsTarget()
    {
        var source = """
            type TargetOf<T> = T extends WeakRef<infer V> ? V : never;
            function f(): TargetOf<WeakRef<string>> { return 42; }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void ConditionalInfer_FromFinalizationRegistry_BindsHeldValue()
    {
        var source = """
            type HeldOf<T> = T extends FinalizationRegistry<infer V> ? V : never;
            function f(): HeldOf<FinalizationRegistry<string>> { return 42; }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    // ---- regression guard: ordinary use of these annotations type-checks and runs in BOTH modes ----

    [Theory, ModeData]
    public void AnnotatedIterator_IteratesCorrectly(ExecutionMode mode)
    {
        // The annotation is type-level only; it must not perturb interpretation or IL codegen.
        var source = """
            function* gen(): Generator<number> { yield 1; yield 2; yield 3; }
            let g: Generator<number> = gen();
            let it: IterableIterator<number> = [10, 20].values();
            let sum = 0;
            for (const x of g) { sum += x; }
            for (const y of it) { sum += y; }
            console.log(sum);
            """;
        Assert.Equal("36\n", TestHarness.Run(source, mode));
    }
}

using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Structural typing of the sync iterable/iterator protocols (#485), the follow-up to #456 which made
/// <c>Iterator&lt;T&gt;</c>/<c>IterableIterator&lt;T&gt;</c> resolve to a real record instead of <c>any</c>.
/// That left these gaps, closed here:
/// <list type="number">
/// <item>Element type was not checked for a structural iterator source — <c>IteratorResult&lt;T&gt;</c>
/// was unmodeled, so a hand-written <c>next()</c>'s element type was ignored.</item>
/// <item><c>Generator&lt;T&gt;</c> accepted only the matching record, never a structural object.</item>
/// <item><c>for...of</c>/spread/<c>yield*</c> did not recognize a bare <c>[Symbol.iterator]</c> object.</item>
/// <item><c>Iterable&lt;T&gt;</c> and the <c>IteratorResult&lt;T&gt;</c> family resolved to <c>any</c>.</item>
/// </list>
/// The async parallel (<c>AsyncIterator</c>/<c>AsyncIterableIterator</c>/<c>AsyncIterable</c> references) is
/// tracked separately in #483.
/// </summary>
public class StructuralIterableTypingTests
{
    // ---- IteratorResult<T> / IteratorYieldResult<T> / IteratorReturnResult<T> are modeled (#485 gap 4) ----

    [Fact]
    public void IteratorResult_ObjectLiteral_Accepted()
    {
        // Modeled structurally as { value: T; done?: boolean }, so a matching literal satisfies it.
        var source = "let r: IteratorResult<number> = { value: 5, done: false };";
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void IteratorResult_DoneIsOptional_BareValueAccepted()
    {
        // IteratorYieldResult declares `done?: false`, so a bare { value } is a valid result.
        var source = "let r: IteratorResult<number> = { value: 7 };";
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void IteratorResult_ValueTypeMismatch_Rejected()
    {
        var source = "let r: IteratorResult<number> = { value: \"s\", done: false };";
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void IteratorResult_MemberTypesResolve()
    {
        // value is T, done is boolean — reading value as the wrong type is an error.
        var source = """
            function f(r: IteratorResult<number>): void {
              const v: number = r.value;
              const d: boolean = r.done;
              const bad: string = r.value;
            }
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Equal("TS2322", ex.Diagnostic.TsCode);
    }

    [Fact]
    public void IteratorYieldResult_And_ReturnResult_Resolve()
    {
        var source = """
            let y: IteratorYieldResult<string> = { value: "a", done: false };
            let r: IteratorReturnResult<number> = { value: 0, done: true };
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void IteratorResult_TooManyTypeArgs_Rejected()
    {
        var source = "let r: IteratorResult<number, string, boolean> = { value: 1 };";
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        // Defaulted-arity range (TReturn defaults) → TS2707, the code tsc uses for a range (#487).
        Assert.Equal("TS2707", ex.Diagnostic.TsCode);
    }

    // ---- Iterable<T> reference is element-typed (#485 gap 4) and relates structurally ----

    [Fact]
    public void Iterable_FromArray_Accepted()
    {
        var source = "let a: Iterable<number> = [1, 2, 3];";
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void Iterable_FromSet_Accepted()
    {
        var source = "let a: Iterable<string> = new Set<string>([\"x\"]);";
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void Iterable_FromMap_PairElement_Accepted()
    {
        // Map<K, V> iterates as [K, V] tuples.
        var source = "let a: Iterable<[string, number]> = new Map<string, number>();";
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void Iterable_FromGenerator_Accepted()
    {
        var source = """
            function* gen(): Generator<number> { yield 1; }
            let a: Iterable<number> = gen();
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void Iterable_ElementMismatch_Rejected()
    {
        var source = "let a: Iterable<number> = [\"a\", \"b\"];";
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void Iterable_FromNonIterable_Rejected()
    {
        var source = "let a: Iterable<number> = 42;";
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void Iterable_FromStructuralSymbolIterator_Accepted()
    {
        var source = """
            const obj = { [Symbol.iterator](): Iterator<number> {
              return { next(): IteratorResult<number> { return { value: 1, done: true }; } };
            } };
            let a: Iterable<number> = obj;
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void Iterable_FromAsyncGenerator_Rejected()
    {
        // The async-iterator hierarchy is not a sync Iterable.
        var source = """
            async function* agen(): AsyncGenerator<number> { yield 1; }
            let a: Iterable<number> = agen();
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void Iterable_FakeMemberRejected()
    {
        // Iterable<T> is a real type (not `any`), and like the other built-in records an unknown member is
        // a TS2339 — matching IterableIterator's IterableIterator_FakeMemberRejected.
        var source = "function f(x: Iterable<number>): void { x.totallyFakeMethod(); }";
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Equal("TS2339", ex.Diagnostic.TsCode);
    }

    [Fact]
    public void Iterable_TooManyTypeArgs_Rejected()
    {
        var source = "let a: Iterable<number, void, undefined, string> = [1];";
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        // Defaulted-arity range (newer lib Iterable<T, TReturn, TNext>) → TS2707 (#487).
        Assert.Equal("TS2707", ex.Diagnostic.TsCode);
    }

    [Fact]
    public void ConditionalInfer_FromIterable_BindsElement()
    {
        var source = """
            type ElemOf<T> = T extends Iterable<infer V> ? V : "none";
            let x: ElemOf<Iterable<number>> = 5;
            let bad: ElemOf<Iterable<number>> = "str";
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Equal("TS2322", ex.Diagnostic.TsCode);
    }

    // ---- Structural iterator sources are now element-checked against Iterator<T> (#485 gap 1) ----

    [Fact]
    public void StructuralIterator_ElementMismatch_Rejected()
    {
        // The issue's headline: a hand-written next() returning { value: number } cannot satisfy
        // IterableIterator<string>. Before #485 this was wrongly accepted (next presence alone passed).
        var source = "let it: IterableIterator<string> = { next() { return { value: 42, done: false }; } };";
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void StructuralIterator_ElementMatch_Accepted()
    {
        var source = "let it: IterableIterator<string> = { next() { return { value: \"x\", done: false }; } };";
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void StructuralIterator_UntypedNext_Accepted()
    {
        // A next() whose result is `any` cannot be element-checked, so it stays compatible (no false
        // mismatch) — preserving the structural-iterator leniency #456 introduced.
        var source = """
            declare const anyVal: any;
            const it: Iterator<number> = { next() { return anyVal; } };
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void StructuralIterator_AnnotatedIteratorResult_Accepted()
    {
        // next(): IteratorResult<number> now resolves to a real record; the returned object literals must
        // remain assignable to it (regression guard for ArrayStaticTests.Array_From_CustomIterator).
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

    // ---- Structural objects can satisfy a Generator<T> target (#485 gap 2) ----

    [Fact]
    public void StructuralIterableIterator_AssignableToGenerator_Accepted()
    {
        var source = """
            const it = {
              next(): IteratorResult<number> { return { value: 1, done: false }; },
              [Symbol.iterator](): Iterator<number> { return this; }
            };
            const g: Generator<number> = it;
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void StructuralIterator_NotIterable_NotAssignableToGenerator_Rejected()
    {
        // A bare iterator (no [Symbol.iterator]) is not even an IterableIterator, so it cannot be a Generator.
        var source = """
            const it = { next(): IteratorResult<number> { return { value: 1, done: false }; } };
            const g: Generator<number> = it;
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void StructuralGenerator_ElementMismatch_Rejected()
    {
        var source = """
            const it = {
              next(): IteratorResult<number> { return { value: 1, done: false }; },
              [Symbol.iterator](): Iterator<number> { return this; }
            };
            const g: Generator<string> = it;
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    // ---- Structural objects can satisfy an AsyncGenerator<T> target (#549, async parallel of #485 gap 2) ----

    [Fact]
    public void StructuralAsyncIterableIterator_AssignableToAsyncGenerator_Accepted()
    {
        var source = """
            const it = {
              next(): Promise<IteratorResult<number>> { return Promise.resolve({ value: 1, done: false }); },
              [Symbol.asyncIterator]() { return this; }
            };
            const g: AsyncGenerator<number> = it;
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void StructuralAsyncIterator_NotIterable_NotAssignableToAsyncGenerator_Rejected()
    {
        // A bare async iterator (no [Symbol.asyncIterator]) is not an AsyncIterableIterator, so it cannot be
        // an AsyncGenerator.
        var source = """
            const it = { next(): Promise<IteratorResult<number>> { return Promise.resolve({ value: 1, done: false }); } };
            const g: AsyncGenerator<number> = it;
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void StructuralAsyncGenerator_ElementMismatch_Rejected()
    {
        var source = """
            const it = {
              next(): Promise<IteratorResult<number>> { return Promise.resolve({ value: 1, done: false }); },
              [Symbol.asyncIterator]() { return this; }
            };
            const g: AsyncGenerator<string> = it;
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    // ---- Structural for...of / spread / yield* (#485 gap 3) ----

    [Fact]
    public void StructuralForOf_InterfaceAnnotated_ElementTyped()
    {
        // for...of over a [Symbol.iterator]-bearing object binds the iterator's element type, not `any`.
        var source = """
            interface MyIter { [Symbol.iterator](): Iterator<string>; }
            function run(x: MyIter): void { for (const v of x) { const n: number = v; } }
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Equal("TS2322", ex.Diagnostic.TsCode);
    }

    [Fact]
    public void StructuralForOf_ObjectLiteral_ElementTyped()
    {
        var source = """
            const obj = {
              [Symbol.iterator]() {
                return { next(): IteratorResult<string> { return { value: "x", done: false }; } };
              }
            };
            for (const v of obj) { const n: number = v; }
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Equal("TS2322", ex.Diagnostic.TsCode);
    }

    [Fact]
    public void StructuralSpread_ElementTyped()
    {
        var source = """
            const obj = { [Symbol.iterator](): Iterator<number> {
              let i = 0;
              return { next(): IteratorResult<number> { return i < 3 ? { value: ++i, done: false } : { value: 0, done: true }; } };
            } };
            const arr: number[] = [...obj];
            const bad: string[] = [...obj];
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Equal("TS2322", ex.Diagnostic.TsCode);
    }

    [Fact]
    public void StructuralYieldStar_Accepted()
    {
        // yield* over a structural iterable type-checks (it was TS2488 "not iterable" before #485).
        var source = """
            const src = { [Symbol.iterator](): Iterator<string> {
              return { next(): IteratorResult<string> { return { value: "a", done: true }; } };
            } };
            function* g() { yield* src; }
            """;
        TestHarness.RunInterpreted(source);
    }

    // ---- A structural object with no [Symbol.iterator] is not iterable: TS2488, not `any` (#550) ----

    [Fact]
    public void ForOf_IteratorOnlyObject_RejectedTS2488()
    {
        // The issue's headline: an object with next() but no [Symbol.iterator] is an Iterator, not an
        // Iterable. Before #550 the loop variable bound `any` silently; tsc reports TS2488.
        var source = """
            const it = { next() { return { value: 1, done: false }; } };
            for (const x of it) { console.log(x); }
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Equal("TS2488", ex.Diagnostic.TsCode);
    }

    [Fact]
    public void ForOf_PlainObjectLiteral_RejectedTS2488()
    {
        var source = """
            const o = { a: 1 };
            for (const x of o) { console.log(x); }
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Equal("TS2488", ex.Diagnostic.TsCode);
    }

    [Fact]
    public void ForOf_NonIterableInterface_RejectedTS2488()
    {
        var source = """
            interface P { x: number; }
            function f(p: P): void { for (const v of p) { console.log(v); } }
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Equal("TS2488", ex.Diagnostic.TsCode);
    }

    [Fact]
    public void YieldStar_IteratorOnlyObject_RejectedTS2488()
    {
        // The yield* half of #550 — already rejected before #550, locked in here. A bare iterator
        // (next() only) cannot be delegated to with yield*.
        var source = """
            const it = { next() { return { value: 1, done: false }; } };
            function* g() { yield* it; }
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Equal("TS2488", ex.Diagnostic.TsCode);
    }

    [Fact]
    public void ForOf_InterfaceExtendingArray_StillAccepted()
    {
        // `interface I extends Array<T>` is iterable in tsc (inherits Array's [Symbol.iterator]) but is
        // modeled here as a numeric index signature, not an @@iterator member. It must NOT be a false
        // TS2488 — the #550 guard spares interfaces carrying a number index.
        var source = """
            interface IA extends Array<number> {}
            function f(x: IA): void { for (const v of x) { console.log(v); } }
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void ForOf_NumberIndexInterface_StillAccepted()
    {
        // A bare numeric index interface is indistinguishable from the extends-Array shape above, so it
        // stays lenient (binds `any`) rather than risk rejecting an iterable-via-Array interface.
        var source = """
            interface NI { [n: number]: string; }
            function f(x: NI): void { for (const v of x) { console.log(v); } }
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void ForOf_ClassInstanceExtendingArray_StillAccepted()
    {
        // A class may `extends Array` (iterable); its base is a name-only placeholder here, so class
        // instances are excluded from the TS2488 rule entirely — this must keep type-checking.
        var source = """
            class C extends Array<number> {}
            function f(x: C): void { for (const v of x) { console.log(v); } }
            """;
        TestHarness.RunInterpreted(source);
    }

    // ---- Runtime regression guard: the new type modeling does not perturb execution in either mode ----

    [Theory, ModeData]
    public void StructuralIterable_IteratesAndSpreads(ExecutionMode mode)
    {
        var source = """
            const obj = {
              [Symbol.iterator]() {
                let i = 0;
                return { next() { return i < 3 ? { value: i++ * 10, done: false } : { value: 0, done: true }; } };
              }
            };
            let total = 0;
            for (const v of obj) { total += v; }
            console.log(total);
            console.log([...obj].length);
            """;
        Assert.Equal("30\n3\n", TestHarness.Run(source, mode));
    }

    // ---- #593: class-instance iterability (plain instance non-iterable; `extends Array` instance iterable) ----
    // A class is iterable only via an inherited [Symbol.iterator]. SharpTS previously bound `any` for a
    // plain instance in for...of (under-rejecting) and threw a spurious TS2488 for an `extends Array`
    // instance in yield* (over-rejecting); both now match tsc.

    [Fact]
    public void PlainClassInstance_ForOf_RejectedTS2488()
    {
        // tsc: TS2488 — a plain class instance has no [Symbol.iterator]. SharpTS used to bind `any`.
        var source = """
            class P { x = 1; }
            function f(p: P) { for (const v of p) { console.log(v); } }
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Equal("TS2488", ex.Diagnostic.TsCode);
    }

    [Fact]
    public void PlainClassInstance_Spread_RejectedTS2488()
    {
        var source = """
            class P { x = 1; }
            function f(p: P) { return [...p]; }
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Equal("TS2488", ex.Diagnostic.TsCode);
    }

    [Fact]
    public void PlainClassInstance_YieldStar_RejectedTS2488()
    {
        var source = """
            class P { x = 1; }
            function* g(p: P) { yield* p; }
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Equal("TS2488", ex.Diagnostic.TsCode);
    }

    [Fact]
    public void ClassWithNextButNoSymbolIterator_ForOf_RejectedTS2488()
    {
        // Having a next() method does not make a class iterable in tsc (needs [Symbol.iterator]); the
        // SharpTS runtime agrees (for...of over such an instance throws), so the static TS2488 matches both.
        var source = """
            class Counter { next() { return { value: 1, done: false }; } }
            function f(c: Counter) { for (const v of c) { console.log(v); } }
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Equal("TS2488", ex.Diagnostic.TsCode);
    }

    [Fact]
    public void ExtendsArray_YieldStar_Accepted()
    {
        // tsc: OK — C inherits Array's [Symbol.iterator]. SharpTS used to throw a spurious TS2488.
        var source = """
            class C extends Array<number> {}
            function* g(x: C) { yield* x; }
            """;
        TestHarness.RunInterpreted(source);
    }

    [Theory, ModeData]
    public void ExtendsArray_ForOf_BindsElementType_RunsInBothModes(ExecutionMode mode)
    {
        // The inherited element type is number (recovered from the dropped `extends Array<number>` arg), and
        // the runtime supports iterating an Array subclass.
        var source = """
            class C extends Array<number> {}
            const c = new C();
            c.push(10); c.push(20);
            let total = 0;
            for (const v of c) { total += v; }
            console.log(total);
            """;
        Assert.Equal("30\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void ExtendsArray_ForOf_ElementIsNumberNotAny_WrongUseRejected()
    {
        // The bound element is genuinely number (not `any`), so misusing it as string is a TS2322.
        var source = """
            class C extends Array<number> {}
            function f(c: C) { for (const v of c) { const s: string = v; } }
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Equal("TS2322", ex.Diagnostic.TsCode);
    }

    [Fact]
    public void ExtendsArray_ThroughUserSubclass_IsIterable()
    {
        // Iterability is inherited transitively: B → A → Array<number>.
        var source = """
            class A extends Array<number> {}
            class B extends A {}
            function f(b: B) { for (const v of b) { const n: number = v; } }
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void ExtendsNonIterableBuiltin_ForOf_RejectedTS2488()
    {
        // `extends Error` is not iterable in tsc; the enumeration of iterable bases excludes it, so the
        // instance is correctly reported non-iterable rather than binding `any`.
        var source = """
            class MyErr extends Error {}
            function f(e: MyErr) { for (const v of e) {} }
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Equal("TS2488", ex.Diagnostic.TsCode);
    }

    // ---- Structural for await...of — [Symbol.asyncIterator] objects are element-typed (#662) ----

    [Fact]
    public void StructuralForAwaitOf_InterfaceAnnotated_ElementTyped()
    {
        // An interface declaring [Symbol.asyncIterator](): AsyncIterator<number> is async-iterable; the
        // element type is number, so assigning the loop variable to string is TS2322.
        var source = """
            interface AsyncSrc { [Symbol.asyncIterator](): AsyncIterator<number>; }
            async function run(x: AsyncSrc): Promise<void> {
              for await (const v of x) { const s: string = v; }
            }
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Equal("TS2322", ex.Diagnostic.TsCode);
    }

    [Fact]
    public void StructuralForAwaitOf_ObjectLiteralWithAsyncGenFactory_ElementTyped()
    {
        // Object literal whose [Symbol.asyncIterator]() returns an AsyncGenerator<number>: the switch
        // in TryGetStructuralAsyncIterableElement matches the AsyncGenerator record, yielding number.
        var source = """
            async function* agen(): AsyncGenerator<number> { yield 42; }
            const obj = { [Symbol.asyncIterator]() { return agen(); } };
            async function run(): Promise<void> {
              for await (const v of obj) { const s: string = v; }
            }
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Equal("TS2322", ex.Diagnostic.TsCode);
    }

    [Fact]
    public void StructuralForAwaitOf_StructuralIteratorFactory_ElementTyped()
    {
        // Object literal whose [Symbol.asyncIterator]() returns a hand-written iterator
        // ({ next(): Promise<IteratorResult<number>> }) — element type extracted via
        // TryGetStructuralAsyncIteratorElement (Promise unwrap + value field).
        var source = """
            const obj = {
              [Symbol.asyncIterator]() {
                return {
                  async next(): Promise<IteratorResult<number>> { return { value: 1, done: false }; }
                };
              }
            };
            async function run(): Promise<void> {
              for await (const v of obj) { const s: string = v; }
            }
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Equal("TS2322", ex.Diagnostic.TsCode);
    }

    [Fact]
    public void StructuralForAwaitOf_CorrectElementType_Accepted()
    {
        // Same structural shape but the loop body uses the correct type — no error.
        var source = """
            async function* agen(): AsyncGenerator<number> { yield 42; }
            const obj = { [Symbol.asyncIterator]() { return agen(); } };
            async function run(): Promise<void> {
              for await (const v of obj) { const n: number = v; }
            }
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void ForAwaitOf_ObjectWithoutAsyncIterator_StaysLenient()
    {
        // An object without [Symbol.asyncIterator] does not resolve structurally, so the loop variable
        // falls through to `any` — no error (async non-iterability is not yet diagnosed statically).
        var source = """
            const plain = { x: 1 };
            async function run(): Promise<void> {
              for await (const v of plain) { const s: string = v; }
            }
            """;
        TestHarness.RunInterpreted(source);
    }
}

using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// The async iterator-protocol type references — <c>AsyncIterator&lt;T&gt;</c> /
/// <c>AsyncIterableIterator&lt;T&gt;</c> (=&gt; <c>TypeInfo.AsyncIterator</c>) and
/// <c>AsyncIterable&lt;T&gt;</c> (=&gt; <c>TypeInfo.AsyncIterable</c>) — now resolve from a type REFERENCE
/// instead of degrading to <c>any</c> (#483, the async parallel of the sync work in #456/#485). Also pins
/// the <c>Generator</c>/<c>AsyncGenerator</c> arity accepting the lib's optional <c>TReturn</c>/<c>TNext</c>
/// (#487).
/// </summary>
public class AsyncIteratorTypeReferenceTests
{
    // ---- #483: async iterator references are strongly typed (no longer `any`) ----

    [Fact]
    public void AsyncIterableIterator_FakeMemberRejected()
    {
        // The issue's headline example: `it` is now AsyncIterator<number>, not `any`, so an unknown member
        // is a type error instead of silently passing (it failed only at runtime before). A parameter is
        // used so the type comes purely from the annotation.
        var source = """
            function f(it: AsyncIterableIterator<number>): void { it.totallyFakeMethod(); }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void AsyncIterator_AltSpelling_FakeMemberRejected()
    {
        var source = """
            function f(it: AsyncIterator<number>): void { it.nope(); }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void AsyncIterable_FakeMemberRejected()
    {
        var source = """
            function f(it: AsyncIterable<number>): void { it.nope(); }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void AsyncIterableIterator_RealMemberResolves()
    {
        // next() is a genuine member of the iterator protocol and must resolve on the strongly-typed value.
        var source = """
            function f(it: AsyncIterableIterator<number>): void { it.next(); }
            """;
        TestHarness.RunInterpreted(source);
    }

    // ---- AsyncGenerator <-> async iterator direction (an AsyncGenerator IS an AsyncIterableIterator) ----

    [Fact]
    public void AsyncGenerator_AssignableToAsyncIterableIterator_Accepted()
    {
        var source = """
            async function* agen(): AsyncGenerator<number> { yield 1; }
            let it: AsyncIterableIterator<number> = agen();
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void AsyncGenerator_AssignableToAsyncIterator_Accepted()
    {
        var source = """
            async function* agen(): AsyncGenerator<number> { yield 1; }
            let it: AsyncIterator<number> = agen();
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void AsyncGenerator_AssignableToAsyncIterable_Accepted()
    {
        var source = """
            async function* agen(): AsyncGenerator<number> { yield 1; }
            let ai: AsyncIterable<number> = agen();
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void AsyncIterableIterator_SelfAssignment_Accepted()
    {
        var source = """
            function f(a: AsyncIterableIterator<number>): AsyncIterableIterator<number> { return a; }
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void AsyncIterable_NotAssignableToAsyncIterator_Rejected()
    {
        // Asymmetric: an AsyncIterable exposes only [Symbol.asyncIterator], not next(), so it is not an
        // AsyncIterator (mirrors the sync Iterable ↛ Iterator relation).
        var source = """
            function f(a: AsyncIterable<number>): AsyncIterableIterator<number> { return a; }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void AsyncIterator_NotAssignableToSyncIterator_Rejected()
    {
        // The sync and async hierarchies are distinct: an AsyncGenerator is not a sync IterableIterator.
        var source = """
            async function* agen(): AsyncGenerator<number> { yield 1; }
            function f(): IterableIterator<number> { return agen(); }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void AsyncIterableIterator_ElementMismatch_Rejected()
    {
        var source = """
            function f(it: AsyncIterableIterator<number>): AsyncIterableIterator<string> { return it; }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void AsyncIterator_ThreeTypeArgs_LibSpelling_Accepted()
    {
        // lib.d.ts is AsyncIterator<T, TReturn = any, TNext = any>; SharpTS keeps only the element type but
        // must accept (and drop) the optional TReturn/TNext.
        var source = """
            async function* agen(): AsyncGenerator<number> { yield 1; }
            let it: AsyncIterator<number, void, undefined> = agen();
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void AsyncIterator_TooManyTypeArgs_Rejected()
    {
        var source = """
            function f(it: AsyncIterator<number, void, undefined, string>): void {}
            """;
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Equal("TS2707", ex.Diagnostic.TsCode);
    }

    // ---- conditional-type `infer` extraction (DecomposeBuiltInContainer) ----

    [Fact]
    public void ConditionalInfer_FromAsyncIterableIterator_CorrectBranchAccepted()
    {
        var source = """
            type ElemOf<T> = T extends AsyncIterableIterator<infer V> ? V : "none";
            let x: ElemOf<AsyncIterableIterator<number>> = 5;
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void ConditionalInfer_FromAsyncIterableIterator_BindsElement()
    {
        var source = """
            type ElemOf<T> = T extends AsyncIterableIterator<infer V> ? V : "none";
            function f(): ElemOf<AsyncIterableIterator<number>> { return "hello"; }
            """;
        Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void ConditionalInfer_FromAsyncIterable_BindsElement()
    {
        var source = """
            type ElemOf<T> = T extends AsyncIterable<infer V> ? V : never;
            let x: ElemOf<AsyncIterable<number>> = 5;
            """;
        TestHarness.RunInterpreted(source);
    }

    // ---- #487: Generator/AsyncGenerator accept the lib's optional TReturn/TNext (arity 1–3) ----

    [Fact]
    public void Generator_ThreeTypeArgs_LibSpelling_Accepted()
    {
        // lib.d.ts is Generator<T, TReturn = any, TNext = unknown>; the extra two args are accepted and
        // dropped (only the yield type is modeled). Previously rejected with "requires exactly 1" (#487).
        var source = """
            function* gen(): Generator<number, void, unknown> { yield 1; }
            let g: Generator<number> = gen();
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void Generator_TwoTypeArgs_Accepted()
    {
        var source = """
            function* gen(): Generator<number, void> { yield 1; }
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void Generator_TooManyTypeArgs_Rejected()
    {
        var source = "function* gen(): Generator<number, void, unknown, string> { yield 1; }";
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Equal("TS2707", ex.Diagnostic.TsCode);
    }

    [Fact]
    public void AsyncGenerator_ThreeTypeArgs_LibSpelling_Accepted()
    {
        var source = """
            async function* agen(): AsyncGenerator<number, void, unknown> { yield 1; }
            let g: AsyncGenerator<number> = agen();
            """;
        TestHarness.RunInterpreted(source);
    }

    [Fact]
    public void AsyncGenerator_TooManyTypeArgs_Rejected()
    {
        var source = "async function* agen(): AsyncGenerator<number, void, unknown, string> { yield 1; }";
        var ex = Assert.ThrowsAny<TypeCheckException>(() => TestHarness.RunInterpreted(source));
        Assert.Equal("TS2707", ex.Diagnostic.TsCode);
    }
}

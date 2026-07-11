using SharpTS.Parsing;

using System.Collections.Frozen;

namespace SharpTS.TypeSystem;

/// <summary>
/// Structural typing of the sync iterable/iterator protocols (#485). SharpTS models
/// iterables/iterators nominally (dedicated <see cref="TypeInfo"/> records: Array, Set, Map, Iterator,
/// Generator, …); these helpers bridge to TypeScript's structural view so a hand-written object exposing
/// <c>[Symbol.iterator]()</c> / <c>next()</c> is recognized as an iterable/iterator and its element type
/// is derived from <c>next().value</c> rather than collapsing to <c>any</c>.
/// </summary>
public partial class TypeChecker
{
    /// <summary>
    /// Builds the structural shape of <c>IteratorResult&lt;T&gt;</c>: <c>{ value: T; done?: boolean }</c>.
    /// IteratorResult is a structural type in TS (a yield/return union); SharpTS keeps a single element
    /// type (as Iterator/Generator drop their TReturn/TNext) and makes <c>done</c> optional because
    /// IteratorYieldResult declares <c>done?: false</c>, so a bare <c>{ value }</c> is a valid result.
    /// </summary>
    private static TypeInfo.Record BuildIteratorResultType(TypeInfo element)
    {
        var fields = new Dictionary<string, TypeInfo>
        {
            ["value"] = element,
            ["done"] = TypeInfo.Primitive.Boolean,
        }.ToFrozenDictionary();
        return new TypeInfo.Record(fields, OptionalFields: new[] { "done" }.ToFrozenSet());
    }

    /// <summary>The declared return type of a callable member (an object's <c>next</c>/<c>[Symbol.iterator]</c>).</summary>
    private static TypeInfo? GetCallableReturnType(TypeInfo? callable) => callable switch
    {
        TypeInfo.Function f => f.ReturnType,
        TypeInfo.OverloadedFunction o => o.Implementation.ReturnType,
        _ => null
    };

    private static bool IsCallableMember(TypeInfo? member) =>
        member is TypeInfo.Function or TypeInfo.OverloadedFunction;

    /// <summary>
    /// Derives the element type of a structural ITERATOR source — an object that itself exposes a callable
    /// <c>next()</c>. The element is the <c>value</c> of <c>next()</c>'s IteratorResult. When <c>next</c> is
    /// present but its result is untyped (<c>any</c>) or its element can't be read precisely (e.g. a union
    /// result), the element is <c>any</c>, so the protocol still matches without inventing a spurious
    /// mismatch. Returns false only when there is no callable <c>next</c> at all.
    /// </summary>
    private bool TryGetStructuralIteratorElement(TypeInfo source, out TypeInfo elementType)
    {
        elementType = null!;
        var nextMember = GetMemberType(source, "next");
        if (!IsCallableMember(nextMember)) return false;

        elementType = ExtractIteratorResultValue(GetCallableReturnType(nextMember));
        return true;
    }

    /// <summary>
    /// Reads the element type out of a <c>next()</c> return type — the <c>value</c> of its IteratorResult.
    /// Only a single concrete object shape is read precisely; <c>any</c>, a shape without a derivable
    /// <c>value</c>, and anything else (e.g. a union of yield/return results we cannot split) yield
    /// <c>any</c>, so a structural iterator never produces a false element mismatch.
    /// </summary>
    private TypeInfo ExtractIteratorResultValue(TypeInfo? nextReturn) => nextReturn switch
    {
        null or TypeInfo.Any => TypeInfo.Any.Shared,
        TypeInfo.Record or TypeInfo.Interface or TypeInfo.Instance =>
            GetMemberType(nextReturn, "value") ?? TypeInfo.Any.Shared,
        _ => TypeInfo.Any.Shared
    };

    /// <summary>
    /// Derives the element type of a structural ASYNC ITERATOR source — an object that exposes a callable
    /// <c>next()</c> whose return type is <c>Promise&lt;IteratorResult&lt;T&gt;&gt;</c>. The async mirror of
    /// <see cref="TryGetStructuralIteratorElement"/>: the Promise wrapping is resolved via
    /// <see cref="ResolveAwaitedType"/> before reading <c>value</c>. Returns false when there is no callable
    /// <c>next</c>.
    /// </summary>
    private bool TryGetStructuralAsyncIteratorElement(TypeInfo source, out TypeInfo elementType)
    {
        elementType = null!;
        var nextMember = GetMemberType(source, "next");
        if (!IsCallableMember(nextMember)) return false;

        // next() on an async iterator returns Promise<IteratorResult<T>>; unwrap the Promise first.
        TypeInfo? nextReturn = GetCallableReturnType(nextMember);
        TypeInfo iterResult = nextReturn is null ? TypeInfo.Any.Shared : ResolveAwaitedType(nextReturn);
        elementType = ExtractIteratorResultValue(iterResult);
        return true;
    }

    /// <summary>
    /// Derives the element type of a structural ITERABLE source — an object exposing
    /// <c>[Symbol.iterator](): Iterator&lt;T&gt;</c>. In declared/interface types the method is the named
    /// member <c>@@iterator</c>; in object literals a symbol-keyed method lands in the symbol index
    /// signature, so both are probed. The returned iterator's element is read via the dedicated records
    /// or, failing that, structurally via its <c>next()</c>. Returns false when no iterator factory exists.
    /// </summary>
    private bool TryGetStructuralIterableElement(TypeInfo source, out TypeInfo elementType)
    {
        elementType = null!;

        TypeInfo? iteratorFactory = GetMemberType(source, "@@iterator");
        if (!IsCallableMember(iteratorFactory) && source is TypeInfo.Record { SymbolIndexType: { } symIndex })
            iteratorFactory = symIndex;   // object-literal [Symbol.iterator]() lands in the symbol index
        if (!IsCallableMember(iteratorFactory))
        {
            // A class instance may inherit iterability from a built-in iterable base
            // (`class C extends Array<number>`). That placeholder superclass is a name-only MutableClass
            // the normal member walk skips, so probe the hierarchy directly (#593).
            return source is TypeInfo.Instance instance
                && TryGetInstanceExtendsBuiltInIterableElement(instance, out elementType);
        }

        TypeInfo? iterator = GetCallableReturnType(iteratorFactory);
        if (iterator is null) { elementType = TypeInfo.Any.Shared; return true; }

        return TryGetIteratorElementFromReturn(iterator, out elementType);
    }

    /// <summary>
    /// Element type of a class instance that (transitively) extends a built-in iterable, read from the
    /// <c>@@iterator</c> the <c>extends Array&lt;T&gt;</c>/<c>Set&lt;T&gt;</c>/… placeholder records (see
    /// <see cref="ResolveDeclaredSuperclass"/>). That placeholder superclass is a name-only
    /// <see cref="TypeInfo.MutableClass"/> which <c>ClassInfoAccessor</c>'s chain walk skips, so the
    /// hierarchy is walked directly here — mirroring <c>ExtendsBuiltInError</c>. Returns <c>false</c> for an
    /// instance with no built-in-iterable base, leaving it to be reported non-iterable (#593).
    /// </summary>
    private bool TryGetInstanceExtendsBuiltInIterableElement(TypeInfo.Instance instance, out TypeInfo elementType)
    {
        elementType = null!;
        TypeInfo? current = instance.ResolvedClassType;
        for (int guard = 0; current != null && guard < 64; guard++)
        {
            TypeInfo? iteratorFactory = current is TypeInfo.MutableClass mc
                ? (mc.Methods.TryGetValue("@@iterator", out var m) ? m : null)
                : GetMethods(current)?.GetValueOrDefault("@@iterator");
            if (IsCallableMember(iteratorFactory))
            {
                TypeInfo? iterator = GetCallableReturnType(iteratorFactory);
                if (iterator is null) { elementType = TypeInfo.Any.Shared; return true; }
                return TryGetIteratorElementFromReturn(iterator, out elementType);
            }
            current = GetSuperclass(current);
        }
        return false;
    }

    /// <summary>
    /// Element of the iterator value returned by <c>[Symbol.iterator]()</c>: a dedicated iterator/generator
    /// record carries it directly, otherwise it is read structurally from the iterator's <c>next()</c>. An
    /// object that yields a <c>[Symbol.iterator]</c> is iterable even if its element is unknown, so this
    /// always succeeds (defaulting to <c>any</c>).
    /// </summary>
    private bool TryGetIteratorElementFromReturn(TypeInfo iterator, out TypeInfo elementType)
    {
        switch (iterator)
        {
            case TypeInfo.Iterator it: elementType = it.ElementType; return true;
            case TypeInfo.Generator g: elementType = g.YieldType; return true;
            case TypeInfo.Iterable ib: elementType = ib.ElementType; return true;
            default:
                if (TryGetStructuralIteratorElement(iterator, out elementType)) return true;
                elementType = TypeInfo.Any.Shared;
                return true;
        }
    }

    /// <summary>
    /// True when <paramref name="source"/> is a structural object SharpTS can prove is NOT iterable, so a
    /// sync <c>for...of</c> over it is the TS2488 tsc reports rather than a silent <c>any</c> binding
    /// (#550). Call only after <see cref="TryGetStructuralIterableElement"/> has already failed to find a
    /// <c>[Symbol.iterator]</c>. Conservative by design — it must never report a type tsc accepts as
    /// iterable:
    /// <list type="bullet">
    /// <item><b>Record</b> (object literal / inline object type): never inherits iterability, so a missing
    ///   <c>[Symbol.iterator]</c> is conclusive — this is the issue's headline iterator-only object, and a
    ///   plain object. A numeric index signature on a record (<c>{ [n: number]: T }</c>) is not iterable
    ///   in tsc either, so no carve-out is needed.</item>
    /// <item><b>Interface</b> WITHOUT a numeric index signature: an <c>interface I extends Array&lt;T&gt;</c>
    ///   is modeled as a numeric index — its inherited <c>[Symbol.iterator]</c> is not retained as an
    ///   <c>@@iterator</c> member (see TypeChecker.Statements.Interfaces) — so an interface carrying a
    ///   number index might be iterable-via-Array and is spared to avoid a false TS2488. A genuine
    ///   <c>[Symbol.iterator]()</c> interface member is caught earlier by the structural probe.</item>
    /// <item><b>Class instance</b>: a class is iterable only via an inherited <c>[Symbol.iterator]</c>.
    ///   An <c>extends Array/Set/Map/...</c> instance carries one (recovered by the structural probe via
    ///   <see cref="TryGetInstanceExtendsBuiltInIterableElement"/>) and is caught earlier; a class cannot
    ///   declare its own <c>[Symbol.iterator]()</c> yet (#592), so any instance reaching here is genuinely
    ///   non-iterable, matching tsc's TS2488 instead of binding <c>any</c> (#593). When #592 lands, a
    ///   user <c>[Symbol.iterator]</c> becomes a real member the structural probe finds first.</item>
    /// </list>
    /// </summary>
    private static bool IsProvablyNonIterableStructuralObject(TypeInfo source) => source switch
    {
        TypeInfo.Record => true,
        TypeInfo.Interface { NumberIndexType: null } => true,
        TypeInfo.Instance => true,
        _ => false
    };

    /// <summary>
    /// The element type of any sync-iterable source, unifying the dedicated records, strings and
    /// structural iterables. Drives <c>for...of</c>, spread and <c>yield*</c> element binding and the
    /// <c>Iterable&lt;T&gt;</c> assignment target. Returns false for non-iterable types (callers decide
    /// whether that is an error or a fall-through to <c>any</c>). Tuples — handled specially elsewhere —
    /// and unions are intentionally excluded to preserve existing behavior.
    /// </summary>
    private bool TryGetIterableElementType(TypeInfo type, out TypeInfo elementType)
    {
        switch (type)
        {
            case TypeInfo.Array arr: elementType = arr.ElementType; return true;
            case TypeInfo.Set set: elementType = set.ElementType; return true;
            case TypeInfo.Map map: elementType = TypeInfo.Tuple.FromTypes([map.KeyType, map.ValueType], 2); return true;
            case TypeInfo.Iterator it: elementType = it.ElementType; return true;
            case TypeInfo.Generator gen: elementType = gen.YieldType; return true;
            case TypeInfo.Iterable iterable: elementType = iterable.ElementType; return true;
            case TypeInfo.String or TypeInfo.StringLiteral: elementType = TypeInfo.String.Shared; return true;
            case TypeInfo.Any: elementType = TypeInfo.Any.Shared; return true;
            case TypeInfo.Record or TypeInfo.Interface or TypeInfo.Instance:
                return TryGetStructuralIterableElement(type, out elementType);
            default:
                elementType = null!;
                return false;
        }
    }

    /// <summary>
    /// Computes the static type produced by the <c>__arrayDestructure</c> helper (#685), which
    /// normalizes an array binding-pattern source through the iterator protocol. Index-addressable
    /// sources (arrays, tuples, <c>any</c>) pass through with their precise type so the desugared
    /// positional index access stays accurate — notably tuples keep their per-position element types.
    /// Any other iterable (Set, Map, generators, <c>[Symbol.iterator]</c> objects) becomes
    /// <c>Array&lt;element&gt;</c>, so the subsequent <c>_dest0[i]</c> reads the element type instead of
    /// erroring. A <b>string</b> is deliberately NOT passed through: it iterates to <c>string[]</c> so a
    /// rest element binds a fresh array (<c>const [a, ...rest] = "hi"</c> → <c>rest: string[]</c>),
    /// matching ECMA-262 instead of binding the trailing substring (#753); non-rest element types are
    /// unchanged (<c>string</c> either way). A non-iterable, non-indexable source is returned unchanged
    /// so the existing index-access diagnostic still fires.
    /// </summary>
    private TypeInfo NormalizeArrayDestructureSourceType(TypeInfo sourceType)
    {
        switch (sourceType)
        {
            case TypeInfo.Array:
            case TypeInfo.Tuple:
            case TypeInfo.Any:
                return sourceType;
        }

        if (TryGetIterableElementType(sourceType, out var elementType))
            return new TypeInfo.Array(elementType);

        return sourceType;
    }

    /// <summary>
    /// The element type of any async-iterable source — the dedicated <see cref="TypeInfo.AsyncIterable"/>,
    /// <see cref="TypeInfo.AsyncIterator"/> (= AsyncIterableIterator) and <see cref="TypeInfo.AsyncGenerator"/>
    /// records. Drives the <c>AsyncIterable&lt;T&gt;</c> assignment target and the async arm of
    /// <c>for await...of</c> (#483). The async mirror of <see cref="TryGetIterableElementType"/>; sync
    /// iterables are intentionally excluded (a sync iterable is not an async iterable). Returns false for
    /// non-async-iterable types (callers decide between an error and a fall-through to <c>any</c>).
    /// </summary>
    private bool TryGetAsyncIterableElementType(TypeInfo type, out TypeInfo elementType)
    {
        switch (type)
        {
            case TypeInfo.AsyncIterable ai: elementType = ai.ElementType; return true;
            case TypeInfo.AsyncIterator ait: elementType = ait.ElementType; return true;
            case TypeInfo.AsyncGenerator ag: elementType = ag.YieldType; return true;
            case TypeInfo.Any: elementType = TypeInfo.Any.Shared; return true;
            case TypeInfo.Record or TypeInfo.Interface or TypeInfo.Instance:
                return TryGetStructuralAsyncIterableElement(type, out elementType);
            default:
                elementType = null!;
                return false;
        }
    }

    /// <summary>
    /// Derives the element type of a structural ASYNC-ITERABLE source — an object exposing
    /// <c>[Symbol.asyncIterator](): AsyncIterator&lt;T&gt;</c>. The async mirror of
    /// <see cref="TryGetStructuralIterableElement"/>: a class's <c>[Symbol.asyncIterator]()</c> member
    /// lands as the named <c>@@asyncIterator</c> member (#592), and an object literal's symbol-keyed
    /// method in the symbol index signature. Needed so a class declaring <c>implements AsyncIterable&lt;T&gt;</c>
    /// validates structurally (#756). Returns false when no async-iterator factory exists.
    /// </summary>
    private bool TryGetStructuralAsyncIterableElement(TypeInfo source, out TypeInfo elementType)
    {
        elementType = null!;

        TypeInfo? iteratorFactory = GetMemberType(source, "@@asyncIterator");
        if (!IsCallableMember(iteratorFactory) && source is TypeInfo.Record { SymbolIndexType: { } symIndex })
            iteratorFactory = symIndex;   // object-literal [Symbol.asyncIterator]() lands in the symbol index
        if (!IsCallableMember(iteratorFactory))
            return false;

        TypeInfo? iterator = GetCallableReturnType(iteratorFactory);
        if (iterator is null) { elementType = TypeInfo.Any.Shared; return true; }

        switch (iterator)
        {
            case TypeInfo.AsyncIterator ait: elementType = ait.ElementType; return true;
            case TypeInfo.AsyncGenerator ag: elementType = ag.YieldType; return true;
            case TypeInfo.AsyncIterable ai: elementType = ai.ElementType; return true;
            default:
                if (TryGetStructuralAsyncIteratorElement(iterator, out elementType)) return true;
                elementType = TypeInfo.Any.Shared;
                return true;
        }
    }
}

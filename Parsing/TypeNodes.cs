namespace SharpTS.Parsing;

/// <summary>
/// Syntactic type nodes — the structured form of a written type annotation.
/// </summary>
/// <remarks>
/// First layer of the two-layer type model (see <c>docs/plans/type-ast-design.md</c>):
/// <c>TypeNode</c> records what was WRITTEN and where; the checker resolves nodes to
/// <see cref="SharpTS.TypeSystem.TypeInfo"/> (what it MEANS) in scope. During the incremental
/// migration the parser builds nodes opportunistically alongside the legacy annotation strings:
/// a construct without node support yields no node and consumers fall back to the string path,
/// which remains authoritative until the migration completes.
/// </remarks>
public abstract record TypeNode(int Line);

/// <summary>A type reference by name: <c>Foo</c>, <c>Box&lt;string&gt;</c>. Type arguments are
/// null for a bare reference. Also covers primitive/keyword names (<c>string</c>, <c>void</c>,
/// …) — resolution treats them uniformly, exactly like the string path.</summary>
public sealed record NamedTypeNode(
    string Name,
    List<TypeNode>? TypeArguments,
    int Line,
    Token? NameToken = null) : TypeNode(Line);

/// <summary>A literal type: <c>"ok"</c>, <c>42</c>, <c>true</c>, <c>1n</c> (bigint literals carry
/// their <c>System.Numerics.BigInteger</c> value).</summary>
public sealed record LiteralTypeNode(object? Value, int Line) : TypeNode(Line);

/// <summary>The <c>unique symbol</c> type. Only valid on <c>const</c> declarations initialized
/// with <c>Symbol()</c> — the const-declaration checker special-cases the annotation BEFORE
/// resolution; resolving this node anywhere else throws TS1331, exactly like the string path.</summary>
public sealed record UniqueSymbolTypeNode(int Line) : TypeNode(Line);

/// <summary>A <c>readonly</c> array/tuple modifier: <c>readonly T[]</c>, <c>readonly [A, B]</c>.
/// Resolution marks the resolved array/tuple readonly (any other inner type ignores it), exactly
/// like the string path's <c>readonly </c> prefix branch.</summary>
public sealed record ReadonlyTypeNode(TypeNode Inner, int Line) : TypeNode(Line);

/// <summary>A type predicate return type: <c>x is T</c> or <c>asserts x is T</c>. Resolves to
/// <c>TypeInfo.TypePredicate</c>. The <c>asserts x</c> shorthand (non-null assertion) is a
/// separate <see cref="AssertsNonNullTypeNode"/>.</summary>
public sealed record TypePredicateNode(string ParameterName, TypeNode PredicateType, bool IsAssertion, int Line) : TypeNode(Line);

/// <summary>The <c>asserts x</c> shorthand return type. Resolves to <c>TypeInfo.AssertsNonNull</c>.</summary>
public sealed record AssertsNonNullTypeNode(string ParameterName, int Line) : TypeNode(Line);

/// <summary>An array type via the suffix syntax: <c>T[]</c>.</summary>
public sealed record ArrayTypeNode(TypeNode ElementType, int Line) : TypeNode(Line);

/// <summary>A union type: <c>A | B | C</c>.</summary>
public sealed record UnionTypeNode(List<TypeNode> Members, int Line) : TypeNode(Line);

/// <summary>An intersection type: <c>A &amp; B &amp; C</c>. Resolution merges members through the
/// same <c>SimplifyIntersection</c> the string path uses, so member ordering and the
/// primitive-conflict / object-merge rules are identical.</summary>
public sealed record IntersectionTypeNode(List<TypeNode> Members, int Line) : TypeNode(Line);

/// <summary>The <c>keyof T</c> index-query operator. Resolves to <c>TypeInfo.KeyOf</c>.</summary>
public sealed record KeyofTypeNode(TypeNode Operand, int Line) : TypeNode(Line);

/// <summary>An indexed-access type: <c>T[K]</c>, <c>T["key"]</c>. Resolves to
/// <c>TypeInfo.IndexedAccess</c>. The array suffix <c>T[]</c> is an <see cref="ArrayTypeNode"/>,
/// not this.</summary>
public sealed record IndexedAccessTypeNode(TypeNode ObjectType, TypeNode IndexType, int Line) : TypeNode(Line);

/// <summary>A conditional type: <c>Check extends Extends ? True : False</c>. Resolves to a
/// deferred <c>TypeInfo.ConditionalType</c> (the same shape the string path builds);
/// distribution and <c>infer</c> inference happen later in <c>EvaluateConditionalType</c>,
/// path-independent.</summary>
public sealed record ConditionalTypeNode(TypeNode CheckType, TypeNode ExtendsType, TypeNode TrueType, TypeNode FalseType, int Line) : TypeNode(Line);

/// <summary>An <c>infer U</c> placeholder inside a conditional's extends clause, optionally
/// constrained (<c>infer U extends C</c>). Resolves to <c>TypeInfo.InferredTypeParameter</c>;
/// the constraint gates the match in <c>EvaluateConditionalType</c> (an inferred type that does
/// not satisfy it sends the conditional to its false branch).</summary>
public sealed record InferTypeNode(string Name, int Line, TypeNode? Constraint = null) : TypeNode(Line);

/// <summary>A <c>typeof entity</c> query. The entity path is carried in its string-path spelling
/// (<c>obj.prop</c>, <c>arr[0]</c>) and resolved by <c>EvaluateTypeOf</c> — the same evaluator the
/// string path uses, so there is no behavioral difference beyond skipping the top-level scan.</summary>
public sealed record TypeQueryNode(string EntityPath, int Line) : TypeNode(Line);

/// <summary>A parameter inside a function or constructor type: <c>x: T</c>, <c>x?: T</c>,
/// <c>...rest: T[]</c>. A bare name (<c>(x) =&gt; R</c>) gets an explicit <c>any</c> type node —
/// the name is a label only, never resolved. Not itself a type.</summary>
public sealed record ParameterTypeNode(string? Name, TypeNode Type, bool IsOptional, bool IsRest, int Line);

/// <summary>A function type: <c>(a: T) =&gt; R</c>, optionally with a leading
/// <c>this: X</c> pseudo-parameter (carried separately — it does not count toward arity).</summary>
public sealed record FunctionTypeNode(TypeNode? ThisType, List<ParameterTypeNode> Parameters, TypeNode ReturnType, int Line) : TypeNode(Line);

/// <summary>A constructor type: <c>new (a: T) =&gt; R</c>, optionally with a leading <c>this: X</c>
/// pseudo-parameter. Resolves to an object type carrying a single construct signature, mirroring
/// the string path's <c>{ new (…) =&gt; R }</c> rendering. The this-type is resolved (so bad names
/// fail identically) but not carried on the signature — <c>TypeInfo.ConstructorSignature</c> has
/// no slot for it, exactly like the string path's <c>ConvertConstructSignatures</c>.</summary>
public sealed record ConstructorTypeNode(List<ParameterTypeNode> Parameters, TypeNode ReturnType, int Line, TypeNode? ThisType = null) : TypeNode(Line);

/// <summary>A generic constructor type: <c>new &lt;T&gt;(a: T) =&gt; R</c>. Resolves to an object type
/// carrying a single GENERIC construct signature; the type parameters scope the body exactly like
/// <see cref="GenericFunctionTypeNode"/> (and the string path's per-signature scoping). A <c>this</c>
/// pseudo-parameter rides on the body <see cref="FunctionTypeNode"/> and is dropped from the
/// signature the same way as on <see cref="ConstructorTypeNode"/>.</summary>
public sealed record GenericConstructorTypeNode(List<TypeParam> TypeParameters, FunctionTypeNode Body, int Line) : TypeNode(Line);

/// <summary>A generic function type: <c>&lt;T&gt;(a: T) =&gt; R</c>. The type-parameter list is carried
/// in its AST <see cref="TypeParam"/> form (constraints/defaults are resolved by the checker in a
/// fresh type-parameter scope, exactly like the string path's two-pass
/// <c>TryParseGenericFunctionTypeInfo</c>); the body is the inner <see cref="FunctionTypeNode"/>,
/// resolved within that scope so its <c>T</c>s bind to the parameters. Resolves to
/// <c>TypeInfo.GenericFunction</c>.</summary>
public sealed record GenericFunctionTypeNode(List<TypeParam> TypeParameters, FunctionTypeNode Body, int Line) : TypeNode(Line);

/// <summary>A template literal type: <c>`a${T}b`</c>. <see cref="Strings"/> holds the N+1 static
/// segments around the N <see cref="InterpolatedTypes"/> (a plain <c>`text`</c> has one string and
/// no interpolations). Resolution mirrors the string path's <c>NormalizeTemplateLiteralType</c>:
/// all-concrete interpolations expand to a union of string literals, otherwise it stays a pattern
/// <c>TypeInfo.TemplateLiteralType</c>.</summary>
public sealed record TemplateLiteralTypeNode(List<string> Strings, List<TypeNode> InterpolatedTypes, int Line) : TypeNode(Line);

/// <summary>An inline object type: <c>{ name: string; greet(x: number): void }</c>.</summary>
public sealed record ObjectTypeNode(List<ObjectTypeMemberNode> Members, int Line) : TypeNode(Line);

/// <summary>A mapped type: <c>{ [K in Constraint as Remap]?: Value }</c>, with optional
/// <c>+/-readonly</c> and <c>+/-?</c> modifiers. The mapped parameter is registered as an open
/// type variable while the as-clause and value type resolve, so their bodies build the same
/// deferred forms (IndexedAccess, deferred references) <c>ExpandMappedType</c> substitutes per
/// key — identical to the string path. The modifier flags map 1:1 to
/// <c>MappedTypeModifiers</c> in the checker (kept as bools so this syntax layer needs no
/// dependency on the semantic enum).</summary>
public sealed record MappedTypeNode(
    string ParamName,
    TypeNode Constraint,
    TypeNode ValueType,
    TypeNode? AsClause,
    bool AddReadonly,
    bool RemoveReadonly,
    bool AddOptional,
    bool RemoveOptional,
    int Line) : TypeNode(Line);

/// <summary>A member of an <see cref="ObjectTypeNode"/>. Not itself a type.</summary>
public abstract record ObjectTypeMemberNode(int Line);

/// <summary>A property or method member: <c>name: T</c>, <c>name?: T</c>, <c>name(x: T): R</c>.
/// Computed names are carried in their string-path spelling (<c>@@iterator</c>). Method-syntax
/// members keep <see cref="IsMethod"/> so bivariant parameter relating survives
/// (the string path's <c>#m</c> marker).</summary>
public sealed record PropertyMemberNode(string Name, TypeNode Type, bool IsOptional, bool IsMethod, int Line) : ObjectTypeMemberNode(Line);

/// <summary>An index signature: <c>[k: string]: T</c>. <see cref="KeyKind"/> is
/// <c>string</c>, <c>number</c>, or <c>symbol</c>.</summary>
public sealed record IndexSignatureNode(string KeyKind, TypeNode ValueType, int Line) : ObjectTypeMemberNode(Line);

/// <summary>A call signature member: <c>(x: T): R</c> or generic <c>&lt;T&gt;(x: T): R</c>. The
/// signature is a <see cref="FunctionTypeNode"/> (non-generic) or a
/// <see cref="GenericFunctionTypeNode"/> (generic overload).</summary>
public sealed record CallSignatureMemberNode(TypeNode Signature, int Line) : ObjectTypeMemberNode(Line);

/// <summary>A construct signature member: <c>new (x: T): R</c> or generic <c>new &lt;T&gt;(x: T): R</c>.
/// The signature is a <see cref="FunctionTypeNode"/> or a <see cref="GenericFunctionTypeNode"/>.</summary>
public sealed record ConstructSignatureMemberNode(TypeNode Signature, int Line) : ObjectTypeMemberNode(Line);

/// <summary>A tuple type: <c>[string, n?: number, ...rest: boolean[]]</c>.</summary>
public sealed record TupleTypeNode(List<TupleElementNode> Elements, int Line) : TypeNode(Line);

/// <summary>One tuple element: optionally named, optional (<c>?</c>), or a spread/rest
/// (<c>...T</c> / <c>...T[]</c>, distinguished at resolution by position and array-ness).
/// Not itself a type.</summary>
public sealed record TupleElementNode(string? Name, TypeNode Type, bool IsOptional, bool IsRest, int Line);

/// <summary>
/// Counters proving how much of the corpus the node path already covers — read by tests and
/// dumped by the checker when <c>SHARPTS_TYPENODE_STATS=1</c>. Coarse by design (spike
/// instrumentation, not telemetry).
/// </summary>
/// <remarks>
/// Backed by <see cref="AsyncLocal{T}"/> so each logical flow has its own counters. The test
/// harness runs the checker inside <c>Task.Run</c>, so the increments land on a pool thread while
/// the test reads the counters on its own thread — AsyncLocal flows the same holder across that
/// boundary, and concurrent tests are sibling flows that never see each other's values. A plain
/// static (or <c>[ThreadStatic]</c>) breaks one of those two requirements: a shared static lets
/// xUnit's parallel collections corrupt the counter (a string fallback in one test fails an
/// unrelated test's <c>Assert.Equal(0, StringFallbacks)</c>); [ThreadStatic] severs the checker's
/// pool-thread writes from the test thread's reads. Call <see cref="Reset"/> before the checked
/// run so the holder is established on the originating flow and flows down into the worker.
/// </remarks>
public static class TypeNodeStats
{
    private sealed class Counters { public long NodeHits; public long StringFallbacks; }

    private static readonly AsyncLocal<Counters?> _current = new();

    private static Counters Current => _current.Value ??= new Counters();

    /// <summary>Annotations resolved through the node path.</summary>
    public static long NodeHits
    {
        get => Current.NodeHits;
        set => Current.NodeHits = value;
    }

    /// <summary>Annotations that had no node (unsupported construct) and used the string path.</summary>
    public static long StringFallbacks
    {
        get => Current.StringFallbacks;
        set => Current.StringFallbacks = value;
    }

    public static void Reset() => _current.Value = new Counters();
}

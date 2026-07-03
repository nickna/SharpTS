using SharpTS.TypeSystem.Exceptions;
using SharpTS.Runtime.BuiltIns;
using System.Collections.Frozen;
using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

/// <summary>
/// Type compatibility checking - structural and nominal typing.
/// </summary>
/// <remarks>
/// Core compatibility logic with memoization and IsCompatibleCore decision tree.
/// Related partial files:
/// - TypeChecker.Compatibility.Helpers.cs: Type predicates and class accessors
/// - TypeChecker.Compatibility.TypeGuards.cs: Control-flow type narrowing
/// - TypeChecker.Compatibility.Structural.cs: Duck typing and member access
/// - TypeChecker.Compatibility.Tuples.cs: Tuple and array compatibility
/// - TypeChecker.Compatibility.Callable.cs: Callable/constructable interfaces
/// - TypeChecker.Compatibility.TemplateLiterals.cs: Template literal patterns
/// </remarks>
public partial class TypeChecker
{
    /// <summary>
    /// Comparer for compatibility cache keys using TypeInfoEqualityComparer.
    /// Ensures structurally equivalent types (including those with List fields)
    /// are treated as equal keys.
    /// </summary>
    private sealed class CompatibilityCacheKeyComparer
        : IEqualityComparer<(TypeInfo Expected, TypeInfo Actual)>
    {
        public static readonly CompatibilityCacheKeyComparer Instance = new();

        public bool Equals((TypeInfo Expected, TypeInfo Actual) x,
                           (TypeInfo Expected, TypeInfo Actual) y)
        {
            return TypeInfoEqualityComparer.Instance.Equals(x.Expected, y.Expected)
                && TypeInfoEqualityComparer.Instance.Equals(x.Actual, y.Actual);
        }

        public int GetHashCode((TypeInfo Expected, TypeInfo Actual) obj)
        {
            return HashCode.Combine(
                TypeInfoEqualityComparer.Instance.GetHashCode(obj.Expected),
                TypeInfoEqualityComparer.Instance.GetHashCode(obj.Actual)
            );
        }
    }

    /// <summary>
    /// Fast identity-based cache for type compatibility.
    /// Uses reference equality for O(1) lookup when the same TypeInfo instances are used.
    /// Falls back to structural equality cache when reference equality misses.
    /// </summary>
    private sealed class IdentityCompatibilityCacheKey(TypeInfo expected, TypeInfo actual)
    {
        public readonly TypeInfo Expected = expected;
        public readonly TypeInfo Actual = actual;
    }

    private sealed class IdentityCacheKeyComparer : IEqualityComparer<IdentityCompatibilityCacheKey>
    {
        public static readonly IdentityCacheKeyComparer Instance = new();

        public bool Equals(IdentityCompatibilityCacheKey? x, IdentityCompatibilityCacheKey? y)
        {
            if (x == null || y == null) return x == y;
            // Use reference equality for fast comparison
            return ReferenceEquals(x.Expected, y.Expected) && ReferenceEquals(x.Actual, y.Actual);
        }

        public int GetHashCode(IdentityCompatibilityCacheKey obj)
        {
            // Use RuntimeHelpers.GetHashCode for identity-based hash
            return HashCode.Combine(
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj.Expected),
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj.Actual)
            );
        }
    }

    // Fast identity-based cache (first level)
    private Dictionary<IdentityCompatibilityCacheKey, bool>? _identityCompatibilityCache;

    // Track compatibility checks in progress for co-induction cycle detection
    // Uses identity-based comparison since we need to detect the exact same type pair
    private HashSet<IdentityCompatibilityCacheKey>? _compatibilityInProgress;

    // Depth counter to prevent stack overflow with deeply nested recursive type checks
    private int _compatibilityCheckDepth;
    private const int MaxCompatibilityCheckDepth = 50;

    /// <summary>
    /// Argument/parameter assignability that additionally honors parameter optionality.
    /// An optional (<c>x?: T</c>) or default-valued (<c>x: T = …</c>) parameter has a call-site
    /// type of <c>T | undefined</c> in TypeScript, so an explicit <c>undefined</c> (or a
    /// <c>T | undefined</c> value) argument is accepted there — and, for a default-valued
    /// parameter, triggers the default. Pass <paramref name="optional"/> = true only for a
    /// non-rest parameter whose position is at or beyond the signature's <c>MinArity</c>; a
    /// genuinely required parameter (optional = false) still rejects <c>undefined</c>. (#668)
    /// </summary>
    private bool IsArgumentCompatible(TypeInfo paramType, TypeInfo argType, bool optional)
    {
        if (IsCompatible(paramType, argType)) return true;
        if (!optional) return false;
        // Widen the declared type with `undefined`; the existing union-compatibility rules then
        // accept an `undefined` / `T | undefined` argument without admitting any other mismatch.
        return IsCompatible(new TypeInfo.Union([paramType, new TypeInfo.Undefined()]), argType);
    }

    /// <summary>
    /// Checks type compatibility with two-level memoization and co-inductive cycle detection.
    /// Level 1: Fast identity-based cache using reference equality (O(1) for same instances)
    /// Level 2: Structural equality cache for different instances with same structure
    /// Co-induction: If checking the same type pair (by identity) that's already in progress,
    /// assume compatible to break infinite recursion with recursive types.
    /// Depth limit: At max depth, assume compatible as a fallback for deeply nested checks.
    /// </summary>
    private bool IsCompatible(TypeInfo expected, TypeInfo actual)
    {
        // Under strictFunctionTypes the verdict for a function-typed pair depends on whether the
        // comparison happens inside a method member (bivariant) or not (strict) — the same pair
        // can legitimately differ between the two contexts, so bivariant-context results must not
        // be cached or served from cache. Variance measurement likewise compares with the callback
        // rule enabled, which ordinary comparisons don't.
        //
        // Speculative hoist-time inference (#383/#388, _suppressDiagnostics > 0) runs in recovery
        // mode and may carry a different open-type-variable scope than the real pass — e.g. a naked
        // type parameter that is in scope during real checking but not during speculation relates
        // differently (naked vs its constraint). Its verdicts are therefore non-authoritative and
        // must neither be written to nor served from the shared cache, or they poison the real pass.
        bool uncacheable = (_strictFunctionTypes && _methodBivarianceDepth > 0)
            || _varianceMeasurementDepth > 0 || _suppressDiagnostics > 0;

        // Level 1: Fast identity-based cache (reference equality)
        _identityCompatibilityCache ??= new(IdentityCacheKeyComparer.Instance);
        var identityKey = new IdentityCompatibilityCacheKey(expected, actual);

        if (!uncacheable && _identityCompatibilityCache.TryGetValue(identityKey, out var identityCached))
            return identityCached;

        // Level 2: Structural equality cache (for different instances with same structure)
        _compatibilityCache ??= new(CompatibilityCacheKeyComparer.Instance);
        var structuralKey = (expected, actual);

        if (!uncacheable && _compatibilityCache.TryGetValue(structuralKey, out var structuralCached))
        {
            // Store in identity cache for future fast lookups
            _identityCompatibilityCache[identityKey] = structuralCached;
            return structuralCached;
        }

        // Co-induction: if this exact type pair (by identity) is already being checked,
        // assume compatible to break cycle. This is safe because if the types were
        // incompatible, we would have found that incompatibility before recursing back.
        _compatibilityInProgress ??= new(IdentityCacheKeyComparer.Instance);
        if (!_compatibilityInProgress.Add(identityKey))
        {
            // Already checking this pair - assume compatible (co-induction)
            return true;
        }

        // Depth limit: at max depth, assume compatible as fallback
        // This handles cases where co-induction doesn't catch the cycle (different actual values)
        if (_compatibilityCheckDepth >= MaxCompatibilityCheckDepth)
        {
            _compatibilityInProgress.Remove(identityKey);
            return true;
        }

        _compatibilityCheckDepth++;
        try
        {
            // Cache miss - compute result
            var result = IsCompatibleCore(expected, actual);

            // Store in both caches (bivariant-context results are context-dependent — skip)
            if (!uncacheable)
            {
                _compatibilityCache[structuralKey] = result;
                _identityCompatibilityCache[identityKey] = result;
            }

            return result;
        }
        finally
        {
            _compatibilityCheckDepth--;
            _compatibilityInProgress.Remove(identityKey);
        }
    }

    /// <summary>
    /// Resolves a mapped type's key domain (its <c>in</c> constraint) the same way
    /// <see cref="ExpandMappedType"/> does — evaluating <c>keyof</c> and resolving a key-filter
    /// indexed access — but without enumerating it into fields. Used to decide whether the
    /// domain is concrete (enumerable) or deferred.
    /// </summary>
    private TypeInfo ResolveMappedKeyDomain(TypeInfo.MappedType mapped)
    {
        TypeInfo domain = mapped.Constraint;
        if (domain is TypeInfo.KeyOf keyOf)
            domain = EvaluateKeyOf(keyOf.SourceType);
        if (domain is TypeInfo.IndexedAccess indexed)
            domain = ResolveIndexedAccess(indexed, new Dictionary<string, TypeInfo>());
        return domain;
    }

    /// <summary>
    /// True when a mapped type's key domain can't be enumerated to concrete keys — it stays a
    /// deferred conditional (the key-filter idiom) or a generic <c>keyof T</c> / bare type
    /// parameter. Concrete key sets (string literals, unions of them) are NOT deferred.
    /// </summary>
    private static bool IsDeferredKeyDomain(TypeInfo domain) => domain switch
    {
        TypeInfo.ConditionalType => true,
        TypeInfo.KeyOf { SourceType: TypeInfo.TypeParameter } => true,
        TypeInfo.TypeParameter => true,
        _ => false
    };

    /// <summary>
    /// Structural relation for a target mapped type whose key domain is DEFERRED, where ordinary
    /// expansion would yield an empty object (so distinct key-filters compare equal and a
    /// homomorphic projection of <c>T</c> fails against <c>T</c>). Mirrors tsc:
    /// <list type="bullet">
    /// <item>source is another mapped type <c>{ [P in K2]: V2 }</c>: relate iff the target's keys
    /// are a subset of the source's (<c>K1 ⊆ K2</c>, contravariant) and the source value relates
    /// to the target value (covariant);</item>
    /// <item>source is the projected object itself for a homomorphic <c>{ [P in K]: X[P] }</c>:
    /// relate iff <c>K ⊆ keyof X</c> (every filtered key is a real key of the source).</item>
    /// </list>
    /// Returns false (deferring to ordinary expansion) for enumerable domains or shapes it does
    /// not recognise. See #337 item 1 (f7: FunctionProperties / NonFunctionProperties).
    /// </summary>
    private bool TryRelateDeferredMappedType(TypeInfo.MappedType target, TypeInfo source, out bool result)
    {
        result = false;
        TypeInfo domain = ResolveMappedKeyDomain(target);
        if (!IsDeferredKeyDomain(domain)) return false;  // enumerable — ordinary expansion handles it

        if (source is TypeInfo.MappedType sourceMapped)
        {
            TypeInfo sourceDomain = ResolveMappedKeyDomain(sourceMapped);
            // keys(target) ⊆ keys(source)  ⟺  the target domain is assignable to the source domain.
            bool keysSubset = IsCompatible(sourceDomain, domain);
            // Align the two mapped parameters, then require source value → target value.
            var align = new Dictionary<string, TypeInfo>
            {
                [sourceMapped.ParameterName] = new TypeInfo.TypeParameter(target.ParameterName)
            };
            bool valuesRelate = IsCompatible(target.ValueType, Substitute(sourceMapped.ValueType, align));
            result = keysSubset && valuesRelate;
            return true;
        }

        // Homomorphic projection `{ [P in K]: X[P] }` assigned from a source equal to X: the
        // source already has every property the projection names as long as K ⊆ keyof X.
        if (target.ValueType is TypeInfo.IndexedAccess { ObjectType: var projected, IndexType: TypeInfo.TypeParameter indexParam } &&
            indexParam.Name == target.ParameterName &&
            TypeInfoEqualityComparer.Instance.Equals(projected, source))
        {
            result = IsCompatible(new TypeInfo.KeyOf(source), domain);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Expands a recursive type alias placeholder to its full type.
    /// Used for lazy expansion during compatibility checks.
    /// Uses _expandedTypeAliasCache to ensure the same TypeInfo object is reused,
    /// enabling identity-based caching to break infinite recursion.
    /// </summary>
    /// <param name="rta">The recursive type alias to expand.</param>
    /// <returns>The expanded type.</returns>
    private TypeInfo ExpandRecursiveTypeAlias(TypeInfo.RecursiveTypeAlias rta)
    {
        // For generic aliases, create a cache key that includes type arguments
        string cacheKey = rta.TypeArguments is { Count: > 0 }
            ? $"{rta.AliasName}<{string.Join(",", rta.TypeArguments.Select(t => t.ToString()))}>"
            : rta.AliasName;

        // Check cache first - reusing the same TypeInfo object is crucial for breaking recursion
        _expandedTypeAliasCache ??= new Dictionary<string, TypeInfo>(StringComparer.Ordinal);
        if (_expandedTypeAliasCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        if (++_typeAliasExpansionDepth > MaxTypeAliasExpansionDepth)
        {
            _typeAliasExpansionDepth--;
            throw new TypeCheckException(
                $"Type alias '{rta.AliasName}' circularly references itself.", tsCode: "TS2456");
        }

        // Set up the expansion stack to prevent infinite recursion when ToTypeInfo
        // encounters nested references to the same alias
        _typeAliasExpansionStack ??= new HashSet<string>(StringComparer.Ordinal);
        bool addedToStack = _typeAliasExpansionStack.Add(rta.AliasName);

        try
        {
            TypeInfo expanded;
            if (rta.TypeArguments is { Count: > 0 })
            {
                // Generic recursive alias - resolve directly with TypeInfo arguments
                // This avoids the TypeInfo -> string -> TypeInfo round-trip
                expanded = ResolveGenericType(rta.AliasName, rta.TypeArguments);
            }
            else
            {
                // Non-generic recursive alias: resolve the stored definition node under the stack
                // entry pushed above (so self-references become placeholders), string fallback.
                // Resolving the DEFINITION — not the alias name — is load-bearing: the name is
                // already on the expansion stack, so name resolution would just hand back another
                // RecursiveTypeAlias placeholder.
                var alias = _environment.GetTypeAlias(rta.AliasName);
                if (alias is { } aliasEntry)
                {
                    expanded = aliasEntry.DefinitionNode is { } definitionNode
                        ? TryToTypeInfo(definitionNode) ?? ToTypeInfo(aliasEntry.Definition)
                        : ToTypeInfo(aliasEntry.Definition);
                }
                else
                {
                    throw new TypeCheckException($"Unknown type '{rta.AliasName}'.", tsCode: "TS2304");
                }
            }

            // Cache the expanded type
            _expandedTypeAliasCache[cacheKey] = expanded;
            return expanded;
        }
        finally
        {
            if (addedToStack)
            {
                _typeAliasExpansionStack.Remove(rta.AliasName);
            }
            _typeAliasExpansionDepth--;
        }
    }

    /// <summary>
    /// Converts a TypeInfo back to a string representation for re-parsing.
    /// Used when expanding recursive type aliases with type arguments.
    /// </summary>
    private static string TypeInfoToString(TypeInfo type) => type switch
    {
        TypeInfo.String => "string",
        TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER } => "number",
        TypeInfo.Primitive { Type: TokenType.TYPE_BOOLEAN } => "boolean",
        TypeInfo.Void => "void",
        TypeInfo.Null => "null",
        TypeInfo.Undefined => "undefined",
        TypeInfo.Unknown => "unknown",
        TypeInfo.Never => "never",
        TypeInfo.Any => "any",
        TypeInfo.Symbol => "symbol",
        TypeInfo.BigInt => "bigint",
        TypeInfo.Object => "object",
        TypeInfo.StringLiteral sl => $"\"{sl.Value}\"",
        TypeInfo.NumberLiteral nl => nl.Value.ToString(),
        TypeInfo.BooleanLiteral bl => bl.Value ? "true" : "false",
        TypeInfo.Array arr => $"{TypeInfoToString(arr.ElementType)}[]",
        TypeInfo.Union u => string.Join(" | ", u.FlattenedTypes.Select(TypeInfoToString)),
        TypeInfo.Intersection i => string.Join(" & ", i.FlattenedTypes.Select(TypeInfoToString)),
        TypeInfo.Tuple t => $"[{string.Join(", ", t.ElementTypes.Select(TypeInfoToString))}]",
        TypeInfo.Record r => $"{{ {string.Join("; ", r.Fields.Select(f => $"{f.Key}: {TypeInfoToString(f.Value)}"))} }}",
        TypeInfo.Class c => c.Name,
        TypeInfo.Instance inst when inst.ClassType is TypeInfo.Class c => c.Name,
        TypeInfo.Interface itf => itf.Name,
        TypeInfo.TypeParameter tp => tp.Name,
        TypeInfo.RecursiveTypeAlias rta => rta.TypeArguments is { Count: > 0 }
            ? $"{rta.AliasName}<{string.Join(", ", rta.TypeArguments.Select(TypeInfoToString))}>"
            : rta.AliasName,
        _ => type.ToString() ?? "any"
    };

    /// <summary>
    /// tsc "Literal" union reduction, narrowed to the slice that changes an assignability verdict:
    /// a bare enum constituent is absorbed by a primitive constituent it is a subtype of — numeric
    /// enum by <c>number</c>, string enum by <c>string</c>, heterogeneous enum when both are present.
    /// Drop the enum, keep the primitive. Needed because enum→enum is NOMINAL, so <c>(e | number)</c>
    /// must relate to a target the way <c>number</c> does (e.g. assignable to a <em>different</em>
    /// numeric enum E2 via numberAssignableToEnum), not member-wise — the <c>e</c> arm would otherwise
    /// reject the whole union (#894). Literal-by-primitive absorption (<c>1</c> by <c>number</c>) is
    /// intentionally omitted: real-literal subtyping is transitive, so it never changes an
    /// assignability verdict, only the type's rendering. Returns the input list unchanged when nothing
    /// is absorbed, so callers keep their cached <see cref="TypeInfo.Union.FlattenedTypes"/> instance.
    /// </summary>
    private static List<TypeInfo> ReduceEnumMembersAbsorbedByPrimitive(List<TypeInfo> members)
    {
        bool hasNumber = members.Any(m => m is TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER });
        bool hasString = members.Any(m => m is TypeInfo.String);
        if (!hasNumber && !hasString) return members;

        bool Absorbed(TypeInfo.Enum e) => e.Kind switch
        {
            EnumKind.Numeric => hasNumber,
            EnumKind.String => hasString,
            EnumKind.Heterogeneous => hasNumber && hasString,
            _ => false
        };

        var reduced = members.Where(m => m is not TypeInfo.Enum en || !Absorbed(en)).ToList();
        return reduced.Count == members.Count ? members : reduced; // never reduce to empty
    }

    /// <summary>
    /// Core type compatibility logic without caching.
    /// </summary>
    private bool IsCompatibleCore(TypeInfo expected, TypeInfo actual)
    {
        if (expected is TypeInfo.Any or TypeInfo.Inferred || actual is TypeInfo.Any or TypeInfo.Inferred) return true;

        // strictNullChecks: off — null/undefined are assignable to every type except `never`.
        // Checked early so it short-circuits before any expected-type-specific rejection.
        if (!_strictNullChecks && actual is TypeInfo.Null or TypeInfo.Undefined)
            return expected is not TypeInfo.Never;

        // Expand recursive type aliases lazily
        if (expected is TypeInfo.RecursiveTypeAlias expectedRTA)
        {
            return IsCompatible(ExpandRecursiveTypeAlias(expectedRTA), actual);
        }
        if (actual is TypeInfo.RecursiveTypeAlias actualRTA)
        {
            return IsCompatible(expected, ExpandRecursiveTypeAlias(actualRTA));
        }

        // A key-filter indexed access over a homomorphic mapped type with a generic key domain
        // (`{ [K in keyof T]: F(K) }[keyof T]`, e.g. FunctionPropertyNames<T>) resolves to a
        // deferred conditional. Surface that BEFORE the keyof/type-parameter rules below, which
        // would otherwise reject the raw IndexedAccess outright and never reach the deferred-
        // conditional relation logic (#337 item 1). Scoped to the conditional result so ordinary
        // `T[K]` accesses keep their existing (later) handling.
        if (expected is TypeInfo.IndexedAccess expectedIaPre &&
            ResolveIndexedAccess(expectedIaPre, new Dictionary<string, TypeInfo>()) is TypeInfo.ConditionalType expectedIaCond)
        {
            expected = expectedIaCond;
        }
        if (actual is TypeInfo.IndexedAccess actualIaPre &&
            ResolveIndexedAccess(actualIaPre, new Dictionary<string, TypeInfo>()) is TypeInfo.ConditionalType actualIaCond)
        {
            actual = actualIaCond;
        }

        // A target mapped type over a deferred key domain (Pick<T, FunctionPropertyNames<T>>, …)
        // is related structurally, BEFORE the type-parameter rules below — those would otherwise
        // reject a homomorphic projection `Pick<T, K> ← T` (source is a bare type parameter) and
        // never reach the mapped-type logic (#337 item 1, f7).
        if (expected is TypeInfo.MappedType earlyMapped &&
            TryRelateDeferredMappedType(earlyMapped, actual, out var earlyMappedRelated))
        {
            return earlyMappedRelated;
        }

        // Conditional types resolve or defer before anything else decides (null rules, the
        // type-parameter rules, primitives). A conditional that evaluates to a concrete type
        // restarts the comparison with the result; one that stays deferred goes through tsc's
        // relation rules for deferred conditionals below.
        if (expected is TypeInfo.ConditionalType expectedCondRaw)
        {
            var evaluated = EvaluateConditionalType(expectedCondRaw);
            if (evaluated is not TypeInfo.ConditionalType) return IsCompatible(evaluated, actual);
            expected = evaluated;
        }
        if (actual is TypeInfo.ConditionalType actualCondRaw)
        {
            var evaluated = EvaluateConditionalType(actualCondRaw);
            if (evaluated is not TypeInfo.ConditionalType) return IsCompatible(expected, evaluated);
            actual = evaluated;
        }
        if (expected is TypeInfo.ConditionalType || actual is TypeInfo.ConditionalType)
        {
            // Two deferred conditionals relate pairwise when their extends types are identical
            // and their check types relate in either direction: then true branch must relate to
            // true branch and false to false (checker.ts: conditional-to-conditional rule).
            // `Covariant<B> → Covariant<A>` (B extends A) relates this way without variance
            // machinery. On failure, fall through to the one-sided rules.
            if (expected is TypeInfo.ConditionalType expPair && actual is TypeInfo.ConditionalType actPair &&
                TypeInfoEqualityComparer.Instance.Equals(expPair.ExtendsType, actPair.ExtendsType) &&
                (IsCompatible(expPair.CheckType, actPair.CheckType) || IsCompatible(actPair.CheckType, expPair.CheckType)) &&
                IsCompatible(expPair.TrueType, actPair.TrueType) &&
                IsCompatible(expPair.FalseType, actPair.FalseType))
            {
                return true;
            }

            // Assigning FROM a deferred conditional: it is assignable wherever one of its
            // constraints is (distributive constraint, then the default true∩extends | false
            // union — see GetConditionalConstraints).
            if (actual is TypeInfo.ConditionalType sourceCond)
            {
                return GetConditionalConstraints(sourceCond).Any(c => IsCompatible(expected, c));
            }

            // Assigning TO a deferred conditional is sound only when the source satisfies BOTH
            // branches — the conditional could resolve to either at instantiation time.
            var targetCond = (TypeInfo.ConditionalType)expected;
            return IsCompatible(targetCond.TrueType, actual)
                && IsCompatible(targetCond.FalseType, actual);
        }

        // Type predicate compatibility:
        // - Regular type predicate (x is T): expects boolean return
        // - Assertion type predicate (asserts x is T): expects void return (function throws if assertion fails)
        // - AssertsNonNull (asserts x): expects void return
        if (expected is TypeInfo.TypePredicate pred)
        {
            if (pred.IsAssertion)
            {
                // Assertion predicates return void (or throw)
                return actual is TypeInfo.Void or TypeInfo.Never;
            }
            else
            {
                // Regular type predicates return boolean
                return actual is TypeInfo.Primitive { Type: Parsing.TokenType.TYPE_BOOLEAN }
                    or TypeInfo.BooleanLiteral;
            }
        }
        if (expected is TypeInfo.AssertsNonNull)
        {
            // AssertsNonNull returns void (or throws)
            return actual is TypeInfo.Void or TypeInfo.Never;
        }

        // Type-parameter compatibility (TypeScript: "type parameters are not assignable to one
        // another unless directly or indirectly constrained to one another").
        if (expected is TypeInfo.TypeParameter expectedTp && actual is TypeInfo.TypeParameter actualTp)
        {
            // The same parameter, or a source transitively constrained to the target (U extends … extends T).
            return expectedTp.Name == actualTp.Name || TypeParameterConstrainedTo(actualTp, expectedTp.Name);
        }

        // Expected is a bare type parameter and the source is some other type. An arbitrary concrete
        // type is NOT assignable to a type parameter — only `never`, or an intersection one of whose
        // constituents is (T & Function → T). (any / inferred and, under non-strict, null / undefined
        // are already accepted earlier in IsCompatibleCore; a source type parameter is handled by the
        // case above.) This is the strict TypeScript rule.
        if (expected is TypeInfo.TypeParameter)
        {
            if (actual is TypeInfo.Intersection actIntForTp)
                return actIntForTp.FlattenedTypes.Any(t => IsCompatible(expected, t));
            return actual is TypeInfo.Never;
        }

        // Source is a type parameter assigned to a non-parameter target: it is assignable wherever its
        // apparent (constraint) type is assignable. Also assignable into a union that contains it.
        if (actual is TypeInfo.TypeParameter actualTpOnly)
        {
            if (expected is TypeInfo.Any or TypeInfo.Unknown) return true;
            if (expected is TypeInfo.Union expUnionForTp &&
                expUnionForTp.FlattenedTypes.Any(t =>
                    t is TypeInfo.TypeParameter unionTp && unionTp.Name == actualTpOnly.Name))
            {
                return true;
            }
            var apparent = ApparentTypeOf(actualTpOnly);
            return apparent != null && IsCompatible(expected, apparent);
        }

        // never as actual: assignable to anything (bottom type)
        if (actual is TypeInfo.Never) return true;

        // never as expected: nothing assignable to never except never
        if (expected is TypeInfo.Never) return actual is TypeInfo.Never;

        // unknown as expected: anything can be assigned TO unknown (top type)
        if (expected is TypeInfo.Unknown) return true;

        // unknown as actual: can only be assigned to unknown or any
        if (actual is TypeInfo.Unknown)
            return expected is TypeInfo.Unknown || expected is TypeInfo.Any;

        // object type: accepts non-primitive, non-null values
        if (expected is TypeInfo.Object)
        {
            if (actual is TypeInfo.Never) return true;  // never is bottom type
            if (actual is TypeInfo.Any) return true;    // any is assignable to anything
            if (actual is TypeInfo.Object) return true; // object to object
            if (IsPrimitiveType(actual)) return false;  // reject primitives
            if (actual is TypeInfo.Null or TypeInfo.Undefined) return false;
            // Accept: Record, Array, Instance, Class, Function, Map, Set, etc.
            return true;
        }

        // object as actual: can only assign to object, any, unknown
        if (actual is TypeInfo.Object)
        {
            return expected is TypeInfo.Object or TypeInfo.Any or TypeInfo.Unknown;
        }

        // Null compatibility (strictNullChecks: on — the off case is handled early in IsCompatibleCore)
        if (actual is TypeInfo.Null)
        {
            if (expected is TypeInfo.Union u && u.ContainsNull) return true;
            if (expected is TypeInfo.Null) return true;
            return false;
        }

        // Undefined compatibility (strictNullChecks: on)
        if (actual is TypeInfo.Undefined)
        {
            if (expected is TypeInfo.Union u && u.ContainsUndefined) return true;
            if (expected is TypeInfo.Undefined) return true;
            return false;
        }

        // Literal type compatibility - literal to literal (must have same value)
        if (expected is TypeInfo.StringLiteral sl1 && actual is TypeInfo.StringLiteral sl2)
            return sl1.Value == sl2.Value;
        if (expected is TypeInfo.NumberLiteral nl1 && actual is TypeInfo.NumberLiteral nl2)
            return nl1.Value == nl2.Value;
        if (expected is TypeInfo.BooleanLiteral bl1 && actual is TypeInfo.BooleanLiteral bl2)
            return bl1.Value == bl2.Value;

        // Literal to primitive widening
        if (expected is TypeInfo.String && actual is TypeInfo.StringLiteral)
            return true;
        if (expected is TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER } && actual is TypeInfo.NumberLiteral)
            return true;
        if (expected is TypeInfo.Primitive { Type: TokenType.TYPE_BOOLEAN } && actual is TypeInfo.BooleanLiteral)
            return true;

        // Template literal type compatibility

        // Template literal widens to string
        if (expected is TypeInfo.String && actual is TypeInfo.TemplateLiteralType)
            return true;

        // String literal matches template literal pattern
        if (expected is TypeInfo.TemplateLiteralType expectedTL && actual is TypeInfo.StringLiteral actualSL)
            return MatchesTemplateLiteralPattern(expectedTL, actualSL.Value);

        // Template literal to template literal: structural compatibility
        if (expected is TypeInfo.TemplateLiteralType expTL && actual is TypeInfo.TemplateLiteralType actTL)
            return TemplatePatternStructurallyCompatible(expTL, actTL);

        // Intrinsic string type: evaluate and check
        if (actual is TypeInfo.IntrinsicStringType ist)
        {
            var evaluated = EvaluateIntrinsicStringType(ist.Inner, ist.Operation);
            return IsCompatible(expected, evaluated);
        }

        // Union-to-union: each type in actual must be compatible with at least one type in expected
        if (expected is TypeInfo.Union expectedUnion && actual is TypeInfo.Union actualUnion)
        {
            var expectedTypes = expectedUnion.FlattenedTypes;
            // Same enum-absorbed-by-primitive reduction as the union-as-actual branch below (#894).
            var actualTypes = ReduceEnumMembersAbsorbedByPrimitive(actualUnion.FlattenedTypes);
            // Per source constituent: some-target-constituent first, then the discriminated
            // path (a constituent may relate only through its discriminant combinations).
            return actualTypes.All(actualType =>
                expectedTypes.Any(expectedType => IsCompatible(expectedType, actualType)) ||
                RelatedToDiscriminatedUnion(expectedUnion, actualType));
        }

        // Union as expected: actual must match at least one member — or relate through the
        // discriminated-union path (tsc typeRelatedToDiscriminatedType).
        if (expected is TypeInfo.Union expUnion)
        {
            var expTypes = expUnion.FlattenedTypes;
            return expTypes.Any(t => IsCompatible(t, actual)) ||
                RelatedToDiscriminatedUnion(expUnion, actual);
        }

        // Union as actual: all members must be compatible with expected. Drop enum members
        // absorbed by a sibling primitive first (e | number → number): enum→enum is nominal, so
        // the un-reduced `e` arm would wrongly reject `(e | number) → E2` (#894).
        if (actual is TypeInfo.Union actUnion)
        {
            var actTypes = ReduceEnumMembersAbsorbedByPrimitive(actUnion.FlattenedTypes);
            return actTypes.All(t => IsCompatible(expected, t));
        }

        // Intersection as expected: actual must satisfy ALL member types
        if (expected is TypeInfo.Intersection expIntersection)
        {
            var expTypes = expIntersection.FlattenedTypes;
            return expTypes.All(t => IsCompatible(t, actual));
        }

        // Intersection as actual: satisfies expected if any member does
        // (because intersection value has all the properties of all its constituents)
        if (actual is TypeInfo.Intersection actIntersection)
        {
            var actTypes = actIntersection.FlattenedTypes;
            return actTypes.Any(t => IsCompatible(expected, t));
        }

        // keyof X ← keyof Y with generic operands compares the OPERANDS, contravariantly:
        // Y's keys include X's exactly when X is assignable to Y (keyof B accepts keyof A
        // when B extends A — B has at least A's keys). Expansion can't decide this (generic
        // operands have no concrete key set).
        if (expected is TypeInfo.KeyOf expKoPair && actual is TypeInfo.KeyOf actKoPair &&
            (IsGenericTypeShape(expKoPair.SourceType) || IsGenericTypeShape(actKoPair.SourceType)))
        {
            return TypeInfoEqualityComparer.Instance.Equals(expKoPair.SourceType, actKoPair.SourceType)
                || IsCompatible(actKoPair.SourceType, expKoPair.SourceType);
        }

        // KeyOf type compatibility - must evaluate to compare
        // Special handling for keyof T where T is a type parameter - don't try to expand
        if (expected is TypeInfo.KeyOf expectedKeyOf)
        {
            // If source is a type parameter, don't expand to avoid infinite recursion
            if (expectedKeyOf.SourceType is TypeInfo.TypeParameter)
            {
                // keyof T is compatible with string, number, symbol, or any
                return actual is TypeInfo.String or TypeInfo.StringLiteral or TypeInfo.Any or
                       TypeInfo.Primitive { Type: Parsing.TokenType.TYPE_NUMBER } or
                       TypeInfo.NumberLiteral or TypeInfo.Symbol or TypeInfo.TypeParameter;
            }
            TypeInfo expandedExpected = EvaluateKeyOf(expectedKeyOf.SourceType);
            return IsCompatible(expandedExpected, actual);
        }
        if (actual is TypeInfo.KeyOf actualKeyOf)
        {
            // If source is a type parameter, don't expand to avoid infinite recursion
            if (actualKeyOf.SourceType is TypeInfo.TypeParameter)
            {
                // keyof T is compatible with string, number, symbol, or any
                return expected is TypeInfo.String or TypeInfo.StringLiteral or TypeInfo.Any or
                       TypeInfo.Primitive { Type: Parsing.TokenType.TYPE_NUMBER } or
                       TypeInfo.NumberLiteral or TypeInfo.Symbol or TypeInfo.KeyOf;
            }
            TypeInfo expandedActual = EvaluateKeyOf(actualKeyOf.SourceType);
            return IsCompatible(expected, expandedActual);
        }

        // Mapped type compatibility - expand lazily then compare. (Deferred-key-domain mapped
        // targets are related structurally earlier, before the type-parameter rules.)
        if (expected is TypeInfo.MappedType expectedMapped)
        {
            TypeInfo expandedExpected = ExpandMappedType(expectedMapped);
            return IsCompatible(expandedExpected, actual);
        }
        if (actual is TypeInfo.MappedType actualMapped)
        {
            TypeInfo expandedActual = ExpandMappedType(actualMapped);
            return IsCompatible(expected, expandedActual);
        }

        // Indexed access type compatibility - resolve then compare
        if (expected is TypeInfo.IndexedAccess expectedIA)
        {
            TypeInfo expandedExpected = ResolveIndexedAccess(expectedIA, new Dictionary<string, TypeInfo>());
            return IsCompatible(expandedExpected, actual);
        }
        if (actual is TypeInfo.IndexedAccess actualIA)
        {
            TypeInfo expandedActual = ResolveIndexedAccess(actualIA, new Dictionary<string, TypeInfo>());
            return IsCompatible(expected, expandedActual);
        }

        // InferredTypeParameter should not appear in compatibility checks
        // (they should be resolved during conditional type evaluation)
        if (expected is TypeInfo.InferredTypeParameter || actual is TypeInfo.InferredTypeParameter)
        {
            return false; // Unresolved infer parameters are not compatible with anything
        }

        // Enum compatibility. Same enum (or its members, which now type as the enum) is
        // assignable; a plain widened `number` still flows into a numeric enum
        // (numberAssignableToEnum pins this) — but a numeric LITERAL does not, even when it
        // matches a member value (enumAssignability: `e = 1` errors; literals are not Primitive
        // here so they fall through to false).
        if (expected is TypeInfo.Enum expectedEnum)
        {
            // Same enum type is compatible
            if (actual is TypeInfo.Enum actualEnum && expectedEnum.Name == actualEnum.Name)
                return true;

            // Numeric enum accepts (widened) number
            if (expectedEnum.Kind == EnumKind.Numeric &&
                actual is TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER })
                return true;

            // String enum accepts string
            if (expectedEnum.Kind == EnumKind.String && actual is TypeInfo.String)
                return true;

            // Heterogeneous enum accepts both
            if (expectedEnum.Kind == EnumKind.Heterogeneous &&
                (actual is TypeInfo.String || actual is TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER }))
                return true;

            return false;
        }

        // Enum as actual: can be assigned to compatible primitive type
        // (e.g., a Direction variable can be used where a number is expected)
        if (actual is TypeInfo.Enum actualEnumType)
        {
            if (actualEnumType.Kind == EnumKind.Numeric &&
                expected is TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER })
                return true;

            if (actualEnumType.Kind == EnumKind.String && expected is TypeInfo.String)
                return true;

            if (actualEnumType.Kind == EnumKind.Heterogeneous &&
                (expected is TypeInfo.String || expected is TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER }))
                return true;
        }

        if (expected is TypeInfo.Primitive p1 && actual is TypeInfo.Primitive p2)
        {
            return p1.Type == p2.Type;
        }

        // String type compatibility
        if (expected is TypeInfo.String && actual is TypeInfo.String)
        {
            return true;
        }

        // UniqueSymbol type compatibility (nominal typing)
        // unique symbol -> unique symbol: must be same declaration
        if (expected is TypeInfo.UniqueSymbol expectedUnique)
        {
            if (actual is TypeInfo.UniqueSymbol actualUnique)
                return expectedUnique.DeclarationId == actualUnique.DeclarationId;
            return false; // regular symbol NOT assignable to unique symbol
        }

        // Symbol type compatibility
        // symbol accepts both symbol and unique symbol (unique symbol is subtype of symbol)
        if (expected is TypeInfo.Symbol)
        {
            return actual is TypeInfo.Symbol or TypeInfo.UniqueSymbol;
        }

        // BigInt type compatibility
        if (expected is TypeInfo.BigInt && actual is TypeInfo.BigInt)
        {
            return true;
        }

        // Buffer type compatibility
        if (expected is TypeInfo.Buffer && actual is TypeInfo.Buffer)
        {
            return true;
        }

        // Date / RegExp identity (these only became reachable as TYPE-position types once the
        // global names stopped resolving to `any`).
        if (expected is TypeInfo.Date && actual is TypeInfo.Date) return true;
        if (expected is TypeInfo.RegExp && actual is TypeInfo.RegExp) return true;

        // Typed-array identity (reachable since `Int32Array`/`Float64Array`/… annotations stopped
        // resolving to `any`). Each typed array is its own nominal type: assignable only from the
        // same element kind (Int32Array ← Int32Array, not ← Float64Array), matching tsc.
        if (expected is TypeInfo.TypedArray expTa && actual is TypeInfo.TypedArray actTa)
            return expTa.ElementType == actTa.ElementType;

        // Error-family compatibility (reachable since Error/TypeError/… stopped resolving to `any`,
        // #528). Every built-in error shares the same structural shape (name/message/stack/cause), so
        // any error satisfies a non-aggregate error target — tsc treats the empty `interface TypeError
        // extends Error {}` declarations as mutually assignable. AggregateError additionally carries
        // `errors`, so only an AggregateError satisfies an AggregateError target. A user class that
        // extends a built-in error (class MyError extends Error) is a nominal subtype of it.
        if (expected is TypeInfo.Error expErr)
        {
            if (actual is TypeInfo.Error actErr)
                return expErr.Name != "AggregateError" || actErr.Name == "AggregateError";
            if (expErr.Name != "AggregateError" && actual is TypeInfo.Instance errInst && ExtendsBuiltInError(errInst))
                return true;
        }

        // Promise type compatibility - Promise<A> is compatible with Promise<B> if A is compatible with B
        if (expected is TypeInfo.Promise expPromise && actual is TypeInfo.Promise actPromise)
        {
            return IsCompatible(expPromise.ValueType, actPromise.ValueType);
        }

        // Map type compatibility - Map<K1, V1> is compatible with Map<K2, V2> if K1=K2 and V1=V2
        if (expected is TypeInfo.Map expMap && actual is TypeInfo.Map actMap)
        {
            return IsCompatible(expMap.KeyType, actMap.KeyType) &&
                   IsCompatible(expMap.ValueType, actMap.ValueType);
        }

        // Set type compatibility - Set<T1> is compatible with Set<T2> if T1=T2
        if (expected is TypeInfo.Set expSet && actual is TypeInfo.Set actSet)
        {
            return IsCompatible(expSet.ElementType, actSet.ElementType);
        }

        // WeakMap type compatibility - WeakMap<K1, V1> is compatible with WeakMap<K2, V2> if K1=K2 and V1=V2
        if (expected is TypeInfo.WeakMap expWeakMap && actual is TypeInfo.WeakMap actWeakMap)
        {
            return IsCompatible(expWeakMap.KeyType, actWeakMap.KeyType) &&
                   IsCompatible(expWeakMap.ValueType, actWeakMap.ValueType);
        }

        // WeakSet type compatibility - WeakSet<T1> is compatible with WeakSet<T2> if T1=T2
        if (expected is TypeInfo.WeakSet expWeakSet && actual is TypeInfo.WeakSet actWeakSet)
        {
            return IsCompatible(expWeakSet.ElementType, actWeakSet.ElementType);
        }

        // WeakRef type compatibility - WeakRef<T1> is compatible with WeakRef<T2> if T1=T2
        if (expected is TypeInfo.WeakRef expWeakRef && actual is TypeInfo.WeakRef actWeakRef)
        {
            return IsCompatible(expWeakRef.TargetType, actWeakRef.TargetType);
        }

        // Iterator<T> compatibility (the IterableIterator/Iterator annotations resolved by
        // ResolveGenericType and the records .keys()/.values()/.entries() produce). A dedicated
        // iterator record or a sync Generator carries the element/yield type directly — a sync
        // Generator structurally IS an iterator (Generator<T> extends IterableIterator<T> extends
        // Iterator<T>), so it satisfies an Iterator-typed target too; without that, assigning a
        // generator to an `IterableIterator<T>` annotation would regress now that the annotation is
        // strongly typed rather than `any` (#456). AsyncGenerator is deliberately excluded: it belongs
        // to the separate async-iterator hierarchy and is not a sync Iterator.
        if (expected is TypeInfo.Iterator expIter)
        {
            if ((actual switch
                {
                    TypeInfo.Iterator it => it.ElementType,
                    TypeInfo.Generator g => g.YieldType,
                    _ => (TypeInfo?)null
                }) is { } actualIterElem)
            {
                return IsCompatible(expIter.ElementType, actualIterElem);
            }

            // Honour TS's structural typing of the iterator protocol: an object exposing a callable `next`
            // satisfies `Iterator<T>` (e.g. a hand-written `{ next() {…} }` literal). The element type is
            // re-derived from `next().value` (the IteratorResult value) and checked against T (#485): an
            // untyped/union next yields `any`, which stays compatible, so the structural iterators accepted
            // since #456 do not regress, while a real element mismatch is now rejected. Dedicated iterator
            // records are invisible to GetMemberType, so only genuine structural objects reach here.
            if (TryGetStructuralIteratorElement(actual, out var structuralIterElem))
                return IsCompatible(expIter.ElementType, structuralIterElem);
            // Not an iterator-shaped source: fall through to the remaining (ultimately rejecting) rules.
        }

        // Generator<T1>/AsyncGenerator<T1> relate to the SAME kind when the yield types relate. These
        // records resolved from a type reference before #456 but had no compatibility arm, so even
        // `let g: Generator<number> = gen()` spuriously failed (every same-record pair without an arm
        // falls through to `return false`); #456 completes the iterable-record family it touches.
        if (expected is TypeInfo.Generator expGen)
        {
            if (actual is TypeInfo.Generator actGen)
                return IsCompatible(expGen.YieldType, actGen.YieldType);
            // A structural object satisfies Generator<T1> when it is at least an IterableIterator — it
            // exposes BOTH the iterator protocol (next) and the iterable protocol ([Symbol.iterator]); its
            // element comes from next().value and is checked against T1 (#485). Requiring both protocols
            // keeps a bare, non-iterable `{ next() {…} }` from passing. Dedicated Iterator/AsyncGenerator
            // records are invisible to the structural lookup, so the asymmetric relations stay rejected.
            if (TryGetStructuralIteratorElement(actual, out var genElem) &&
                TryGetStructuralIterableElement(actual, out _))
                return IsCompatible(expGen.YieldType, genElem);
        }
        if (expected is TypeInfo.AsyncGenerator expAsyncGen)
        {
            if (actual is TypeInfo.AsyncGenerator actAsyncGen)
                return IsCompatible(expAsyncGen.YieldType, actAsyncGen.YieldType);
            // A structural object satisfies AsyncGenerator<T1> when it is at least an AsyncIterableIterator —
            // it exposes BOTH the async iterator protocol (next returning Promise<IteratorResult<T>>) AND the
            // async iterable protocol ([Symbol.asyncIterator]); its element comes from the Promise-unwrapped
            // next().value and is checked against T1 (#549, async parallel of #485 / the sync Generator arm
            // above). Requiring both protocols keeps a bare async `{ next() {…} }` — an AsyncIterator, handled
            // by its own arm below — from passing. Dedicated AsyncIterator records are invisible to the
            // structural lookup, so the asymmetric relations stay rejected.
            if (TryGetStructuralAsyncIteratorElement(actual, out var asyncGenElem) &&
                TryGetStructuralAsyncIterableElement(actual, out _))
                return IsCompatible(expAsyncGen.YieldType, asyncGenElem);
        }

        // AsyncIterator<T1>/AsyncIterableIterator<T1> (#483, async parallel of the sync Iterator arm above):
        // an AsyncIterator source (self) and an AsyncGenerator (which structurally IS an AsyncIterator)
        // satisfy it when the element types relate, so a strict `let it: AsyncIterableIterator<number> =
        // agen()` does not regress. An AsyncIterable (only `[Symbol.asyncIterator]`, no `next`) is NOT an
        // AsyncIterator and falls through to rejection — the async mirror of Iterable ↛ Iterator.
        if (expected is TypeInfo.AsyncIterator expAsyncIter)
        {
            TypeInfo? asyncIterSrcElem = actual switch
            {
                TypeInfo.AsyncIterator a => a.ElementType,
                TypeInfo.AsyncGenerator g => g.YieldType,
                _ => null
            };
            if (asyncIterSrcElem is not null)
                return IsCompatible(expAsyncIter.ElementType, asyncIterSrcElem);
            // Not async-iterator-shaped: fall through to the remaining (ultimately rejecting) rules.
        }

        // AsyncIterable<T1> (#483, async parallel of the sync Iterable arm below): the dedicated async
        // records — AsyncIterable (self), the AsyncIterator record (= AsyncIterableIterator, which IS an
        // AsyncIterable) and AsyncGenerator — satisfy it when their element types relate. A sync iterable
        // is NOT an AsyncIterable and falls through to rejection.
        if (expected is TypeInfo.AsyncIterable expAsyncIterable)
        {
            if (TryGetAsyncIterableElementType(actual, out var asyncIterableSrcElem))
                return IsCompatible(expAsyncIterable.ElementType, asyncIterableSrcElem);
            // Not async-iterable: fall through to the remaining (ultimately rejecting) rules.
        }

        // Iterable<T1> (#485): a sync-iterable source satisfies it when its element type relates to T1 —
        // arrays, sets, maps ([K, V]), strings, the dedicated iterator/generator records, and structural
        // objects exposing [Symbol.iterator]. Covariant in the element, like the other container records.
        // The async-iterator hierarchy (AsyncGenerator/AsyncIterable) is not a sync Iterable and falls
        // through to rejection.
        if (expected is TypeInfo.Iterable expIterable)
        {
            if (TryGetIterableElementType(actual, out var iterableSrcElem))
                return IsCompatible(expIterable.ElementType, iterableSrcElem);
            // Not iterable: fall through to the remaining (ultimately rejecting) rules.
        }

        // FinalizationRegistry<T1> is compatible with FinalizationRegistry<T2> if T1=T2 (#456).
        if (expected is TypeInfo.FinalizationRegistry expFinReg && actual is TypeInfo.FinalizationRegistry actFinReg)
        {
            return IsCompatible(expFinReg.TargetType, actFinReg.TargetType);
        }

        // Member accessibility (TypeScript private/protected): two object types are unrelated when a
        // member they share has a conflicting accessibility origin — a public member cannot satisfy a
        // private one, and two private members must originate from the same declaration. Applies in
        // both directions to every object-like pair, ahead of the structural paths below.
        if (IsObjectLikeForAccessibility(expected) && IsObjectLikeForAccessibility(actual) &&
            !MembersAccessibilityCompatible(expected, actual))
            return false;

        if (expected is TypeInfo.Instance i1 && actual is TypeInfo.Instance i2)
        {
            // Handle InstantiatedGeneric expected type - check if actual's class hierarchy includes it
            if (i1.ClassType is TypeInfo.InstantiatedGeneric expectedIG)
            {
                // Check if actual is also the same InstantiatedGeneric
                if (i2.ClassType is TypeInfo.InstantiatedGeneric actualIG)
                {
                    // Same generic definition and compatible type arguments (variance-aware)
                    if (expectedIG.GenericDefinition is TypeInfo.GenericClass gc1 &&
                        actualIG.GenericDefinition is TypeInfo.GenericClass gc2 &&
                        gc1.Name == gc2.Name)
                    {
                        if (expectedIG.TypeArguments.Count != actualIG.TypeArguments.Count)
                            return false;
                        // Check type arguments respecting variance annotations
                        if (!AreTypeArgumentsCompatible(gc1.TypeParams, expectedIG.TypeArguments, actualIG.TypeArguments))
                            return false;
                        return true;
                    }
                    // Check if actualIG's hierarchy includes expectedIG
                    return IsInSuperclassChain(actualIG, expectedIG);
                }

                // Check if actual is a regular Class that extends the expected InstantiatedGeneric
                // e.g., NumberBox extends Box<number>, checking if NumberBox assignable to Box<number>
                if (i2.ClassType is TypeInfo.Class actualClassForIG)
                {
                    return IsInSuperclassChain(actualClassForIG, expectedIG);
                }
            }

            // Handle regular Class comparison (including MutableClass resolution)
            // Use ResolvedClassType to handle MutableClass instances that may occur during signature collection
            var resolvedExpected = i1.ResolvedClassType;
            var resolvedActual = i2.ResolvedClassType;

            if (resolvedExpected is TypeInfo.Class expectedClass && resolvedActual is TypeInfo.Class actualClass)
            {
                // Check direct class hierarchy (by name)
                TypeInfo? current = actualClass;
                while (current != null)
                {
                    if (current is TypeInfo.Class cls && cls.Name == expectedClass.Name) return true;
                    current = GetSuperclass(current);
                }

                // Structural compatibility (TypeScript): when the target class carries no nominal
                // brand, a source instance is assignable if it structurally provides the target's
                // public members and satisfies its index signatures (generic args substituted). A
                // branded or member-less/index-less target stays nominal (handled by the walk above,
                // preserving subclass-safety).
                if (StructurallyAssignableToClassTarget(expectedClass, i2)) return true;
            }
            // Handle MutableClass (unfrozen) comparison by name - occurs during signature collection
            else if (resolvedExpected is TypeInfo.MutableClass mc1 && resolvedActual is TypeInfo.MutableClass mc2)
            {
                return mc1.Name == mc2.Name;
            }

            // Mixed case: InstantiatedGeneric vs regular Class - not compatible unless in hierarchy
            return false;
        }

        // Structural (TypeScript): an unbranded target class instance — including a generic-class
        // instantiation like `A<Base>` — accepts any structurally-matching object-like source (an
        // interface value or object literal/record). Class-instance sources are handled by the
        // Instance-vs-Instance block above; the reverse directions (a class instance assignable to an
        // interface/record) are handled by the structural paths below.
        if (expected is TypeInfo.Instance targetInst &&
            actual is TypeInfo.Interface or TypeInfo.Record &&
            StructurallyAssignableToClassTarget(targetInst.ResolvedClassType, actual))
        {
            return true;
        }

        // tsc's weak-type check (TS2559): an all-optional-properties target rejects a source
        // with properties but none in common, despite being vacuously satisfied structurally.
        if (FailsWeakTypeCheck(expected, actual))
        {
            return false;
        }

        if (expected is TypeInfo.Interface itf)
        {
            // Callable interface: every target call signature must be satisfied by the source.
            if (itf.IsCallable)
            {
                if (NormalizeSignature(actual) is { } funcSource)
                    return CallSignaturesSatisfiedBy(itf.CallSignatures!, [funcSource]);

                if (GetCallSignatures(actual) is { } actualCallSigs)
                {
                    if (!CallSignaturesSatisfiedBy(itf.CallSignatures!, actualCallSigs.Select(NormalizeCallSignature).ToList()))
                        return false;
                    // Non-signature members (if any) still checked by the structural path below.
                    if (itf.Members.Count == 0) return true;
                }
                // Other actuals may still be callable via downstream paths — fall through rather
                // than rejecting here.
            }

            // Constructable interface: the source must satisfy the construct signatures.
            if (itf.IsConstructable)
            {
                if (actual is TypeInfo.Class cls)
                    return ClassMatchesConstructorSignatures(cls, itf.ConstructorSignatures!);

                if (GetConstructorSignatures(actual) is { } actualCtorSigs)
                {
                    if (!ConstructorSignaturesSatisfiedBy(itf.ConstructorSignatures!, actualCtorSigs)) return false;
                    if (itf.Members.Count == 0) return true;
                }
                else if (actual is not TypeInfo.Any)
                {
                    return false; // a constructable interface is not satisfied by a non-constructable source
                }
            }

            // If actual is also an interface, compare member-to-member structurally
            if (actual is TypeInfo.Interface actualItf)
            {
                // Check that actual has all required members with compatible types (including inherited)
                var allExpectedMembers = itf.GetAllMembers().ToDictionary(m => m.Key, m => m.Value);
                var allExpectedOptional = itf.GetAllOptionalMembers().ToHashSet();
                var allActualMembers = actualItf.GetAllMembers().ToDictionary(m => m.Key, m => m.Value);
                var allActualOptional = actualItf.GetAllOptionalMembers().ToHashSet();

                foreach (var member in allExpectedMembers)
                {
                    if (!allActualMembers.TryGetValue(member.Key, out var actualMemberType))
                    {
                        // Member missing - check if optional
                        if (!allExpectedOptional.Contains(member.Key))
                            return false;
                    }
                    // A source-OPTIONAL member never satisfies a target-REQUIRED one (presence,
                    // not type: tsc errors regardless of strictNullChecks).
                    else if (!allExpectedOptional.Contains(member.Key) && allActualOptional.Contains(member.Key))
                    {
                        return false;
                    }
                    else if (!IsMemberCompatible(member.Value, actualMemberType,
                                 itf.IsMethodMember(member.Key) || actualItf.IsMethodMember(member.Key)))
                    {
                        return false;
                    }
                }
                return IndexSignaturesSatisfied(itf, actualItf);
            }
            // Use GetAllMembers to include inherited members when checking structural compatibility
            var allMembers = itf.GetAllMembers().ToDictionary(m => m.Key, m => m.Value);
            var allOptional = itf.GetAllOptionalMembers().ToHashSet();
            return CheckStructuralCompatibility(allMembers, actual, allOptional)
                && IndexSignaturesSatisfied(itf, actual);
        }

        // Handle InstantiatedGeneric interface (e.g., Container<number>)
        if (expected is TypeInfo.InstantiatedGeneric expectedInterfaceIG &&
            expectedInterfaceIG.GenericDefinition is TypeInfo.GenericInterface gi)
        {
            // Check if actual is also the same generic interface with different type arguments
            if (actual is TypeInfo.InstantiatedGeneric actualInterfaceIG &&
                actualInterfaceIG.GenericDefinition is TypeInfo.GenericInterface actualGI &&
                gi.Name == actualGI.Name)
            {
                // Same generic interface - check type arguments with variance
                if (expectedInterfaceIG.TypeArguments.Count != actualInterfaceIG.TypeArguments.Count)
                    return false;
                if (AreTypeArgumentsCompatible(gi.TypeParams, expectedInterfaceIG.TypeArguments, actualInterfaceIG.TypeArguments))
                    return true;
                // Unannotated type parameters compare invariantly above, but tsc relates two
                // instantiations of the same generic by MEASURED variance (computed from member
                // positions) — e.g. P<Derived> is assignable to P<Base> when T only occupies
                // covariant positions. Explicit variance annotations are authoritative (tsc uses
                // them as declared), so the measured fallback only applies when every parameter
                // is unannotated.
                if (gi.TypeParams.Any(tp => tp.Variance != TypeParameterVariance.Invariant))
                    return false;
                return SameGenericInstantiationsStructurallyCompatible(
                    gi, expectedInterfaceIG, actualInterfaceIG);
            }

            // Build substitution map for structural comparison
            Dictionary<string, TypeInfo> subs = [];
            for (int i = 0; i < gi.TypeParams.Count; i++)
                subs[gi.TypeParams[i].Name] = expectedInterfaceIG.TypeArguments[i];

            // Substitute type parameters in interface members
            Dictionary<string, TypeInfo> substitutedMembers = [];
            foreach (var kvp in gi.Members)
                substitutedMembers[kvp.Key] = Substitute(kvp.Value, subs);

            // The instantiation's index signatures constrain the source too — `A<T>` with
            // `[x: string]: T` only accepts sources whose index/members fit the substituted T
            // (StringIndexOf/NumberIndexOf substitute the instantiation's arguments).
            if (!IndexSignaturesSatisfied(expected, actual)) return false;

            return CheckStructuralCompatibility(substitutedMembers, actual, gi.OptionalMembers);
        }

        if (expected is TypeInfo.Array a1 && actual is TypeInfo.Array a2)
        {
            return IsCompatible(a1.ElementType, a2.ElementType);
        }

        // Callable / constructable inline object types: `{ (x): T }`, `{ new (x): T }`, and mixed
        // forms with named fields. Mirrors the callable-interface handling above.
        if (expected is TypeInfo.Record exSigRec && (exSigRec.IsCallable || exSigRec.IsConstructable))
        {
            if (exSigRec.IsCallable)
            {
                if (NormalizeSignature(actual) is { } funcSource)
                {
                    if (!CallSignaturesSatisfiedBy(exSigRec.CallSignatures!, [funcSource])) return false;
                }
                else if (GetCallSignatures(actual) is { } actualSigs)
                {
                    // Callable interface/object source: each expected signature must be matched.
                    if (!CallSignaturesSatisfiedBy(exSigRec.CallSignatures!, actualSigs.Select(NormalizeCallSignature).ToList()))
                        return false;
                }
                // Other actuals may still be callable via downstream paths.
            }

            if (exSigRec.IsConstructable)
            {
                if (actual is TypeInfo.Class constructableCls)
                {
                    if (!ClassMatchesConstructorSignatures(constructableCls, exSigRec.ConstructorSignatures!)) return false;
                }
                else if (GetConstructorSignatures(actual) is { } actualCtorSigs)
                {
                    if (!ConstructorSignaturesSatisfiedBy(exSigRec.ConstructorSignatures!, actualCtorSigs)) return false;
                }
                else if (actual is not TypeInfo.Any)
                {
                    return false; // a constructable object type is not satisfied by a non-constructable source
                }
            }

            // Mixed object types still must match their named fields structurally.
            if (exSigRec.Fields.Count > 0)
                return CheckStructuralCompatibility(exSigRec.Fields, actual, exSigRec.OptionalFields);
            return true;
        }

        // Record-to-Record compatibility (inline object types)
        if (expected is TypeInfo.Record expRecord && actual is TypeInfo.Record actRecord)
        {
            // All required fields in expected must exist in actual with compatible types
            // Optional fields can be omitted
            foreach (var (name, expectedFieldType) in expRecord.Fields)
            {
                if (!actRecord.Fields.TryGetValue(name, out var actualFieldType))
                {
                    // Field missing - only OK if the field is optional
                    if (!expRecord.IsFieldOptional(name))
                        return false;
                }
                // Source-optional vs target-required: not satisfied (presence rule).
                else if (!expRecord.IsFieldOptional(name) && actRecord.IsFieldOptional(name))
                {
                    return false;
                }
                else if (!IsMemberCompatible(expectedFieldType, actualFieldType,
                             expRecord.IsMethodMember(name) || actRecord.IsMethodMember(name)))
                {
                    return false;
                }
            }
            // Named fields match; the target's index signatures (if any) must also be satisfied by
            // the source's members and own index signatures.
            return IndexSignaturesSatisfied(expRecord, actRecord);
        }

        // Record constraint compatibility with types that have members (String, Array, etc.)
        // This handles cases like `T extends { length: number }` with strings or arrays
        if (expected is TypeInfo.Record expRec)
        {
            // Use CheckStructuralCompatibility to check if actual type has all required fields
            return CheckStructuralCompatibility(expRec.Fields, actual, expRec.OptionalFields)
                && IndexSignaturesSatisfied(expRec, actual);
        }

        // Tuple-to-tuple compatibility
        if (expected is TypeInfo.Tuple expTuple && actual is TypeInfo.Tuple actTuple)
        {
            return IsTupleCompatible(expTuple, actTuple);
        }

        // Tuple assignable to array (e.g., [string, number] -> (string | number)[])
        if (expected is TypeInfo.Array expArr && actual is TypeInfo.Tuple actTuple2)
        {
            return IsTupleToArrayCompatible(expArr, actTuple2);
        }

        // Array assignable to tuple (limited - only for rest tuples or all-optional)
        if (expected is TypeInfo.Tuple expTuple2 && actual is TypeInfo.Array actArr)
        {
            return IsArrayToTupleCompatible(expTuple2, actArr);
        }

        if (expected is TypeInfo.Void && actual is TypeInfo.Void) return true;

        // OverloadedFunction expected: actual function must satisfy all overload signatures
        // This handles cases like interface method overloads being satisfied by a union-parameter function
        if (expected is TypeInfo.OverloadedFunction overloadedFunc && actual is TypeInfo.Function actualFunc)
        {
            // The actual function must satisfy ALL overload signatures
            foreach (var signature in overloadedFunc.Signatures)
            {
                if (!IsFunctionCompatibleWithSignature(actualFunc, signature))
                    return false;
            }
            return true;
        }

        // Function type compatibility
        // A callable interface or object type (`{ (x): T }`) is assignable to a function-typed
        // target (plain or generic) when one of its call signatures satisfies that function type.
        if (NormalizeSignature(expected) is { } expectedFnSig && GetCallSignatures(actual) is { } sourceCallSigs)
        {
            return CallableAssignableToFunction(sourceCallSigs, expectedFnSig);
        }

        // Function / generic-function relation, unified. Normalize each side to a signature shape
        // (optional type parameters + a function shape) and relate them, so plain `Function` and
        // `GenericFunction` participate through one code path. Contextual signature instantiation is
        // applied inside SignatureRelatedTo when the source is generic and the target is not.
        if (NormalizeSignature(expected) is { } targetSig && NormalizeSignature(actual) is { } sourceSig)
        {
            return SignatureRelatedTo(sourceSig, targetSig);
        }

        return false;
    }

    /// <summary>
    /// Checks if an actual function can satisfy a specific signature from an overloaded function.
    /// Used for structural typing when an interface has overloaded methods.
    /// The actual function with union parameters can satisfy multiple specific signatures.
    /// </summary>
    private bool IsFunctionCompatibleWithSignature(TypeInfo.Function actualFunc, TypeInfo.Function signature)
    {
        // The actual function must have at least as many parameters as the signature requires
        // But it can have more parameters (they'd be optional or union parameters)
        if (actualFunc.ParamTypes.Count < signature.MinArity)
            return false;

        // For each parameter position in the signature, the signature's param type
        // must be assignable to the actual param type (contravariance)
        // This ensures the actual function can accept calls matching the signature
        for (int i = 0; i < signature.ParamTypes.Count && i < actualFunc.ParamTypes.Count; i++)
        {
            // Signature param must be assignable to actual param (contravariance for function params)
            // If signature expects (number), actual can accept (number | string)
            if (!IsCompatible(actualFunc.ParamTypes[i], signature.ParamTypes[i]))
                return false;
        }

        // Return type: actual must be assignable to signature (covariance)
        if (signature.ReturnType is not TypeInfo.Void)
        {
            if (!IsCompatible(signature.ReturnType, actualFunc.ReturnType))
                return false;
        }

        return true;
    }
}

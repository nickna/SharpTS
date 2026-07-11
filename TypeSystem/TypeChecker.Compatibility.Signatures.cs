using System.Collections.Frozen;

namespace SharpTS.TypeSystem;

/// <summary>
/// Unified function-signature assignability: relates <see cref="TypeInfo.Function"/> and
/// <see cref="TypeInfo.GenericFunction"/> through one path, including contextual signature
/// instantiation when a generic source is assigned to a non-generic target.
/// </summary>
/// <remarks>
/// Before this, <c>GenericFunction</c> had no case in <c>IsCompatibleCore</c> at all, so any
/// comparison involving a generic signature fell through to the bare type-parameter rules or off
/// the end to <c>false</c> — incoherently (spuriously rejecting valid generic-source assignments
/// while silently accepting invalid non-generic-source-to-generic-target ones).
///
/// The model mirrors TypeScript's <c>signatureRelatedTo</c>:
/// <list type="bullet">
/// <item>Generic source → non-generic target: instantiate the source's type parameters by inferring
/// them from the target's parameters (un-inferred ones default to <c>{}</c>), then relate the
/// instantiated shape. This is why <c>aN = bN</c> (assign a generic fn to a concrete-typed slot)
/// type-checks while a return-only type parameter that can't be inferred collapses to <c>{}</c> and
/// fails the return check.</item>
/// <item>Everything else (non-generic source, or a generic <em>target</em>): relate the shapes
/// directly, leaving type parameters opaque. The existing type-parameter rules then correctly reject
/// a concrete source against a bare type parameter — so <c>bN = aN</c> (assign a concrete fn to a
/// generic-typed slot) is an error, since the target must hold for every instantiation.</item>
/// </list>
/// </remarks>
public partial class TypeChecker
{
    /// <summary>A callable signature reduced to its type parameters and a plain function shape.</summary>
    private readonly record struct NormalizedSignature(
        List<TypeInfo.TypeParameter>? TypeParams,
        TypeInfo.Function Func)
    {
        public bool IsGeneric => TypeParams is { Count: > 0 };
    }

    /// <summary>
    /// Reduces a function-like type to a <see cref="NormalizedSignature"/>, or null if it isn't a
    /// plain or generic function. Callable interfaces/object types are handled separately (via their
    /// call signatures) and are intentionally not folded in here.
    /// </summary>
    private static NormalizedSignature? NormalizeSignature(TypeInfo type) => type switch
    {
        TypeInfo.Function f => new NormalizedSignature(null, f),
        TypeInfo.GenericFunction gf => new NormalizedSignature(
            gf.TypeParams,
            new TypeInfo.Function(gf.ParamTypes, gf.ReturnType, gf.RequiredParams, gf.HasRestParam, gf.ThisType, gf.ParamNames)),
        _ => null,
    };

    /// <summary>
    /// True when <paramref name="source"/> is assignable to <paramref name="target"/> as a call
    /// signature. Applies contextual signature instantiation for a generic source against a
    /// non-generic target; otherwise relates the shapes with type parameters left opaque.
    /// </summary>
    private bool SignatureRelatedTo(NormalizedSignature source, NormalizedSignature target)
    {
        // Type-parameter identity is name-based in this checker, so a signature's bound parameter
        // must not capture a free type parameter of the same name on the other side (e.g. relating
        // `<T>() => T` to `() => T` inside `function foo<T>()` — those T's are different). Alpha-
        // rename each side's bound parameters to fresh, side-distinct names (apostrophes make them
        // unspellable in user code) before relating.
        source = AlphaRenameSignature(source, "'S");
        target = AlphaRenameSignature(target, "'T");

        if (source.IsGeneric)
        {
            // Contextual signature instantiation (tsc's instantiateSignatureInContextOf): infer the
            // source's type parameters from the target's parameter types, substitute, then relate
            // the instantiated shape. When the target is itself generic, its type parameters stay
            // rigid — they participate as opaque types the source's parameters are inferred to be,
            // and the source's constraints must hold for them. A constraint the inferred type can't
            // satisfy means the signatures don't relate.
            var instantiatedSource = InstantiateGenericSourceFromTarget(source, target.Func);
            if (instantiatedSource is null) return false;
            return RelateFunctionShapes(target.Func, instantiatedSource);
        }

        return RelateFunctionShapes(target.Func, source.Func);
    }

    /// <summary>
    /// Instantiates a generic source signature against a concrete target by inferring each source
    /// type parameter from the corresponding target type, then substituting. This is a dedicated
    /// inference path — deliberately separate from the call-argument inference (<c>InferFromType</c>)
    /// so changing it can't perturb generic call type-checking.
    /// </summary>
    /// <remarks>
    /// Inference is variance-aware. A type parameter is collected from every structural position it
    /// occupies (recursing arrays, functions, objects, tuples, generic instantiations), and the
    /// candidates are combined by the variance of those positions:
    /// <list type="bullet">
    /// <item>seen in any <em>contravariant</em> (parameter) position → intersection: the chosen type
    /// must serve as every input it's used as, so <c>&lt;T&gt;(x: T, y: T)</c> matched against
    /// <c>(x: {a}, y: {a;b})</c> yields <c>{a;b}</c>, not a first-wins <c>{a}</c> that then fails the
    /// second parameter;</item>
    /// <item>otherwise (purely covariant) → union.</item>
    /// </list>
    /// Un-inferred parameters default to their constraint, or <c>{}</c> when unconstrained — matching
    /// TypeScript, where an uninferable (typically return-only) type parameter collapses to <c>{}</c>.
    /// Returns null when an inferred type violates its parameter's (substituted) constraint — the
    /// signatures cannot relate under any instantiation.
    /// </remarks>
    private TypeInfo.Function? InstantiateGenericSourceFromTarget(NormalizedSignature source, TypeInfo.Function target)
    {
        var paramNames = source.TypeParams!.Select(tp => tp.Name).ToHashSet();
        var candidates = new Dictionary<string, List<TypeInfo>>();
        var sawContravariant = new HashSet<string>();
        var returnCandidates = new Dictionary<string, List<TypeInfo>>();
        var returnSawContravariant = new HashSet<string>();

        int positions = Math.Min(source.Func.ParamTypes.Count, target.ParamTypes.Count);
        for (int i = 0; i < positions; i++)
            CollectInferenceCandidates(source.Func.ParamTypes[i], target.ParamTypes[i], contravariant: true, paramNames, candidates, sawContravariant);
        // Return-position candidates are kept separate and only consulted when the parameter
        // positions yielded nothing — mirroring tsc's inference priorities, where a return-position
        // inference never overrides a parameter-position one (e.g. relating `<T>(x: T) => T` to
        // `(x: U) => void` must infer T := U, not T := U | void).
        CollectInferenceCandidates(source.Func.ReturnType, target.ReturnType, contravariant: false, paramNames, returnCandidates, returnSawContravariant);

        var inferred = new Dictionary<string, TypeInfo>();
        var inferredFromCandidates = new HashSet<string>();
        foreach (var tp in source.TypeParams!)
        {
            TypeInfo? combined = null;
            bool conflicted = false;
            if (candidates.TryGetValue(tp.Name, out var cands) && cands.Count > 0)
            {
                // Contravariant (parameter-position) candidates must combine to a type that serves
                // as every input it's used as: the common subtype among the candidates. Distinct
                // incomparable candidates (e.g. two different rigid type parameters, or string vs
                // number) have none — a CONFLICT, not a default: tsc leaves the type parameter
                // rigid in that case, so the relation fails against anything the parameter itself
                // wouldn't relate to. Purely covariant candidates union.
                combined = sawContravariant.Contains(tp.Name)
                    ? PickCommonSubtype(cands)
                    : cands.Aggregate(CreateUnion);
                conflicted = combined is null;
            }
            else if (returnCandidates.TryGetValue(tp.Name, out var retCands) && retCands.Count > 0)
            {
                combined = returnSawContravariant.Contains(tp.Name)
                    ? PickCommonSubtype(retCands)
                    : retCands.Aggregate(CreateUnion);
                conflicted = combined is null;
            }
            if (combined is not null)
            {
                inferred[tp.Name] = combined;
                inferredFromCandidates.Add(tp.Name);
            }
            else if (!conflicted)
            {
                // No inference site at all (typically a return-only parameter): default to the
                // constraint, or {} when unconstrained — matching tsc.
                inferred[tp.Name] = tp.Constraint ?? EmptyObjectType;
            }
            // Conflicted parameters get no substitution: they stay rigid in the relating below.
        }

        // Inferred types must satisfy their parameter's constraint (with the inference substituted
        // into it, since constraints may reference sibling parameters). A violating inference is
        // CLAMPED to the constraint — tsc's rule — rather than failing the relation: the shape
        // comparison below then decides (e.g. `<T extends Derived>(...x: T[]) => T` assigned to
        // `(...x: Base[]) => Base` clamps T to Derived and relates fine). Defaulted parameters are
        // skipped — a constraint default satisfies itself, and {} has no constraint to violate.
        foreach (var tp in source.TypeParams!)
        {
            if (tp.Constraint is null || !inferredFromCandidates.Contains(tp.Name)) continue;
            var constraint = Substitute(tp.Constraint, inferred);
            if (!IsCompatible(constraint, inferred[tp.Name]))
                inferred[tp.Name] = constraint;
        }

        var paramTypes = source.Func.ParamTypes.Select(p => Substitute(p, inferred)).ToList();
        var returnType = Substitute(source.Func.ReturnType, inferred);
        return new TypeInfo.Function(
            paramTypes, returnType, source.Func.RequiredParams, source.Func.HasRestParam,
            source.Func.ThisType, source.Func.ParamNames);
    }

    /// <summary>
    /// The candidate assignable to every other candidate (the most specific one), or null when the
    /// candidates are pairwise incomparable — an inference failure.
    /// </summary>
    private TypeInfo? PickCommonSubtype(List<TypeInfo> candidates)
    {
        var distinct = candidates.Distinct().ToList();
        if (distinct.Count == 1) return distinct[0];
        foreach (var c in distinct)
        {
            if (distinct.All(other => ReferenceEquals(other, c) || Equals(other, c) || IsCompatible(other, c)))
                return c;
        }
        return null;
    }

    /// <summary>
    /// Recursively records, for each named type parameter of the source, the target types appearing
    /// at the same structural position — tracking whether the position is contravariant so candidates
    /// can later be combined correctly. Mirrors function-parameter contravariance (recursing into a
    /// nested function flips the variance of its parameters).
    /// </summary>
    private void CollectInferenceCandidates(
        TypeInfo sourceType, TypeInfo targetType, bool contravariant,
        HashSet<string> paramNames, Dictionary<string, List<TypeInfo>> candidates, HashSet<string> sawContravariant)
    {
        switch (sourceType)
        {
            case TypeInfo.TypeParameter tp when paramNames.Contains(tp.Name):
                if (!candidates.TryGetValue(tp.Name, out var list))
                    candidates[tp.Name] = list = [];
                list.Add(targetType);
                if (contravariant) sawContravariant.Add(tp.Name);
                return;

            case TypeInfo.Array sa when targetType is TypeInfo.Array ta:
                CollectInferenceCandidates(sa.ElementType, ta.ElementType, contravariant, paramNames, candidates, sawContravariant);
                return;

            case TypeInfo.Function sf when targetType is TypeInfo.Function tf:
                int n = Math.Min(sf.ParamTypes.Count, tf.ParamTypes.Count);
                for (int i = 0; i < n; i++)
                    // Parameter positions of a nested function flip the variance.
                    CollectInferenceCandidates(sf.ParamTypes[i], tf.ParamTypes[i], !contravariant, paramNames, candidates, sawContravariant);
                CollectInferenceCandidates(sf.ReturnType, tf.ReturnType, contravariant, paramNames, candidates, sawContravariant);
                return;

            case TypeInfo.Record sr when targetType is TypeInfo.Record tr:
                foreach (var (fieldName, fieldType) in sr.Fields)
                    if (tr.Fields.TryGetValue(fieldName, out var targetFieldType))
                        CollectInferenceCandidates(fieldType, targetFieldType, contravariant, paramNames, candidates, sawContravariant);
                return;

            case TypeInfo.Tuple stup when targetType is TypeInfo.Tuple ttup:
                int m = Math.Min(stup.Elements.Count, ttup.Elements.Count);
                for (int i = 0; i < m; i++)
                    CollectInferenceCandidates(stup.Elements[i].Type, ttup.Elements[i].Type, contravariant, paramNames, candidates, sawContravariant);
                return;

            case TypeInfo.InstantiatedGeneric sg when targetType is TypeInfo.InstantiatedGeneric tg:
                int k = Math.Min(sg.TypeArguments.Count, tg.TypeArguments.Count);
                for (int i = 0; i < k; i++)
                    CollectInferenceCandidates(sg.TypeArguments[i], tg.TypeArguments[i], contravariant, paramNames, candidates, sawContravariant);
                return;
        }
    }

    /// <summary>The empty object type <c>{}</c> — the default for an uninferable type parameter.</summary>
    private static readonly TypeInfo.Record EmptyObjectType = new(FrozenDictionary<string, TypeInfo>.Empty);

    /// <summary>
    /// Renames a generic signature's type parameters (and every occurrence of them in its
    /// constraints, parameters, and return type) to <c>{prefix}0</c>, <c>{prefix}1</c>, …
    /// Deterministic per side, so repeated comparisons of the same types produce identical
    /// renderings (keeping the compatibility cache effective).
    /// </summary>
    private NormalizedSignature AlphaRenameSignature(NormalizedSignature sig, string prefix)
    {
        if (!sig.IsGeneric) return sig;
        int count = sig.TypeParams!.Count;

        // Pass 0: bare fresh names so constraints can reference siblings. Then re-resolve the
        // constraints `count` times, rebuilding the map each pass so every occurrence of a renamed
        // parameter — including inside sibling constraints — carries its own resolved constraint
        // (mirrors BuildGenericTypeParameters' chain-deepening passes; the occurrences must keep
        // their constraints or ApparentTypeOf sees an unconstrained parameter).
        var map = new Dictionary<string, TypeInfo>();
        for (int i = 0; i < count; i++)
            map[sig.TypeParams[i].Name] = new TypeInfo.TypeParameter($"{prefix}{i}");

        // count+2 passes: `count` to link sibling chains (mirrors BuildGenericTypeParameters) plus
        // two extra levels so SELF-referential constraints (T extends I2<T>) carry enough nested
        // constraint depth for the apparent-type walks the relating performs.
        List<TypeInfo.TypeParameter> renamed = [];
        for (int pass = 0; pass < count + 2; pass++)
        {
            renamed = [];
            for (int i = 0; i < count; i++)
            {
                var tp = sig.TypeParams[i];
                renamed.Add(new TypeInfo.TypeParameter(
                    $"{prefix}{i}",
                    tp.Constraint is null ? null : Substitute(tp.Constraint, map),
                    tp.Default is null ? null : Substitute(tp.Default, map),
                    tp.IsConst, tp.Variance));
            }
            for (int i = 0; i < count; i++)
                map[sig.TypeParams[i].Name] = renamed[i];
        }

        var f = sig.Func;
        return new NormalizedSignature(renamed, new TypeInfo.Function(
            f.ParamTypes.Select(p => Substitute(p, map)).ToList(),
            Substitute(f.ReturnType, map),
            f.RequiredParams, f.HasRestParam,
            f.ThisType is null ? null : Substitute(f.ThisType, map),
            f.ParamNames));
    }

    /// <summary>
    /// Erases a signature's type parameters to <c>any</c>. tsc relates signatures with full
    /// contextual instantiation only in the single-signature-vs-single-signature case; when either
    /// side is overloaded, each pairing is related with erased type parameters instead
    /// (checker.ts <c>signaturesRelatedTo</c>).
    /// </summary>
    private NormalizedSignature EraseSignature(NormalizedSignature sig)
    {
        if (!sig.IsGeneric) return sig;
        var anyMap = new Dictionary<string, TypeInfo>();
        foreach (var tp in sig.TypeParams!) anyMap[tp.Name] = TypeInfo.Any.Shared;
        var f = sig.Func;
        return new NormalizedSignature(null, new TypeInfo.Function(
            f.ParamTypes.Select(p => Substitute(p, anyMap)).ToList(),
            Substitute(f.ReturnType, anyMap),
            f.RequiredParams, f.HasRestParam, f.ThisType, f.ParamNames));
    }

    /// <summary>
    /// Core function-shape assignability: <paramref name="f2"/> (source) assignable to
    /// <paramref name="f1"/> (target). Parameter arity/positions with rest-parameter expansion, then
    /// return-type covariance with the void-return special case. Extracted verbatim from the former
    /// inline <c>Function</c>-vs-<c>Function</c> block so non-generic behavior is unchanged.
    /// </summary>
    private bool RelateFunctionShapes(TypeInfo.Function f1, TypeInfo.Function f2)
    {
        // Source (f2) must not require more parameters than the target (f1) can supply.
        // A rest parameter on the target lets it supply unboundedly many, so the count check
        // only applies when the target has no rest parameter.
        if (!f1.HasRestParam && f2.MinArity > f1.ParamTypes.Count) return false;

        // Both context flags apply to THIS shape relation only (tsc computes them per
        // compareSignaturesRelated invocation): capture and clear so nested comparisons —
        // parameter and return types — relate under their own rules.
        bool inCallback = _inCallbackComparison;
        _inCallbackComparison = false;
        bool isMethodTarget = _methodBivarianceDepth > 0;
        int savedMethodDepth = _methodBivarianceDepth;
        _methodBivarianceDepth = 0;
        try
        {
            // Compare parameter positions, expanding a rest parameter to its element type so it
            // covers the other side's fixed parameters (e.g. `(...a: number[])` matches `(a, b)`).
            // Parameter relation: bivariant by default (tsc pre-strictFunctionTypes), strict
            // contravariance under strictFunctionTypes — except for method-member targets (tsc's
            // method exemption) — and contravariance-only inside a callback comparison.
            bool bivariantParams = !inCallback && (!_strictFunctionTypes || isMethodTarget);

            int f1Fixed = f1.HasRestParam ? f1.ParamTypes.Count - 1 : f1.ParamTypes.Count;
            int f2Fixed = f2.HasRestParam ? f2.ParamTypes.Count - 1 : f2.ParamTypes.Count;
            int positions = Math.Max(f1Fixed, f2Fixed);
            for (int i = 0; i < positions; i++)
            {
                var fp1 = EffectiveParamType(f1, i);
                var fp2 = EffectiveParamType(f2, i);
                // A position absent on one side (and not covered by a rest param) is unconstrained.
                if (fp1 is null || fp2 is null) continue;

                // Callback rule: when both parameters are pure function types, relate them
                // SWAPPED (target's callback as the source) with strict parameters inside —
                // callbacks are output positions, so they relate covariantly even where plain
                // parameters would be bivariant (tsc's Promise rule, checker.ts
                // compareSignaturesRelated). Suppressed when either side's position was a NAKED
                // type parameter before instantiation (tsc's isInstantiatedGenericParameter,
                // #51620): `set(value: T)` instantiated with a function type stays bivariant —
                // the function-ness came from the argument, not the declaration.
                if (!inCallback &&
                    fp1 is TypeInfo.Function targetCb && fp2 is TypeInfo.Function sourceCb &&
                    !f1.IsInstantiatedTypeParamPosition(i) && !f2.IsInstantiatedTypeParamPosition(i))
                {
                    bool related;
                    _inCallbackComparison = true;
                    try { related = RelateFunctionShapes(sourceCb, targetCb); }
                    finally { _inCallbackComparison = false; }
                    if (!related) return false;
                    continue;
                }

                // Contravariance: the target's parameter must be acceptable to the source.
                if (!IsCompatible(fp2, fp1) && (!bivariantParams || !IsCompatible(fp1, fp2))) return false;
            }
            // When both have rest parameters, their element types must also be compatible.
            if (f1.HasRestParam && f2.HasRestParam)
            {
                var e1 = EffectiveParamType(f1, f1.ParamTypes.Count);
                var e2 = EffectiveParamType(f2, f2.ParamTypes.Count);
                if (e1 is not null && e2 is not null &&
                    !IsCompatible(e2, e1) && (!bivariantParams || !IsCompatible(e1, e2))) return false;
            }
            // Return type: if expected return type is void, any return type is acceptable
            // This is standard TypeScript behavior - void context ignores the return value
            if (f1.ReturnType is TypeInfo.Void) return true;
            // Inside a (bivariant) callback comparison, return types relate bivariantly — tsc:
            // "otherwise the containing type wouldn't be co-variant. For example,
            // interface Foo<T> { add(cb: () => T): void } wouldn't be co-variant for T".
            if (inCallback && IsCompatible(f2.ReturnType, f1.ReturnType)) return true;
            // Otherwise, actual must be compatible with expected
            return IsCompatible(f1.ReturnType, f2.ReturnType);
        }
        finally
        {
            _methodBivarianceDepth = savedMethodDepth;
            _inCallbackComparison = inCallback;
        }
    }
}

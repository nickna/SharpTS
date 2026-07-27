using SharpTS.TypeSystem.Exceptions;
using SharpTS.Runtime.BuiltIns;
using System.Collections.Frozen;

namespace SharpTS.TypeSystem;

/// <summary>
/// Conditional type evaluation, distribution, and extends checking with infer patterns.
/// </summary>
/// <remarks>
/// Contains methods: EvaluateConditionalType, SubstituteWithoutConditionalEval,
/// DistributeConditionalOverUnion,
/// CheckExtendsWithInfer, CheckExtendsRecursive, IsSameGenericDefinition,
/// EvaluateIntrinsicStringType, ApplyStringManipulation, MatchTemplateLiteralWithInfer,
/// MatchStringLiteralToTemplatePattern.
/// </remarks>
public partial class TypeChecker
{
    /// <summary>
    /// Maximum recursion depth for conditional type evaluation.
    /// Prevents infinite loops in recursive type definitions.
    /// </summary>
    private const int MaxConditionalTypeDepth = 50;

    /// <summary>
    /// Tracks current recursion depth during conditional type evaluation.
    /// Thread-local to handle concurrent type checking safely.
    /// </summary>
    [ThreadStatic]
    private static int _conditionalTypeDepth;

    /// <summary>
    /// Evaluates a conditional type, handling distribution over unions and infer patterns.
    /// </summary>
    /// <param name="conditional">The conditional type to evaluate</param>
    /// <param name="substitutions">Current type parameter substitutions</param>
    /// <returns>The resolved type after conditional evaluation</returns>
    public TypeInfo EvaluateConditionalType(
        TypeInfo.ConditionalType conditional,
        Dictionary<string, TypeInfo>? substitutions = null)
    {
        substitutions ??= [];

        // Check recursion depth
        if (_conditionalTypeDepth >= MaxConditionalTypeDepth)
        {
            // SharpTS-only: implementation depth limit (TS uses TS2589 "Type instantiation is excessively deep" for similar)
            throw new TypeCheckException(
                $"Conditional type recursion depth exceeded {MaxConditionalTypeDepth}. " +
                "This may indicate an infinitely recursive type definition.");
        }

        _conditionalTypeDepth++;
        try
        {
            // Apply current substitutions to the check type
            TypeInfo checkType = SubstituteWithoutConditionalEval(conditional.CheckType, substitutions);

            // A check type that is an indexed access over a CONCRETE object (e.g. `Part["updatePart"]`
            // in the key-filter idiom) must be resolved to the member type before the extends-test,
            // or the structural match against `Function` fails and the filter picks the wrong branch
            // (#337 item 2). Generic accesses (`T[K]`) stay unresolved so the deferral below fires.
            if (checkType is TypeInfo.IndexedAccess checkIndexed && !IsGenericTypeShape(checkIndexed))
            {
                var resolvedCheck = ResolveIndexedAccess(checkIndexed, substitutions);
                if (resolvedCheck is not (TypeInfo.IndexedAccess or TypeInfo.Any))
                    checkType = resolvedCheck;
            }

            // Distribution happens only for DISTRIBUTIVE conditionals (declared with a naked
            // type-parameter check). A literal `string | number extends string ? A : B` does not
            // distribute in tsc — the union is checked as a whole.
            if (conditional.IsDistributive && checkType is TypeInfo.Union union)
            {
                return DistributeConditionalOverUnion(conditional, union, substitutions);
            }

            // A check type that still contains type variables (naked parameter, parameter inside
            // an array/function/intersection, a nested deferred conditional, ...) can't be decided
            // yet — defer, preserving the declaration's distributivity (tsc isGenericType deferral).
            if (IsGenericTypeShape(checkType))
            {
                return new TypeInfo.ConditionalType(
                    checkType,
                    SubstituteWithoutConditionalEval(conditional.ExtendsType, substitutions),
                    SubstituteWithoutConditionalEval(conditional.TrueType, substitutions),
                    SubstituteWithoutConditionalEval(conditional.FalseType, substitutions)
                ) { IsDistributive = conditional.IsDistributive };
            }

            // Apply substitutions to the extends type
            TypeInfo extendsType = SubstituteWithoutConditionalEval(conditional.ExtendsType, substitutions);

            // tsc: `any extends X ? A : B` yields A | B — an `any` check type matches BOTH
            // branches, and every infer placeholder in the extends clause resolves to `any`. So
            // `any extends Foo<infer U> ? U : never` is `any | never` = `any`. Seed every true-branch
            // infer reference with `any` first (the structural match against `any` cannot bind them,
            // and the extends clause may even have collapsed to `any` upstream), then overlay any
            // more-specific bindings the wildcard match did produce.
            if (checkType is TypeInfo.Any)
            {
                var (_, anyInferred) = CheckExtendsWithInfer(checkType, extendsType);
                var trueSubs = new Dictionary<string, TypeInfo>(substitutions);
                CollectReferencedInferNames(conditional.TrueType, name => trueSubs[name] = TypeInfo.Any.Shared);
                foreach (var (name, type) in anyInferred)
                    trueSubs[name] = type;
                return CreateUnion(
                    Substitute(conditional.TrueType, trueSubs),
                    Substitute(conditional.FalseType, substitutions));
            }

            // Perform the extends check with infer pattern matching
            var (matches, inferredTypes) = CheckExtendsWithInfer(checkType, extendsType);

            // `infer U extends C`: the final inferred type (after all candidate sites unioned)
            // must satisfy the constraint, or the conditional resolves to its false branch.
            // Checked after matching, not per binding site, so `{ a: infer U extends string,
            // b: infer U }` applies the constraint to the merged inference (tsc semantics).
            if (matches)
                matches = InferConstraintsSatisfied(extendsType, inferredTypes);

            // Merge inferred types into substitutions
            var newSubstitutions = new Dictionary<string, TypeInfo>(substitutions);
            foreach (var (name, type) in inferredTypes)
            {
                newSubstitutions[name] = type;
            }

            // Evaluate true or false branch based on extends check result
            TypeInfo resultType = matches
                ? Substitute(conditional.TrueType, newSubstitutions)
                : Substitute(conditional.FalseType, substitutions);

            return resultType;
        }
        finally
        {
            _conditionalTypeDepth--;
        }
    }

    /// <summary>
    /// Substitutes type parameters without triggering conditional type evaluation (to avoid infinite recursion).
    /// Thin wrapper over the shared <see cref="Substitute(TypeInfo, Dictionary{string, TypeInfo}, bool)"/> switch
    /// with <c>evalConditionals: false</c> — see that method for the per-variant behaviour and why the two
    /// substitution paths must share one switch (#1106).
    /// </summary>
    private TypeInfo SubstituteWithoutConditionalEval(TypeInfo type, Dictionary<string, TypeInfo> substitutions)
        => Substitute(type, substitutions, evalConditionals: false);

    /// <summary>
    /// Distributes a conditional type over a union type.
    /// (A | B) extends U ? X : Y becomes (A extends U ? X : Y) | (B extends U ? X : Y)
    /// </summary>
    private TypeInfo DistributeConditionalOverUnion(
        TypeInfo.ConditionalType conditional,
        TypeInfo.Union union,
        Dictionary<string, TypeInfo> substitutions)
    {
        List<TypeInfo> resultTypes = [];

        foreach (var memberType in union.FlattenedTypes)
        {
            // Create substitutions with the union member replacing the original type parameter
            var memberSubs = new Dictionary<string, TypeInfo>(substitutions);
            if (conditional.CheckType is TypeInfo.TypeParameter tp)
            {
                memberSubs[tp.Name] = memberType;
            }

            // Create a new conditional with this union member as the check type
            var distributed = new TypeInfo.ConditionalType(
                memberType,
                conditional.ExtendsType,
                conditional.TrueType,
                conditional.FalseType
            );

            // Evaluate the distributed conditional
            var result = EvaluateConditionalType(distributed, memberSubs);

            // Skip 'never' results (they disappear from unions)
            if (result is not TypeInfo.Never)
            {
                resultTypes.Add(result);
            }
        }

        // Build result union
        if (resultTypes.Count == 0)
            return TypeInfo.Never.Shared;
        if (resultTypes.Count == 1)
            return resultTypes[0];

        // Deduplicate and flatten
        var flattenedTypes = resultTypes
            .SelectMany(t => t is TypeInfo.Union u ? u.FlattenedTypes : [t])
            .Distinct(TypeInfoEqualityComparer.Instance)
            .ToList();

        if (flattenedTypes.Count == 1)
            return flattenedTypes[0];

        return new TypeInfo.Union(flattenedTypes);
    }

    /// <summary>
    /// Checks if a type extends another type, with support for infer pattern matching.
    /// Returns (matches, inferredTypes) where inferredTypes contains bindings for infer parameters.
    /// </summary>
    private (bool Matches, Dictionary<string, TypeInfo> InferredTypes) CheckExtendsWithInfer(
        TypeInfo checkType,
        TypeInfo extendsType)
    {
        var inferredTypes = new Dictionary<string, TypeInfo>();
        bool matches = CheckExtendsRecursive(checkType, extendsType, inferredTypes);
        return (matches, inferredTypes);
    }

    /// <summary>
    /// Verifies every constrained <c>infer</c> declaration in an extends clause against its final
    /// inferred binding. Unbound infers are not failures (the structural match decides those).
    /// </summary>
    private bool InferConstraintsSatisfied(TypeInfo extendsType, Dictionary<string, TypeInfo> inferredTypes)
    {
        List<TypeInfo.InferredTypeParameter>? constrained = null;
        CollectConstrainedInfers(extendsType, ref constrained);
        if (constrained is null) return true;
        foreach (var infer in constrained)
        {
            if (inferredTypes.TryGetValue(infer.Name, out var bound) && !IsCompatible(infer.Constraint!, bound))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Walks the extends-clause type for constrained infer declarations. Mirrors the shapes
    /// <see cref="CheckExtendsRecursive"/> descends into; nested conditional types are their own
    /// inference scope, so descent stops there (their constraints are checked when they evaluate).
    /// </summary>
    private static void CollectConstrainedInfers(TypeInfo type, ref List<TypeInfo.InferredTypeParameter>? result)
    {
        switch (type)
        {
            case TypeInfo.InferredTypeParameter { Constraint: not null } infer:
                (result ??= []).Add(infer);
                break;
            case TypeInfo.Array arr:
                CollectConstrainedInfers(arr.ElementType, ref result);
                break;
            case TypeInfo.Promise promise:
                CollectConstrainedInfers(promise.ValueType, ref result);
                break;
            case TypeInfo.Function func:
                foreach (var p in func.ParamTypes) CollectConstrainedInfers(p, ref result);
                CollectConstrainedInfers(func.ReturnType, ref result);
                break;
            case TypeInfo.Tuple tuple:
                foreach (var elem in tuple.Elements) CollectConstrainedInfers(elem.Type, ref result);
                if (tuple.RestElementType is { } rest) CollectConstrainedInfers(rest, ref result);
                break;
            case TypeInfo.Union union:
                foreach (var t in union.Types) CollectConstrainedInfers(t, ref result);
                break;
            case TypeInfo.Intersection intersection:
                foreach (var t in intersection.Types) CollectConstrainedInfers(t, ref result);
                break;
            case TypeInfo.Record rec:
                foreach (var (_, t) in rec.Fields) CollectConstrainedInfers(t, ref result);
                break;
            case TypeInfo.Interface itf:
                foreach (var (_, t) in itf.Members) CollectConstrainedInfers(t, ref result);
                break;
            case TypeInfo.InstantiatedGeneric ig:
                foreach (var t in ig.TypeArguments) CollectConstrainedInfers(t, ref result);
                break;
            case TypeInfo.Instance inst:
                CollectConstrainedInfers(inst.ResolvedClassType, ref result);
                break;
            case TypeInfo.KeyOf keyOf:
                CollectConstrainedInfers(keyOf.SourceType, ref result);
                break;
            case TypeInfo.IndexedAccess ia:
                CollectConstrainedInfers(ia.ObjectType, ref result);
                CollectConstrainedInfers(ia.IndexType, ref result);
                break;
            case TypeInfo.TemplateLiteralType tl:
                foreach (var t in tl.InterpolatedTypes) CollectConstrainedInfers(t, ref result);
                break;
            // Built-in containers (Map<…, infer V extends C>, …): descend into their arguments (#347).
            default:
                if (DecomposeBuiltInContainer(type) is { } containerArgs)
                    foreach (var t in containerArgs) CollectConstrainedInfers(t, ref result);
                break;
        }
    }

    /// <summary>
    /// Invokes <paramref name="visit"/> with the name of every <see cref="TypeInfo.InferredTypeParameter"/>
    /// referenced within <paramref name="type"/>. Used by the <c>any extends …</c> branch to resolve
    /// the true branch's infer placeholders to <c>any</c>.
    /// </summary>
    private static void CollectReferencedInferNames(TypeInfo type, Action<string> visit)
    {
        switch (type)
        {
            case TypeInfo.InferredTypeParameter infer:
                visit(infer.Name);
                if (infer.Constraint is { } c) CollectReferencedInferNames(c, visit);
                break;
            case TypeInfo.Array arr:
                CollectReferencedInferNames(arr.ElementType, visit);
                break;
            case TypeInfo.Promise promise:
                CollectReferencedInferNames(promise.ValueType, visit);
                break;
            case TypeInfo.Function func:
                foreach (var p in func.ParamTypes) CollectReferencedInferNames(p, visit);
                CollectReferencedInferNames(func.ReturnType, visit);
                break;
            case TypeInfo.Tuple tuple:
                foreach (var elem in tuple.Elements) CollectReferencedInferNames(elem.Type, visit);
                if (tuple.RestElementType is { } rest) CollectReferencedInferNames(rest, visit);
                break;
            case TypeInfo.Union union:
                foreach (var t in union.Types) CollectReferencedInferNames(t, visit);
                break;
            case TypeInfo.Intersection intersection:
                foreach (var t in intersection.Types) CollectReferencedInferNames(t, visit);
                break;
            case TypeInfo.Record rec:
                foreach (var (_, t) in rec.Fields) CollectReferencedInferNames(t, visit);
                break;
            case TypeInfo.InstantiatedGeneric ig:
                foreach (var t in ig.TypeArguments) CollectReferencedInferNames(t, visit);
                break;
            case TypeInfo.Instance inst:
                CollectReferencedInferNames(inst.ResolvedClassType, visit);
                break;
            case TypeInfo.KeyOf keyOf:
                CollectReferencedInferNames(keyOf.SourceType, visit);
                break;
            case TypeInfo.IndexedAccess ia:
                CollectReferencedInferNames(ia.ObjectType, visit);
                CollectReferencedInferNames(ia.IndexType, visit);
                break;
            case TypeInfo.TemplateLiteralType tl:
                foreach (var t in tl.InterpolatedTypes) CollectReferencedInferNames(t, visit);
                break;
            // Built-in containers (Map<…, infer V>, Set<infer T>, …): descend into their arguments (#347).
            default:
                if (DecomposeBuiltInContainer(type) is { } containerArgs)
                    foreach (var t in containerArgs) CollectReferencedInferNames(t, visit);
                break;
        }
    }

    /// <summary>
    /// Recursively checks the extends relationship, extracting infer bindings.
    /// </summary>
    private bool CheckExtendsRecursive(
        TypeInfo checkType,
        TypeInfo extendsType,
        Dictionary<string, TypeInfo> inferredTypes)
    {
        // Handle infer pattern: T extends infer U - bind U to the check type
        if (extendsType is TypeInfo.InferredTypeParameter inferParam)
        {
            if (inferredTypes.TryGetValue(inferParam.Name, out var existing))
            {
                // Multiple declarations of the same infer name accumulate a union (tsc unions
                // covariant inference candidates: `{ a: infer U, b: infer U }` infers a | b).
                // Constraint satisfaction is checked against the final union after the match.
                inferredTypes[inferParam.Name] = CreateUnion(existing, checkType);
                return true;
            }
            inferredTypes[inferParam.Name] = checkType;
            return true;
        }

        // Array<infer U> matching
        if (extendsType is TypeInfo.Array extendsArr)
        {
            if (checkType is TypeInfo.Array checkArr)
            {
                return CheckExtendsRecursive(checkArr.ElementType, extendsArr.ElementType, inferredTypes);
            }
            if (checkType is TypeInfo.Tuple checkTuple)
            {
                // Tuple extends Array if all elements extend the array element type
                if (extendsArr.ElementType is TypeInfo.InferredTypeParameter tupleInfer)
                {
                    // Infer the union of all tuple element types
                    TypeInfo unionOfElements = checkTuple.ElementTypes.Count switch
                    {
                        0 => TypeInfo.Never.Shared,
                        1 => checkTuple.ElementTypes[0],
                        _ => new TypeInfo.Union(checkTuple.ElementTypes)
                    };
                    inferredTypes[tupleInfer.Name] = unionOfElements;
                    return true;
                }
                return checkTuple.ElementTypes.All(e =>
                    CheckExtendsRecursive(e, extendsArr.ElementType, inferredTypes));
            }
            return false;
        }

        // Promise<infer T> matching
        if (extendsType is TypeInfo.Promise extendsPromise)
        {
            if (checkType is TypeInfo.Promise checkPromise)
            {
                return CheckExtendsRecursive(checkPromise.ValueType, extendsPromise.ValueType, inferredTypes);
            }
            return false;
        }

        // Template literal pattern with infer
        if (extendsType is TypeInfo.TemplateLiteralType templatePattern)
        {
            return MatchTemplateLiteralWithInfer(checkType, templatePattern, inferredTypes);
        }

        // Function type matching with infer for return/param types
        if (extendsType is TypeInfo.Function extendsFunc)
        {
            if (checkType is TypeInfo.Function checkFunc)
            {
                // Without infer placeholders to bind, `check extends F` is the ordinary
                // assignability question — `(s: string) => void` IS a `Function` (modelled as
                // `(...args: any[]) => any`). Strict signature unification wrongly rejects it,
                // which broke the `T[K] extends Function` key-filter (#337 item 2). Reserve
                // MatchSignatureWithInfer for targets that actually carry infers.
                bool hasInfer = false;
                CollectReferencedInferNames(extendsFunc, _ => hasInfer = true);
                return hasInfer
                    ? MatchSignatureWithInfer(checkFunc, extendsFunc, inferredTypes)
                    : IsCompatible(extendsType, checkType);
            }
            return false;
        }

        // Tuple matching
        if (extendsType is TypeInfo.Tuple extendsTuple)
        {
            if (checkType is TypeInfo.Tuple checkTuple)
            {
                // Check tuple has at least as many elements
                if (checkTuple.ElementTypes.Count < extendsTuple.RequiredCount)
                    return false;

                // Match element types
                for (int i = 0; i < extendsTuple.ElementTypes.Count && i < checkTuple.ElementTypes.Count; i++)
                {
                    if (!CheckExtendsRecursive(checkTuple.ElementTypes[i], extendsTuple.ElementTypes[i], inferredTypes))
                        return false;
                }
                return true;
            }
            return false;
        }

        // InstantiatedGeneric matching (e.g., Box<infer T>, user-defined generic class/interface).
        // A generic CLASS instance arrives wrapped in TypeInfo.Instance (Box<number> =>
        // Instance(InstantiatedGeneric)); unwrap both sides so the type-argument match reaches the
        // infer placeholders instead of falling through to a structural IsCompatible that cannot
        // bind them (#347). UnwrapToInstantiatedGeneric is a no-op for an already-bare generic.
        if (UnwrapToInstantiatedGeneric(extendsType) is { } extendsGeneric)
        {
            if (UnwrapToInstantiatedGeneric(checkType) is { } checkGeneric)
            {
                // Must be same generic base
                if (!IsSameGenericDefinition(checkGeneric.GenericDefinition, extendsGeneric.GenericDefinition))
                    return false;

                // Match type arguments
                if (checkGeneric.TypeArguments.Count != extendsGeneric.TypeArguments.Count)
                    return false;

                for (int i = 0; i < extendsGeneric.TypeArguments.Count; i++)
                {
                    if (!CheckExtendsRecursive(checkGeneric.TypeArguments[i], extendsGeneric.TypeArguments[i], inferredTypes))
                        return false;
                }
                return true;
            }
            return false;
        }

        // Built-in container matching (Map<…, infer V>, Set<infer T>, the weak variants, iterators,
        // generators). These carry bespoke TypeInfo records rather than InstantiatedGeneric, so
        // without this they fall through to a structural IsCompatible that cannot bind infers (#347).
        // Same record kind (same runtime type) and arity → recurse over type arguments pairwise.
        if (DecomposeBuiltInContainer(extendsType) is { } extendsArgs)
        {
            if (checkType.GetType() == extendsType.GetType()
                && DecomposeBuiltInContainer(checkType) is { } checkArgs
                && checkArgs.Count == extendsArgs.Count)
            {
                for (int i = 0; i < extendsArgs.Count; i++)
                {
                    if (!CheckExtendsRecursive(checkArgs[i], extendsArgs[i], inferredTypes))
                        return false;
                }
                return true;
            }
            return false;
        }

        // Record/object type matching
        if (extendsType is TypeInfo.Record extendsRec)
        {
            // `new (...) => infer U` / `(...) => infer U` model as object types carrying construct or
            // call signatures (see TypeChecker.TypeNodes.cs). Match those structurally so infer
            // placeholders in parameter/return positions bind — the field loop alone vacuously
            // succeeds and leaves U dangling (#316).
            if (extendsRec.ConstructorSignatures is { Count: > 0 } recCtorSigs
                && !MatchSignaturesWithInfer(GetCheckConstructorSignatures(checkType),
                    recCtorSigs.Select(ConstructorSignatureToFunction).ToList(), inferredTypes))
                return false;
            if (extendsRec.CallSignatures is { Count: > 0 } recCallSigs
                && !MatchSignaturesWithInfer(GetCheckCallSignatures(checkType),
                    recCallSigs.Select(CallSignatureToFunction).ToList(), inferredTypes))
                return false;

            var checkProps = ExtractInferMatchProperties(checkType);
            foreach (var (key, extendsFieldType) in extendsRec.Fields)
            {
                if (!checkProps.TryGetValue(key, out var checkFieldType))
                    checkFieldType = ResolveBuiltInRecordMember(checkType, key);
                if (checkFieldType is null
                    || !CheckExtendsRecursive(checkFieldType, extendsFieldType, inferredTypes))
                    return false;
            }
            return true;
        }

        // Interface matching
        if (extendsType is TypeInfo.Interface extendsItf)
        {
            if (extendsItf.ConstructorSignatures is { Count: > 0 } itfCtorSigs
                && !MatchSignaturesWithInfer(GetCheckConstructorSignatures(checkType),
                    itfCtorSigs.Select(ConstructorSignatureToFunction).ToList(), inferredTypes))
                return false;
            if (extendsItf.CallSignatures is { Count: > 0 } itfCallSigs
                && !MatchSignaturesWithInfer(GetCheckCallSignatures(checkType),
                    itfCallSigs.Select(CallSignatureToFunction).ToList(), inferredTypes))
                return false;

            var checkProps = ExtractInferMatchProperties(checkType);
            foreach (var (key, extendsFieldType) in extendsItf.Members)
            {
                if (extendsItf.OptionalMembers.Contains(key))
                    continue; // Optional members don't need to exist

                if (!checkProps.TryGetValue(key, out var checkFieldType))
                    checkFieldType = ResolveBuiltInRecordMember(checkType, key);
                if (checkFieldType is null
                    || !CheckExtendsRecursive(checkFieldType, extendsFieldType, inferredTypes))
                    return false;
            }
            return true;
        }

        // Union on extends side: check type must extend ALL members (intersection semantics)
        if (extendsType is TypeInfo.Union extendsUnion)
        {
            // For conditional types, extends union is satisfied if check extends any member
            return extendsUnion.FlattenedTypes.Any(t => CheckExtendsRecursive(checkType, t, inferredTypes));
        }

        // Fall back to standard compatibility check (no infer patterns)
        return IsCompatible(extendsType, checkType);
    }

    /// <summary>
    /// Resolves the type of a single instance member on a dedicated built-in type record
    /// (Date/RegExp/Map/Set/Promise and the weak/iterator variants) for conditional-type infer
    /// matching — e.g. so <c>T extends { toJSON(): infer R }</c> can see <c>Date.toJSON: () =&gt; string</c>
    /// (#491). Delegates to <see cref="BuiltInTypes.GetInstanceMemberType"/>, the same apparent-members
    /// model that <c>value.member</c> reads, keyof, and structural assignability use, so they agree on
    /// one source of truth (#512). Returns null when <paramref name="checkType"/> is not such a record
    /// or the member is absent — the infer match then fails (false branch), exactly as before.
    /// </summary>
    private static TypeInfo? ResolveBuiltInRecordMember(TypeInfo checkType, string memberName)
        => BuiltInTypes.GetInstanceMemberType(checkType, memberName);

    /// <summary>
    /// True when the type still contains type variables (type parameters, infer placeholders,
    /// deferred conditionals) anywhere instantiation could reach — tsc's isGenericType, used to
    /// decide whether a conditional's check type can be decided now or must defer.
    /// </summary>
    private static bool IsGenericTypeShape(TypeInfo type)
    {
        var visiting = new HashSet<TypeInfo>(ReferenceEqualityComparer.Instance);

        bool Visit(TypeInfo current, int depth = 0)
        {
            // Some recursive aliases expand to a fresh record on every edge, so
            // reference-cycle detection alone cannot bound the walk. Treat an
            // exceptionally deep shape as generic/deferred.
            if (depth >= 24)
                return true;
            // Declaration libraries contain recursive object/tuple aliases. A
            // cycle by itself is not evidence of an open type variable.
            if (!visiting.Add(current))
                return false;
            try
            {
                return current switch
                {
                    TypeInfo.TypeParameter or TypeInfo.InferredTypeParameter => true,
                    // A conditional that reaches this point is deferred (concrete ones evaluate away).
                    TypeInfo.ConditionalType => true,
                    TypeInfo.KeyOf k => Visit(k.SourceType, depth + 1),
                    TypeInfo.IndexedAccess ia => Visit(ia.ObjectType, depth + 1) || Visit(ia.IndexType, depth + 1),
                    TypeInfo.Intersection i => i.Types.Any(t => Visit(t, depth + 1)),
                    TypeInfo.Union u => u.Types.Any(t => Visit(t, depth + 1)),
                    TypeInfo.Array arr => Visit(arr.ElementType, depth + 1),
                    TypeInfo.Promise p => Visit(p.ValueType, depth + 1),
                    TypeInfo.Tuple t => t.Elements.Any(e => Visit(e.Type, depth + 1))
                        || (t.RestElementType is { } rest && Visit(rest, depth + 1)),
                    TypeInfo.Function f => f.ParamTypes.Any(t => Visit(t, depth + 1))
                        || Visit(f.ReturnType, depth + 1),
                    TypeInfo.InstantiatedGeneric ig => ig.TypeArguments.Any(t => Visit(t, depth + 1)),
                    // A generic class instance (Box<infer V>) is wrapped in Instance; look through it so a
                    // type variable inside its arguments still defers the conditional / blocks eager resolution (#347).
                    TypeInfo.Instance inst => Visit(inst.ResolvedClassType, depth + 1),
                    TypeInfo.RecursiveTypeAlias rta => rta.TypeArguments is { } args
                        && args.Any(t => Visit(t, depth + 1)),
                    TypeInfo.Record r => r.Fields.Values.Any(t => Visit(t, depth + 1)),
                    TypeInfo.MappedType => true,
                    TypeInfo.IntrinsicStringType ist => Visit(ist.Inner, depth + 1),
                    TypeInfo.TemplateLiteralType tl => tl.InterpolatedTypes.Any(t => Visit(t, depth + 1)),
                    // Built-in containers (Map/Set/weak variants/iterators/generators) carry type variables in
                    // their arguments just like a generic instantiation (#347).
                    _ => DecomposeBuiltInContainer(current) is { } containerArgs
                        && containerArgs.Any(t => Visit(t, depth + 1))
                };
            }
            finally
            {
                visiting.Remove(current);
            }
        }

        return Visit(type);
    }

    /// <summary>
    /// In-progress guard for <see cref="GetConditionalConstraint"/> — self-referential
    /// constraints (e.g. <c>T76&lt;T extends T[]&gt;</c>) would otherwise recurse forever.
    /// </summary>
    [ThreadStatic]
    private static HashSet<string>? _conditionalConstraintInProgress;

    /// <summary>
    /// The assignability constraint of a DEFERRED conditional type, mirroring tsc's two-step
    /// rule for relating a conditional source to a non-conditional target:
    /// 1. Distributive constraint: for a distributive conditional whose check type is a
    ///    constrained type parameter, the conditional applied to that constraint
    ///    (<c>ZeroOf&lt;T extends number|string&gt;</c> ⇒ <c>0 | ""</c>).
    /// 2. Default constraint: union of the true branch instantiated with
    ///    <c>check ∩ extends</c> for the check parameter (tsc's substitution-type narrowing —
    ///    <c>Extract&lt;T, Foo&gt;</c> constrains to <c>T &amp; Foo</c>) and the false branch.
    /// Infer placeholders are erased to their declared constraint (or unknown).
    /// Returns null when no useful constraint can be computed.
    /// </summary>
    private List<TypeInfo> GetConditionalConstraints(TypeInfo.ConditionalType cond)
    {
        var key = cond.CacheKey();
        _conditionalConstraintInProgress ??= new(StringComparer.Ordinal);
        if (!_conditionalConstraintInProgress.Add(key)) return [];
        try
        {
            List<TypeInfo> constraints = [];

            // Step 1: distributive constraint over the check parameter's own constraint.
            if (cond.IsDistributive && cond.CheckType is TypeInfo.TypeParameter tp &&
                ApparentTypeOf(tp) is { } tpConstraint)
            {
                var instantiated = EvaluateConditionalType(cond, new() { [tp.Name] = tpConstraint });
                if (instantiated is not (TypeInfo.Never or TypeInfo.ConditionalType))
                    constraints.Add(instantiated);
            }

            // Step 2: default constraint — true branch under check ∩ extends, unioned with false.
            Dictionary<string, TypeInfo> subs = [];
            CollectInferSubstitutions(cond.ExtendsType, subs);
            var erasedExtends = subs.Count > 0
                ? SubstituteWithoutConditionalEval(cond.ExtendsType, subs)
                : cond.ExtendsType;
            TypeInfo trueConstraint;
            if (cond.CheckType is TypeInfo.TypeParameter checkTp)
            {
                subs[checkTp.Name] = SimplifyIntersection([checkTp, erasedExtends]);
                trueConstraint = Substitute(cond.TrueType, subs);
            }
            else if (TypeInfoEqualityComparer.Instance.Equals(cond.TrueType, cond.CheckType))
            {
                // tsc narrows true-branch occurrences of the check type via substitution types
                // even when the check is itself a composite (`Extract<Extract<T, Foo>, Bar>`:
                // the outer true branch is the inner conditional narrowed by ∩ Bar). We have no
                // substitution-type node, but the Extract/Exclude shape — true branch IS the
                // check type — covers the cases that reach here. A conditional check resolves to
                // its own constraint first so the intersection's object members can merge.
                var checkUpperBound = cond.CheckType is TypeInfo.ConditionalType innerCond &&
                    GetConditionalConstraints(innerCond) is { Count: > 0 } innerConstraints
                        ? innerConstraints[^1]
                        : cond.CheckType;
                trueConstraint = SimplifyIntersection([checkUpperBound, erasedExtends]);
            }
            else
            {
                trueConstraint = subs.Count > 0 ? Substitute(cond.TrueType, subs) : cond.TrueType;
            }
            constraints.Add(CreateUnion(trueConstraint, cond.FalseType));
            return constraints;
        }
        catch (TypeCheckException)
        {
            // Constraint computation is best-effort: a depth/circularity failure here must not
            // surface as a checker diagnostic — callers fall back to stricter rules.
            return [];
        }
        finally
        {
            _conditionalConstraintInProgress.Remove(key);
        }
    }

    /// <summary>
    /// Maps every <c>infer</c> placeholder in an extends clause to its declared constraint (or
    /// unknown), so branch types referencing them can be used as a constraint approximation.
    /// </summary>
    private static void CollectInferSubstitutions(TypeInfo extendsType, Dictionary<string, TypeInfo> subs)
    {
        switch (extendsType)
        {
            case TypeInfo.InferredTypeParameter infer:
                subs.TryAdd(infer.Name, infer.Constraint ?? TypeInfo.Unknown.Shared);
                break;
            case TypeInfo.Array arr: CollectInferSubstitutions(arr.ElementType, subs); break;
            case TypeInfo.Promise p: CollectInferSubstitutions(p.ValueType, subs); break;
            case TypeInfo.Function f:
                foreach (var pt in f.ParamTypes) CollectInferSubstitutions(pt, subs);
                CollectInferSubstitutions(f.ReturnType, subs);
                break;
            case TypeInfo.Tuple t:
                foreach (var e in t.Elements) CollectInferSubstitutions(e.Type, subs);
                if (t.RestElementType is { } rest) CollectInferSubstitutions(rest, subs);
                break;
            case TypeInfo.Union u:
                foreach (var m in u.Types) CollectInferSubstitutions(m, subs);
                break;
            case TypeInfo.Intersection i:
                foreach (var m in i.Types) CollectInferSubstitutions(m, subs);
                break;
            case TypeInfo.Record r:
                foreach (var (_, v) in r.Fields) CollectInferSubstitutions(v, subs);
                break;
            case TypeInfo.Interface itf:
                foreach (var (_, v) in itf.Members) CollectInferSubstitutions(v, subs);
                break;
            case TypeInfo.InstantiatedGeneric ig:
                foreach (var a in ig.TypeArguments) CollectInferSubstitutions(a, subs);
                break;
            case TypeInfo.KeyOf k: CollectInferSubstitutions(k.SourceType, subs); break;
            case TypeInfo.IndexedAccess ia:
                CollectInferSubstitutions(ia.ObjectType, subs);
                CollectInferSubstitutions(ia.IndexType, subs);
                break;
            case TypeInfo.TemplateLiteralType tl:
                foreach (var it in tl.InterpolatedTypes) CollectInferSubstitutions(it, subs);
                break;
        }
    }

    /// <summary>
    /// Matches a single function-shaped signature (plain function, or the function denoted by a call
    /// or construct signature) extracting infer bindings. Parameters are inference positions
    /// (covariant capture, like tsc), the return type is covariant. Shared by the Function branch and
    /// the call/construct-signature branches of <see cref="CheckExtendsRecursive"/>.
    /// </summary>
    private bool MatchSignatureWithInfer(
        TypeInfo.Function checkFunc,
        TypeInfo.Function extendsFunc,
        Dictionary<string, TypeInfo> inferredTypes)
    {
        // Match parameters (check function should have at least as many params; missing ones bind to any)
        for (int i = 0; i < extendsFunc.ParamTypes.Count; i++)
        {
            TypeInfo extendsParam = extendsFunc.ParamTypes[i];
            TypeInfo checkParam = i < checkFunc.ParamTypes.Count
                ? checkFunc.ParamTypes[i]
                : TypeInfo.Any.Shared;

            if (!CheckExtendsRecursive(checkParam, extendsParam, inferredTypes))
                return false;
        }

        // Match return type (covariant)
        return CheckExtendsRecursive(checkFunc.ReturnType, extendsFunc.ReturnType, inferredTypes);
    }

    /// <summary>
    /// Matches each extends-side signature against some check-side signature, extracting infer
    /// bindings. Returns false when the check side carries no compatible signatures (e.g. a
    /// non-constructable type against a construct-signature pattern), which steers the conditional
    /// to its false branch instead of vacuously succeeding.
    /// </summary>
    private bool MatchSignaturesWithInfer(
        List<TypeInfo.Function>? checkSigs,
        List<TypeInfo.Function> extendsSigs,
        Dictionary<string, TypeInfo> inferredTypes)
    {
        if (checkSigs is not { Count: > 0 })
            return false;
        foreach (var extendsSig in extendsSigs)
        {
            if (!checkSigs.Any(cs => MatchSignatureWithInfer(cs, extendsSig, inferredTypes)))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Construct signatures carried by the check side of an extends clause, viewed as the constructor
    /// functions they denote. Returns null when the type carries no construct signature. (A class
    /// value — <c>typeof C</c> — reaches infer matching through <see cref="ExpandInstanceType"/>'s
    /// dedicated path, not here.)
    /// </summary>
    private static List<TypeInfo.Function>? GetCheckConstructorSignatures(TypeInfo type) =>
        GetConstructorSignatures(type) is { Count: > 0 } sigs
            ? sigs.Select(ConstructorSignatureToFunction).ToList()
            : null;

    /// <summary>
    /// Call signatures carried by the check side of an extends clause, viewed as the functions they
    /// denote (a plain function is its own call signature). Returns null when the type is not callable.
    /// </summary>
    private static List<TypeInfo.Function>? GetCheckCallSignatures(TypeInfo type)
    {
        if (type is TypeInfo.Function f)
            return [f];
        return GetCallSignatures(type) is { Count: > 0 } sigs
            ? sigs.Select(CallSignatureToFunction).ToList()
            : null;
    }

    /// <summary>
    /// Views a type as an <see cref="TypeInfo.InstantiatedGeneric"/>, transparently looking through the
    /// <see cref="TypeInfo.Instance"/> wrapper that a generic class instance carries. Returns null when
    /// the type denotes neither (so the caller falls through to other matching rules).
    /// </summary>
    private static TypeInfo.InstantiatedGeneric? UnwrapToInstantiatedGeneric(TypeInfo type) => type switch
    {
        TypeInfo.InstantiatedGeneric ig => ig,
        TypeInfo.Instance { ResolvedClassType: TypeInfo.InstantiatedGeneric ig } => ig,
        _ => null
    };

    /// <summary>
    /// Decomposes a dedicated built-in container type (Map, Set, the weak variants, generators,
    /// iterators, WeakRef, FinalizationRegistry) into its type arguments, so the conditional-type
    /// machinery can treat <c>Map&lt;…, infer V&gt;</c> or <c>IterableIterator&lt;infer V&gt;</c>
    /// like a generic instantiation. These carry bespoke TypeInfo records rather than
    /// <see cref="TypeInfo.InstantiatedGeneric"/>. The set mirrors exactly the container names that
    /// <see cref="ResolveGenericType"/> resolves from a type reference — so an extends clause can
    /// actually denote one of them; Array/Promise/Tuple keep their own dedicated match branches and
    /// are excluded. Returns null for any other type (#347).
    /// </summary>
    private static IReadOnlyList<TypeInfo>? DecomposeBuiltInContainer(TypeInfo type) => type switch
    {
        TypeInfo.Map m => [m.KeyType, m.ValueType],
        TypeInfo.WeakMap m => [m.KeyType, m.ValueType],
        TypeInfo.Set s => [s.ElementType],
        TypeInfo.WeakSet s => [s.ElementType],
        TypeInfo.Generator g => [g.YieldType],
        TypeInfo.AsyncGenerator g => [g.YieldType],
        TypeInfo.Iterator it => [it.ElementType],
        TypeInfo.Iterable ib => [ib.ElementType],
        TypeInfo.AsyncIterator ait => [ait.ElementType],
        TypeInfo.AsyncIterable aib => [aib.ElementType],
        TypeInfo.WeakRef wr => [wr.TargetType],
        TypeInfo.FinalizationRegistry fr => [fr.TargetType],
        _ => null
    };

    /// <summary>
    /// Checks if two generic definitions refer to the same generic type.
    /// </summary>
    private static bool IsSameGenericDefinition(TypeInfo def1, TypeInfo def2)
    {
        return (def1, def2) switch
        {
            (TypeInfo.GenericClass gc1, TypeInfo.GenericClass gc2) => gc1.Name == gc2.Name,
            (TypeInfo.GenericInterface gi1, TypeInfo.GenericInterface gi2) => gi1.Name == gi2.Name,
            _ => false
        };
    }

    // ============== INTRINSIC STRING TYPE EVALUATION ==============

    /// <summary>
    /// Evaluates an intrinsic string manipulation type (Uppercase, Lowercase, Capitalize, Uncapitalize).
    /// </summary>
    private TypeInfo EvaluateIntrinsicStringType(TypeInfo input, StringManipulation operation)
    {
        return input switch
        {
            TypeInfo.StringLiteral sl => new TypeInfo.StringLiteral(ApplyStringManipulation(sl.Value, operation)),
            TypeInfo.Union u => new TypeInfo.Union(
                u.FlattenedTypes.Select(t => EvaluateIntrinsicStringType(t, operation)).ToList()),
            TypeInfo.TemplateLiteralType tl => new TypeInfo.TemplateLiteralType(
                tl.Strings.Select(s => ApplyStringManipulation(s, operation)).ToList(),
                tl.InterpolatedTypes.Select(t => EvaluateIntrinsicStringType(t, operation)).ToList()),
            TypeInfo.TypeParameter => new TypeInfo.IntrinsicStringType(operation, input),
            TypeInfo.IntrinsicStringType ist => new TypeInfo.IntrinsicStringType(operation,
                EvaluateIntrinsicStringType(ist.Inner, ist.Operation)),  // Compose intrinsics
            _ => TypeInfo.String.Shared  // Fallback for string, any, etc.
        };
    }

    /// <summary>
    /// Applies a string manipulation operation to a string value.
    /// </summary>
    private static string ApplyStringManipulation(string value, StringManipulation op) => op switch
    {
        StringManipulation.Uppercase => value.ToUpperInvariant(),
        StringManipulation.Lowercase => value.ToLowerInvariant(),
        StringManipulation.Capitalize => value.Length > 0
            ? char.ToUpperInvariant(value[0]) + value[1..] : value,
        StringManipulation.Uncapitalize => value.Length > 0
            ? char.ToLowerInvariant(value[0]) + value[1..] : value,
        _ => value
    };

    // ============== TEMPLATE LITERAL INFER MATCHING ==============

    /// <summary>
    /// Matches a type against a template literal pattern, extracting inferred types.
    /// </summary>
    private bool MatchTemplateLiteralWithInfer(
        TypeInfo checkType,
        TypeInfo.TemplateLiteralType pattern,
        Dictionary<string, TypeInfo> inferredTypes)
    {
        // String literal: try to match and extract parts
        if (checkType is TypeInfo.StringLiteral sl)
        {
            return MatchStringLiteralToTemplatePattern(sl.Value, pattern, inferredTypes);
        }

        // Union: distribute over members and combine inferred types
        if (checkType is TypeInfo.Union union)
        {
            var allInferred = new Dictionary<string, List<TypeInfo>>();
            foreach (var member in union.FlattenedTypes)
            {
                var memberInferred = new Dictionary<string, TypeInfo>();
                if (!MatchTemplateLiteralWithInfer(member, pattern, memberInferred))
                    return false;
                foreach (var (name, type) in memberInferred)
                {
                    if (!allInferred.ContainsKey(name))
                        allInferred[name] = [];
                    allInferred[name].Add(type);
                }
            }
            // Build union of inferred types
            foreach (var (name, types) in allInferred)
            {
                var distinct = types.Distinct(TypeInfoEqualityComparer.Instance).ToList();
                inferredTypes[name] = distinct.Count == 1 ? distinct[0] : new TypeInfo.Union(distinct);
            }
            return true;
        }

        // Template literal to template literal: structural match with recursive infer
        if (checkType is TypeInfo.TemplateLiteralType checkTL)
        {
            if (checkTL.Strings.Count != pattern.Strings.Count)
                return false;

            // Static strings must match
            for (int i = 0; i < pattern.Strings.Count; i++)
            {
                if (pattern.Strings[i] != checkTL.Strings[i])
                    return false;
            }

            // Recursively match interpolated types
            for (int i = 0; i < pattern.InterpolatedTypes.Count; i++)
            {
                if (!CheckExtendsRecursive(checkTL.InterpolatedTypes[i], pattern.InterpolatedTypes[i], inferredTypes))
                    return false;
            }
            return true;
        }

        // string type matches if all interpolated parts accept string
        if (checkType is TypeInfo.String)
        {
            // Bind all infer positions to string
            foreach (var typePart in pattern.InterpolatedTypes)
            {
                if (typePart is TypeInfo.InferredTypeParameter infer)
                {
                    if (!inferredTypes.TryGetValue(infer.Name, out _))
                        inferredTypes[infer.Name] = TypeInfo.String.Shared;
                }
            }
            return true;
        }

        return false;
    }

    /// <summary>
    /// Matches a string value against a template literal pattern, extracting captured parts.
    /// </summary>
    private bool MatchStringLiteralToTemplatePattern(
        string value,
        TypeInfo.TemplateLiteralType pattern,
        Dictionary<string, TypeInfo> inferredTypes)
    {
        int pos = 0;

        for (int i = 0; i < pattern.InterpolatedTypes.Count; i++)
        {
            string literalBefore = pattern.Strings[i];

            // Verify literal prefix matches
            if (!value[pos..].StartsWith(literalBefore))
                return false;
            pos += literalBefore.Length;

            // Find where this type part ends
            string literalAfter = pattern.Strings[i + 1];
            int endPos;

            if (i == pattern.InterpolatedTypes.Count - 1 && string.IsNullOrEmpty(literalAfter))
            {
                // Last interpolation with empty suffix - capture rest of string
                endPos = value.Length;
            }
            else if (string.IsNullOrEmpty(literalAfter))
            {
                // Empty separator - use minimal match (single char for first infer)
                endPos = pos + 1;
                if (endPos > value.Length) endPos = value.Length;
            }
            else
            {
                // Find next literal part
                endPos = value.IndexOf(literalAfter, pos);
                if (endPos < 0) return false;
            }

            string captured = value[pos..endPos];

            // Handle the interpolated type
            var typePart = pattern.InterpolatedTypes[i];
            if (typePart is TypeInfo.InferredTypeParameter infer)
            {
                // Infer binding
                if (inferredTypes.TryGetValue(infer.Name, out var existing))
                {
                    // Check consistency
                    if (existing is TypeInfo.StringLiteral existingSl && existingSl.Value != captured)
                        return false;
                }
                else
                {
                    inferredTypes[infer.Name] = new TypeInfo.StringLiteral(captured);
                }
            }
            else
            {
                // Non-infer type - check if captured matches
                if (!CheckExtendsRecursive(new TypeInfo.StringLiteral(captured), typePart, inferredTypes))
                    return false;
            }

            pos = endPos;
        }

        // Verify final suffix
        return value[pos..] == pattern.Strings[^1];
    }
}

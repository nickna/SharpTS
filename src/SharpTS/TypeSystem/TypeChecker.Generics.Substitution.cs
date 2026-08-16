using System.Collections.Frozen;

namespace SharpTS.TypeSystem;

/// <summary>
/// Type parameter substitution and tuple flattening operations.
/// </summary>
/// <remarks>
/// Contains methods: Substitute, SubstituteTupleWithFlattening, ValidateSpreadConstraints,
/// IsArrayLikeType, FlattenTupleSpreads.
/// </remarks>
public partial class TypeChecker
{
    /// <summary>
    /// Parameter positions whose DECLARED type is a naked type parameter being substituted with
    /// a concrete type — tsc's <c>isInstantiatedGenericParameter</c>, which gates the callback
    /// comparison rule (TypeScript #51620). Alpha-renames (parameter → parameter) do not mark;
    /// existing marks survive re-substitution.
    /// </summary>
    private static FrozenSet<int>? MarkInstantiatedParamPositions(TypeInfo.Function func, Dictionary<string, TypeInfo> substitutions)
    {
        HashSet<int>? marks = null;
        for (int i = 0; i < func.ParamTypes.Count; i++)
        {
            bool marked = func.IsInstantiatedTypeParamPosition(i) ||
                (func.ParamTypes[i] is TypeInfo.TypeParameter tp &&
                 substitutions.TryGetValue(tp.Name, out var replacement) &&
                 replacement is not TypeInfo.TypeParameter);
            if (marked) (marks ??= []).Add(i);
        }
        return marks?.ToFrozenSet();
    }

    /// <summary>
    /// Substitutes type parameters with concrete types, evaluating any conditional types reached.
    /// </summary>
    private TypeInfo Substitute(TypeInfo type, Dictionary<string, TypeInfo> substitutions)
        => Substitute(type, substitutions, evalConditionals: true);

    /// <summary>
    /// Single recursive substitution switch shared by <see cref="Substitute(TypeInfo, Dictionary{string, TypeInfo})"/>
    /// and <see cref="SubstituteWithoutConditionalEval"/>. The <paramref name="evalConditionals"/> flag is the ONLY
    /// behavioural axis between the two callers:
    /// <list type="bullet">
    ///   <item>conditional types: evaluate now (<c>true</c>) vs. recurse into their parts without evaluating
    ///         (<c>false</c>, the recursion-safe form used while a conditional is mid-evaluation);</item>
    ///   <item>function parameter positions: marked as instantiated (<c>true</c>, gates the callback comparison
    ///         rule) vs. left unmarked (<c>false</c>);</item>
    ///   <item>records: rebuilt fields-only (<c>true</c> — see the NOTE) vs. signature/index preserving
    ///         (<c>false</c>, which #316 needs so <c>T extends new (...) =&gt; infer U</c> can bind U);</item>
    ///   <item>indexed access: concrete accesses collapsed to the member type (<c>true</c>) vs. left as a
    ///         deferred node (<c>false</c>; the conditional machinery resolves these itself).</item>
    /// </list>
    /// Keeping ONE switch means a newly added <see cref="TypeInfo"/> variant is substituted on both paths —
    /// the conditional copy previously fell through <c>SpreadType</c>/<c>MappedType</c>/<c>RecursiveTypeAlias</c>
    /// to <c>_ =&gt; type</c>, silently returning a type parameter inside any of them un-substituted (#1106).
    /// </summary>
    private TypeInfo Substitute(TypeInfo type, Dictionary<string, TypeInfo> substitutions, bool evalConditionals)
    {
        TypeInfo Sub(TypeInfo t) => Substitute(t, substitutions, evalConditionals);
        return type switch
        {
            TypeInfo.TypeParameter tp =>
                substitutions.TryGetValue(tp.Name, out var sub) ? sub : type,
            TypeInfo.Array arr =>
                new TypeInfo.Array(Sub(arr.ElementType)),
            TypeInfo.Promise promise =>
                new TypeInfo.Promise(Sub(promise.ValueType)),
            TypeInfo.Function func =>
                new TypeInfo.Function(
                    func.ParamTypes.Select(Sub).ToList(),
                    Sub(func.ReturnType),
                    func.RequiredParams,
                    func.HasRestParam,
                    ThisType: null,
                    ParamNames: null,
                    // Marking instantiated parameter positions gates the callback comparison rule; only the
                    // assignability path (evalConditionals) needs it. The conditional path leaves them unmarked.
                    InstantiatedTypeParamPositions: evalConditionals
                        ? MarkInstantiatedParamPositions(func, substitutions)
                        : null),
            TypeInfo.Tuple tuple =>
                SubstituteTupleWithFlattening(tuple, substitutions, evalConditionals),
            TypeInfo.SpreadType spread =>
                new TypeInfo.SpreadType(Sub(spread.Inner)),
            TypeInfo.Union union =>
                new TypeInfo.Union(union.Types.Select(Sub).ToList()),
            // Assignability path (evalConditionals): fields only. Preserving call/construct signatures here
            // would feed generic construct-signature *assignment* relating (which erases/instantiates via
            // Substitute) types it does not yet compare correctly, regressing
            // assignmentCompatWithConstructSignatures. The conditional-type path that #316 needs preserves
            // them (SubstituteRecordMembers).
            TypeInfo.Record rec =>
                evalConditionals
                    ? new TypeInfo.Record(
                        rec.Fields.ToDictionary(
                            kvp => kvp.Key,
                            kvp => Sub(kvp.Value)).ToFrozenDictionary())
                    : SubstituteRecordMembers(rec, Sub),
            TypeInfo.InstantiatedGeneric ig =>
                new TypeInfo.InstantiatedGeneric(
                    ig.GenericDefinition,
                    ig.TypeArguments.Select(Sub).ToList()),
            // Handle new mapped type constructs
            TypeInfo.KeyOf keyOf =>
                new TypeInfo.KeyOf(Sub(keyOf.SourceType)),
            TypeInfo.TypeOf => type, // typeof doesn't contain type parameters, return as-is
            TypeInfo.MappedType mapped =>
                new TypeInfo.MappedType(
                    mapped.ParameterName,
                    Sub(mapped.Constraint),
                    Sub(mapped.ValueType),
                    mapped.Modifiers,
                    mapped.AsClause != null ? Sub(mapped.AsClause) : null),
            TypeInfo.IndexedAccess ia =>
                evalConditionals
                    // Collapse a fully concrete access to the member type; a generic one stays deferred.
                    ? SimplifyConcreteIndexedAccess(Sub(ia.ObjectType), Sub(ia.IndexType))
                    // The conditional machinery resolves indexed accesses itself (EvaluateConditionalType),
                    // so leave the node deferred here.
                    : new TypeInfo.IndexedAccess(Sub(ia.ObjectType), Sub(ia.IndexType)),
            // Conditional types: evaluate with current substitutions, or (recursion-safe form) substitute
            // into the parts without evaluating — preserving distributivity.
            TypeInfo.ConditionalType cond =>
                evalConditionals
                    ? EvaluateConditionalType(cond, substitutions)
                    : new TypeInfo.ConditionalType(
                        Sub(cond.CheckType),
                        Sub(cond.ExtendsType),
                        Sub(cond.TrueType),
                        Sub(cond.FalseType))
                      { IsDistributive = cond.IsDistributive },
            // Inferred type parameters: substitute if bound, else keep as-is (substituting any
            // outer type parameters referenced by the constraint)
            TypeInfo.InferredTypeParameter infer =>
                substitutions.TryGetValue(infer.Name, out var inferSub)
                    ? inferSub
                    : infer.Constraint is { } inferConstraint
                        ? infer with { Constraint = Sub(inferConstraint) }
                        : type,
            // Recursive type alias: substitute type arguments if present
            TypeInfo.RecursiveTypeAlias rta =>
                rta.TypeArguments is { Count: > 0 }
                    ? new TypeInfo.RecursiveTypeAlias(
                        rta.AliasName,
                        rta.TypeArguments.Select(Sub).ToList())
                    : rta,
            // Primitives, Any, Void, Never, Unknown, Null pass through unchanged
            _ => type
        };
    }

    /// <summary>
    /// Simplifies an indexed access whose object and index are both fully concrete (no remaining
    /// type variables) to the accessed member type — e.g. <c>Part[][number]</c> ⇒ <c>Part</c>,
    /// <c>Foo["bar"]</c> ⇒ the member type. A generic object or index is left as a deferred
    /// <see cref="TypeInfo.IndexedAccess"/> so distribution and per-instantiation resolution still
    /// run. Used by <see cref="Substitute"/> so substituting a concrete argument into a <c>T[K]</c>
    /// position (e.g. flattening <c>DeepReadonlyArray&lt;Part[][number]&gt;</c>) collapses the access
    /// instead of carrying an unresolved node that downstream consumers read as <c>any</c> (#365).
    /// </summary>
    private TypeInfo SimplifyConcreteIndexedAccess(TypeInfo objectType, TypeInfo indexType)
    {
        if (IsGenericTypeShape(objectType) || IsGenericTypeShape(indexType))
            return new TypeInfo.IndexedAccess(objectType, indexType);
        return ResolveIndexedAccess(
            new TypeInfo.IndexedAccess(objectType, indexType), new Dictionary<string, TypeInfo>());
    }

    /// <summary>
    /// Rebuilds a record applying <paramref name="sub"/> to every type-carrying member — fields,
    /// index signatures, and the parameter/return types of call and construct signatures — while
    /// preserving structural metadata (optional/readonly/getter-only/method flags). Used by
    /// <see cref="SubstituteWithoutConditionalEval"/>: the naive "copy Fields only" rebuild silently
    /// dropped construct/call signatures and index types, leaving <c>T extends new (...) =&gt; infer
    /// U</c> with an empty object on both sides so U never bound (#316).
    /// </summary>
    private static TypeInfo.Record SubstituteRecordMembers(TypeInfo.Record rec, Func<TypeInfo, TypeInfo> sub) =>
        new(
            rec.Fields.ToDictionary(kvp => kvp.Key, kvp => sub(kvp.Value)).ToFrozenDictionary(),
            rec.StringIndexType is { } sit ? sub(sit) : null,
            rec.NumberIndexType is { } nit ? sub(nit) : null,
            rec.SymbolIndexType is { } yit ? sub(yit) : null,
            rec.OptionalFields,
            rec.IsReadonly,
            rec.GetterOnlyFields,
            rec.CallSignatures?.Select(cs => cs with
            {
                ParamTypes = cs.ParamTypes.Select(sub).ToList(),
                ReturnType = sub(cs.ReturnType)
            }).ToList(),
            rec.ConstructorSignatures?.Select(cs => cs with
            {
                ParamTypes = cs.ParamTypes.Select(sub).ToList(),
                ReturnType = sub(cs.ReturnType)
            }).ToList(),
            rec.MethodMembers);

    /// <summary>
    /// Like <see cref="Substitute"/>, but preserves (and substitutes into) a Record's call/construct
    /// signatures and index types instead of dropping them. Used by the inheritance/extends relating
    /// paths — interface-extends (TS2430) and class-extends index signatures (TS2415) — where a base
    /// member or index value that is itself a construct/call signature must survive substitution to
    /// be related against the derived one. General <see cref="Substitute"/> intentionally rebuilds a
    /// Record fields-only (see the NOTE there) to keep generic construct-signature *assignment*
    /// relating unchanged; this variant is scoped to the conformance checks that need the signatures.
    /// Records recurse so a nested Record field keeps its signatures too; every non-Record type
    /// delegates to <see cref="Substitute"/> (whose only signature-dropping happens at Record nodes,
    /// which this intercepts).
    /// </summary>
    private TypeInfo SubstitutePreservingSignatures(TypeInfo type, Dictionary<string, TypeInfo> substitutions) =>
        type is TypeInfo.Record rec
            ? SubstituteRecordMembers(rec, t => SubstitutePreservingSignatures(t, substitutions))
            : Substitute(type, substitutions);

    /// <summary>
    /// Substitutes type parameters in a tuple with flattening of spread elements.
    /// When a spread resolves to a concrete tuple, its elements are inlined.
    /// <paramref name="evalConditionals"/> threads through to the element substitutions (see
    /// <see cref="Substitute(TypeInfo, Dictionary{string, TypeInfo}, bool)"/>).
    /// </summary>
    private TypeInfo SubstituteTupleWithFlattening(TypeInfo.Tuple tuple, Dictionary<string, TypeInfo> substitutions, bool evalConditionals)
    {
        List<TypeInfo.TupleElement> newElements = [];
        int newRequiredCount = 0;

        foreach (var elem in tuple.Elements)
        {
            if (elem.Kind == TupleElementKind.Spread)
            {
                var substitutedInner = Substitute(elem.Type, substitutions, evalConditionals);

                // Flatten if spread resolves to concrete tuple
                if (substitutedInner is TypeInfo.Tuple innerTuple)
                {
                    foreach (var innerElem in innerTuple.Elements)
                    {
                        newElements.Add(innerElem);
                        if (innerElem.Kind == TupleElementKind.Required)
                            newRequiredCount++;
                    }
                    // Preserve inner tuple's rest type as trailing spread
                    if (innerTuple.RestElementType != null)
                    {
                        newElements.Add(new TypeInfo.TupleElement(
                            new TypeInfo.Array(innerTuple.RestElementType),
                            TupleElementKind.Spread
                        ));
                    }
                }
                else if (substitutedInner is TypeInfo.Array arr)
                {
                    // Spread of array stays as spread
                    newElements.Add(new TypeInfo.TupleElement(arr, TupleElementKind.Spread, elem.Name));
                }
                else
                {
                    // Unresolved type parameter or other type - keep spread
                    newElements.Add(new TypeInfo.TupleElement(substitutedInner, TupleElementKind.Spread, elem.Name));
                }
            }
            else
            {
                var substitutedType = Substitute(elem.Type, substitutions, evalConditionals);
                newElements.Add(new TypeInfo.TupleElement(substitutedType, elem.Kind, elem.Name));
                if (elem.Kind == TupleElementKind.Required)
                    newRequiredCount++;
            }
        }

        var newRestType = tuple.RestElementType != null
            ? Substitute(tuple.RestElementType, substitutions, evalConditionals)
            : null;

        return new TypeInfo.Tuple(newElements, newRequiredCount, newRestType);
    }

    /// <summary>
    /// Validates spread constraints in a tuple type.
    /// Spread element inner types must be constrained to extend unknown[] or be concrete tuple/array types.
    /// </summary>
    private void ValidateSpreadConstraints(TypeInfo type, Dictionary<string, TypeInfo>? substitutions = null)
    {
        if (type is not TypeInfo.Tuple tuple) return;

        foreach (var elem in tuple.Elements)
        {
            if (elem.Kind != TupleElementKind.Spread) continue;

            var inner = elem.Type;
            if (substitutions != null)
                inner = Substitute(inner, substitutions);

            if (inner is TypeInfo.TypeParameter tp)
            {
                if (tp.Constraint == null || !IsArrayLikeType(tp.Constraint))
                {
                    throw new TypeSystem.Exceptions.TypeCheckException(
                        $" A rest element type must be an array type. " +
                        $"Type parameter '{tp.Name}' is not constrained to an array type.");
                }
            }
            else if (!IsArrayLikeType(inner))
            {
                throw new TypeSystem.Exceptions.TypeCheckException(
                    " A rest element type must be an array type.");
            }
        }
    }

    /// <summary>
    /// Checks if a type is array-like (valid for spread element).
    /// </summary>
    private static bool IsArrayLikeType(TypeInfo type) => type switch
    {
        TypeInfo.Array => true,
        TypeInfo.Tuple => true,
        TypeInfo.Any => true,
        TypeInfo.Unknown => true,
        _ => false
    };

    /// <summary>
    /// Flattens spread elements in tuples when they contain concrete tuple types.
    /// For example, [string, ...[number, boolean]] becomes [string, number, boolean].
    /// </summary>
    private static TypeInfo FlattenTupleSpreads(TypeInfo type)
    {
        if (type is not TypeInfo.Tuple tuple)
            return type;

        // Check if any spread element contains a tuple that needs flattening
        bool needsFlattening = tuple.Elements.Any(e =>
            e.Kind == TupleElementKind.Spread && e.Type is TypeInfo.Tuple);

        if (!needsFlattening)
        {
            // Recursively process nested tuple elements
            var processedElements = tuple.Elements.Select(e =>
                new TypeInfo.TupleElement(FlattenTupleSpreads(e.Type), e.Kind, e.Name)).ToList();
            return new TypeInfo.Tuple(processedElements, tuple.RequiredCount, tuple.RestElementType);
        }

        // Flatten the tuple
        List<TypeInfo.TupleElement> newElements = [];
        int newRequiredCount = 0;

        foreach (var elem in tuple.Elements)
        {
            if (elem.Kind == TupleElementKind.Spread && elem.Type is TypeInfo.Tuple innerTuple)
            {
                // Flatten: inline all elements from the inner tuple
                foreach (var innerElem in innerTuple.Elements)
                {
                    // Recursively flatten nested tuples
                    var flattenedType = FlattenTupleSpreads(innerElem.Type);
                    newElements.Add(new TypeInfo.TupleElement(flattenedType, innerElem.Kind, innerElem.Name));
                    if (innerElem.Kind == TupleElementKind.Required)
                        newRequiredCount++;
                }
                // Preserve inner tuple's rest type as trailing spread
                if (innerTuple.RestElementType != null)
                {
                    newElements.Add(new TypeInfo.TupleElement(
                        new TypeInfo.Array(innerTuple.RestElementType),
                        TupleElementKind.Spread
                    ));
                }
            }
            else
            {
                // Recursively process the element type
                var flattenedType = FlattenTupleSpreads(elem.Type);
                newElements.Add(new TypeInfo.TupleElement(flattenedType, elem.Kind, elem.Name));
                if (elem.Kind == TupleElementKind.Required)
                    newRequiredCount++;
            }
        }

        var newRestType = tuple.RestElementType;
        return new TypeInfo.Tuple(newElements, newRequiredCount, newRestType);
    }
}

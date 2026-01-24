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
    /// Substitutes type parameters with concrete types.
    /// </summary>
    private TypeInfo Substitute(TypeInfo type, Dictionary<string, TypeInfo> substitutions)
    {
        return type switch
        {
            TypeInfo.TypeParameter tp =>
                substitutions.TryGetValue(tp.Name, out var sub) ? sub : type,
            TypeInfo.Array arr =>
                new TypeInfo.Array(Substitute(arr.ElementType, substitutions)),
            TypeInfo.Promise promise =>
                new TypeInfo.Promise(Substitute(promise.ValueType, substitutions)),
            TypeInfo.Function func =>
                new TypeInfo.Function(
                    func.ParamTypes.Select(p => Substitute(p, substitutions)).ToList(),
                    Substitute(func.ReturnType, substitutions),
                    func.RequiredParams,
                    func.HasRestParam),
            TypeInfo.Tuple tuple =>
                SubstituteTupleWithFlattening(tuple, substitutions),
            TypeInfo.SpreadType spread =>
                new TypeInfo.SpreadType(Substitute(spread.Inner, substitutions)),
            TypeInfo.Union union =>
                new TypeInfo.Union(union.Types.Select(t => Substitute(t, substitutions)).ToList()),
            TypeInfo.Record rec =>
                new TypeInfo.Record(
                    rec.Fields.ToDictionary(
                        kvp => kvp.Key,
                        kvp => Substitute(kvp.Value, substitutions)).ToFrozenDictionary()),
            TypeInfo.InstantiatedGeneric ig =>
                new TypeInfo.InstantiatedGeneric(
                    ig.GenericDefinition,
                    ig.TypeArguments.Select(a => Substitute(a, substitutions)).ToList()),
            // Handle new mapped type constructs
            TypeInfo.KeyOf keyOf =>
                new TypeInfo.KeyOf(Substitute(keyOf.SourceType, substitutions)),
            TypeInfo.TypeOf => type, // typeof doesn't contain type parameters, return as-is
            TypeInfo.MappedType mapped =>
                new TypeInfo.MappedType(
                    mapped.ParameterName,
                    Substitute(mapped.Constraint, substitutions),
                    Substitute(mapped.ValueType, substitutions),
                    mapped.Modifiers,
                    mapped.AsClause != null ? Substitute(mapped.AsClause, substitutions) : null),
            TypeInfo.IndexedAccess ia =>
                new TypeInfo.IndexedAccess(
                    Substitute(ia.ObjectType, substitutions),
                    Substitute(ia.IndexType, substitutions)),
            // Conditional types: evaluate with current substitutions
            TypeInfo.ConditionalType cond =>
                EvaluateConditionalType(cond, substitutions),
            // Inferred type parameters: substitute if bound, else keep as-is
            TypeInfo.InferredTypeParameter infer =>
                substitutions.TryGetValue(infer.Name, out var inferSub) ? inferSub : type,
            // Recursive type alias: substitute type arguments if present
            TypeInfo.RecursiveTypeAlias rta =>
                rta.TypeArguments is { Count: > 0 }
                    ? new TypeInfo.RecursiveTypeAlias(
                        rta.AliasName,
                        rta.TypeArguments.Select(a => Substitute(a, substitutions)).ToList())
                    : rta,
            // Primitives, Any, Void, Never, Unknown, Null pass through unchanged
            _ => type
        };
    }

    /// <summary>
    /// Substitutes type parameters in a tuple with flattening of spread elements.
    /// When a spread resolves to a concrete tuple, its elements are inlined.
    /// </summary>
    private TypeInfo SubstituteTupleWithFlattening(TypeInfo.Tuple tuple, Dictionary<string, TypeInfo> substitutions)
    {
        List<TypeInfo.TupleElement> newElements = [];
        int newRequiredCount = 0;

        foreach (var elem in tuple.Elements)
        {
            if (elem.Kind == TupleElementKind.Spread)
            {
                var substitutedInner = Substitute(elem.Type, substitutions);

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
                var substitutedType = Substitute(elem.Type, substitutions);
                newElements.Add(new TypeInfo.TupleElement(substitutedType, elem.Kind, elem.Name));
                if (elem.Kind == TupleElementKind.Required)
                    newRequiredCount++;
            }
        }

        var newRestType = tuple.RestElementType != null
            ? Substitute(tuple.RestElementType, substitutions)
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

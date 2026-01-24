using SharpTS.TypeSystem.Exceptions;
using System.Collections.Frozen;

namespace SharpTS.TypeSystem;

/// <summary>
/// Conditional type evaluation, distribution, and extends checking with infer patterns.
/// </summary>
/// <remarks>
/// Contains methods: EvaluateConditionalType, SubstituteWithoutConditionalEval,
/// SubstituteTupleWithoutConditionalEval, DistributeConditionalOverUnion,
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
            throw new TypeCheckException(
                $"Conditional type recursion depth exceeded {MaxConditionalTypeDepth}. " +
                "This may indicate an infinitely recursive type definition.");
        }

        _conditionalTypeDepth++;
        try
        {
            // Apply current substitutions to the check type
            TypeInfo checkType = SubstituteWithoutConditionalEval(conditional.CheckType, substitutions);

            // If check type is still a naked type parameter (not substituted), defer evaluation
            if (checkType is TypeInfo.TypeParameter)
            {
                // Return unevaluated conditional with substitutions applied
                return new TypeInfo.ConditionalType(
                    checkType,
                    SubstituteWithoutConditionalEval(conditional.ExtendsType, substitutions),
                    SubstituteWithoutConditionalEval(conditional.TrueType, substitutions),
                    SubstituteWithoutConditionalEval(conditional.FalseType, substitutions)
                );
            }

            // Distribution: if check type is a union (either from substitution or directly), distribute
            // This handles both: T extends U ? X : Y where T substitutes to union,
            // and directly expanded types like: string | number extends U ? X : Y
            if (checkType is TypeInfo.Union union)
            {
                return DistributeConditionalOverUnion(conditional, union, substitutions);
            }

            // Apply substitutions to the extends type
            TypeInfo extendsType = SubstituteWithoutConditionalEval(conditional.ExtendsType, substitutions);

            // Perform the extends check with infer pattern matching
            var (matches, inferredTypes) = CheckExtendsWithInfer(checkType, extendsType);

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
    /// </summary>
    private TypeInfo SubstituteWithoutConditionalEval(TypeInfo type, Dictionary<string, TypeInfo> substitutions)
    {
        return type switch
        {
            TypeInfo.TypeParameter tp =>
                substitutions.TryGetValue(tp.Name, out var sub) ? sub : type,
            TypeInfo.Array arr =>
                new TypeInfo.Array(SubstituteWithoutConditionalEval(arr.ElementType, substitutions)),
            TypeInfo.Promise promise =>
                new TypeInfo.Promise(SubstituteWithoutConditionalEval(promise.ValueType, substitutions)),
            TypeInfo.Function func =>
                new TypeInfo.Function(
                    func.ParamTypes.Select(p => SubstituteWithoutConditionalEval(p, substitutions)).ToList(),
                    SubstituteWithoutConditionalEval(func.ReturnType, substitutions),
                    func.RequiredParams,
                    func.HasRestParam),
            TypeInfo.Tuple tuple =>
                SubstituteTupleWithoutConditionalEval(tuple, substitutions),
            TypeInfo.Union union =>
                new TypeInfo.Union(union.Types.Select(t => SubstituteWithoutConditionalEval(t, substitutions)).ToList()),
            TypeInfo.Record rec =>
                new TypeInfo.Record(
                    rec.Fields.ToDictionary(
                        kvp => kvp.Key,
                        kvp => SubstituteWithoutConditionalEval(kvp.Value, substitutions)).ToFrozenDictionary()),
            TypeInfo.InstantiatedGeneric ig =>
                new TypeInfo.InstantiatedGeneric(
                    ig.GenericDefinition,
                    ig.TypeArguments.Select(a => SubstituteWithoutConditionalEval(a, substitutions)).ToList()),
            TypeInfo.KeyOf keyOf =>
                new TypeInfo.KeyOf(SubstituteWithoutConditionalEval(keyOf.SourceType, substitutions)),
            TypeInfo.TypeOf => type, // typeof doesn't contain type parameters, return as-is
            TypeInfo.IndexedAccess ia =>
                new TypeInfo.IndexedAccess(
                    SubstituteWithoutConditionalEval(ia.ObjectType, substitutions),
                    SubstituteWithoutConditionalEval(ia.IndexType, substitutions)),
            TypeInfo.ConditionalType cond =>
                new TypeInfo.ConditionalType(
                    SubstituteWithoutConditionalEval(cond.CheckType, substitutions),
                    SubstituteWithoutConditionalEval(cond.ExtendsType, substitutions),
                    SubstituteWithoutConditionalEval(cond.TrueType, substitutions),
                    SubstituteWithoutConditionalEval(cond.FalseType, substitutions)),
            TypeInfo.InferredTypeParameter infer =>
                substitutions.TryGetValue(infer.Name, out var inferSub) ? inferSub : type,
            _ => type
        };
    }

    /// <summary>
    /// Substitutes type parameters in a tuple without triggering conditional type evaluation.
    /// Handles spread element flattening similar to SubstituteTupleWithFlattening.
    /// </summary>
    private TypeInfo SubstituteTupleWithoutConditionalEval(TypeInfo.Tuple tuple, Dictionary<string, TypeInfo> substitutions)
    {
        List<TypeInfo.TupleElement> newElements = [];
        int newRequiredCount = 0;

        foreach (var elem in tuple.Elements)
        {
            if (elem.Kind == TupleElementKind.Spread)
            {
                var substitutedInner = SubstituteWithoutConditionalEval(elem.Type, substitutions);

                // Flatten if spread resolves to concrete tuple
                if (substitutedInner is TypeInfo.Tuple innerTuple)
                {
                    foreach (var innerElem in innerTuple.Elements)
                    {
                        newElements.Add(innerElem);
                        if (innerElem.Kind == TupleElementKind.Required)
                            newRequiredCount++;
                    }
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
                    newElements.Add(new TypeInfo.TupleElement(arr, TupleElementKind.Spread, elem.Name));
                }
                else
                {
                    newElements.Add(new TypeInfo.TupleElement(substitutedInner, TupleElementKind.Spread, elem.Name));
                }
            }
            else
            {
                var substitutedType = SubstituteWithoutConditionalEval(elem.Type, substitutions);
                newElements.Add(new TypeInfo.TupleElement(substitutedType, elem.Kind, elem.Name));
                if (elem.Kind == TupleElementKind.Required)
                    newRequiredCount++;
            }
        }

        var newRestType = tuple.RestElementType != null
            ? SubstituteWithoutConditionalEval(tuple.RestElementType, substitutions)
            : null;

        return new TypeInfo.Tuple(newElements, newRequiredCount, newRestType);
    }

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
            return new TypeInfo.Never();
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
                // Already inferred - check consistency
                return IsCompatible(existing, checkType) || IsCompatible(checkType, existing);
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
                        0 => new TypeInfo.Never(),
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
                // Function assignability is contravariant in parameters, covariant in return
                // For infer in parameters, we infer from the check function's params
                // For infer in return, we infer from the check function's return

                // Match parameters (check function should have at least as many params)
                for (int i = 0; i < extendsFunc.ParamTypes.Count; i++)
                {
                    TypeInfo extendsParam = extendsFunc.ParamTypes[i];
                    TypeInfo checkParam = i < checkFunc.ParamTypes.Count
                        ? checkFunc.ParamTypes[i]
                        : new TypeInfo.Any();

                    if (!CheckExtendsRecursive(checkParam, extendsParam, inferredTypes))
                        return false;
                }

                // Match return type (covariant)
                return CheckExtendsRecursive(checkFunc.ReturnType, extendsFunc.ReturnType, inferredTypes);
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

        // InstantiatedGeneric matching (e.g., Box<infer T>)
        if (extendsType is TypeInfo.InstantiatedGeneric extendsGeneric)
        {
            if (checkType is TypeInfo.InstantiatedGeneric checkGeneric)
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

        // Record/object type matching
        if (extendsType is TypeInfo.Record extendsRec)
        {
            var checkProps = ExtractPropertiesWithTypes(checkType);
            foreach (var (key, extendsFieldType) in extendsRec.Fields)
            {
                if (!checkProps.TryGetValue(key, out var checkFieldType))
                    return false;
                if (!CheckExtendsRecursive(checkFieldType, extendsFieldType, inferredTypes))
                    return false;
            }
            return true;
        }

        // Interface matching
        if (extendsType is TypeInfo.Interface extendsItf)
        {
            var checkProps = ExtractPropertiesWithTypes(checkType);
            foreach (var (key, extendsFieldType) in extendsItf.Members)
            {
                if (extendsItf.OptionalMembers.Contains(key))
                    continue; // Optional members don't need to exist

                if (!checkProps.TryGetValue(key, out var checkFieldType))
                    return false;
                if (!CheckExtendsRecursive(checkFieldType, extendsFieldType, inferredTypes))
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
            _ => new TypeInfo.String()  // Fallback for string, any, etc.
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
                        inferredTypes[infer.Name] = new TypeInfo.String();
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

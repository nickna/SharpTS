using SharpTS.TypeSystem.Exceptions;
using System.Collections.Frozen;

namespace SharpTS.TypeSystem;

/// <summary>
/// Type argument inference from call arguments.
/// </summary>
/// <remarks>
/// Contains methods: InferTypeArguments, InferConstLiteralType, InferFromType, CreateUnion, TypesEqual.
/// </remarks>
public partial class TypeChecker
{
    /// <summary>
    /// Infers type arguments from call arguments for a generic function.
    /// </summary>
    private List<TypeInfo> InferTypeArguments(
        TypeInfo.GenericFunction gf,
        List<TypeInfo> argTypes,
        TypeInfo? contextualResultType = null,
        bool fallbackToConstraints = false,
        bool combineCandidates = false,
        TypeInfo.Tuple? tupleRestArguments = null)
    {
        Dictionary<string, TypeInfo> inferred = [];
        HashSet<string> contextualOnly = [];

        // Try to infer each type parameter from the corresponding argument
        int regularParams = gf.HasRestParam ? gf.ParamTypes.Count - 1 : gf.ParamTypes.Count;
        for (int i = 0; i < regularParams && i < argTypes.Count; i++)
        {
            InferFromType(gf.ParamTypes[i], argTypes[i], inferred, combineCandidates);
        }

        // Rest parameter: `...args: T[]` infers the element from every remaining argument;
        // `...args: A` where A is itself a type parameter (A extends any[]) infers A as the
        // TUPLE of the remaining argument types — pairing A with a single argument type would
        // produce a non-array inference that trips A's constraint (inferTypes1 invoker).
        if (gf.HasRestParam && gf.ParamTypes.Count > 0)
        {
            var restDeclared = gf.ParamTypes[^1];
            var restArgs = argTypes.Skip(regularParams).ToList();
            if (restDeclared is TypeInfo.Array restArr)
            {
                foreach (var restArg in restArgs)
                    InferFromType(restArr.ElementType, restArg, inferred, combineCandidates);
            }
            else if (restDeclared is TypeInfo.TypeParameter)
            {
                InferFromType(restDeclared, TypeInfo.Tuple.FromTypes(restArgs, restArgs.Count), inferred, combineCandidates);
            }
            else if (restDeclared is TypeInfo.Tuple tuple)
            {
                InferFromTupleRest(tuple, tupleRestArguments ?? TypeInfo.Tuple.FromTypes(restArgs, restArgs.Count),
                    inferred, combineCandidates);
            }
        }

        // A concrete assignment/return context is valid evidence for type parameters
        // that appear only in the result (`const n: number = host.create<T>()`).
        // Calls checked without a context retain the existing constraint/any fallback.
        if (contextualResultType is not null and not TypeInfo.Any)
        {
            Dictionary<string, TypeInfo> contextualInferences = [];
            InferFromType(gf.ReturnType, contextualResultType, contextualInferences, combineCandidates);
            foreach (var (name, candidate) in contextualInferences)
                if (inferred.TryAdd(name, candidate)) contextualOnly.Add(name);
        }

        // Build result list in order of type parameters
        List<TypeInfo> result = [];
        foreach (var tp in gf.TypeParams)
        {
            if (inferred.TryGetValue(tp.Name, out var inferredType))
            {
                // Validate constraint if present
                if (tp.Constraint != null && tp.Constraint is not TypeInfo.Any)
                {
                    // Substitute already-inferred type parameters in the constraint
                    // This handles cases like K extends keyof T where T is already inferred
                    var substitutedConstraint = Substitute(tp.Constraint, inferred);

                    // Return context is a lower-priority candidate, not an argument that
                    // violates the call's constraint. Keep compatible union alternatives,
                    // or use the constraint so the enclosing assignment checks the result.
                    if (contextualOnly.Contains(tp.Name) && !IsCompatible(substitutedConstraint, inferredType))
                    {
                        var candidates = inferredType is TypeInfo.Union union ? union.FlattenedTypes : [inferredType];
                        var compatible = candidates.Where(candidate => IsCompatible(substitutedConstraint, candidate)).ToList();
                        inferredType = compatible.Count == 0 ? substitutedConstraint : CollapseOrCreateUnion(compatible);
                    }

                    // For Record constraints, check that actual type has all required fields
                    if (substitutedConstraint is TypeInfo.Record constraintRecord && inferredType is TypeInfo.Record actualRecord)
                    {
                        bool constraintFailed = false;
                        foreach (var (fieldName, _) in constraintRecord.Fields)
                        {
                            if (!actualRecord.Fields.ContainsKey(fieldName))
                            {
                                if (!fallbackToConstraints)
                                    throw new TypeCheckException($"Inferred type '{inferredType}' does not satisfy constraint '{tp.Constraint}' for type parameter '{tp.Name}' - missing required property '{fieldName}'.", tsCode: "TS2344");
                                constraintFailed = true;
                                break;
                            }
                        }
                        if (constraintFailed)
                            inferredType = substitutedConstraint;
                    }
                    else if (!IsCompatible(substitutedConstraint, inferredType))
                    {
                        if (!fallbackToConstraints)
                            throw new TypeCheckException($"Inferred type '{inferredType}' does not satisfy constraint '{tp.Constraint}' for type parameter '{tp.Name}'.", tsCode: "TS2344");
                        inferredType = substitutedConstraint;
                    }
                }
                result.Add(inferredType);
                inferred[tp.Name] = inferredType;
            }
            else
            {
                bool dependsOnSuppliedArgument = gf.ParamTypes.Take(argTypes.Count).Any(parameter =>
                {
                    HashSet<string> names = [];
                    CollectSubstitutionParameterNames(parameter, names);
                    return names.Contains(tp.Name);
                });
                // Callback result inference is deferred until contextualization. Preserve its
                // fallback here; defaults for return-only parameters are already authoritative.
                TypeInfo fallback = tp.Default is { } defaultType && (fallbackToConstraints || !dependsOnSuppliedArgument)
                    ? Substitute(defaultType, inferred)
                    : tp.Constraint is { } constraint ? Substitute(constraint, inferred) : TypeInfo.Any.Shared;
                result.Add(fallback);
                inferred[tp.Name] = fallback;
            }
        }

        return result;
    }

    private void InferFromTupleRest(TypeInfo.Tuple pattern, TypeInfo.Tuple arguments,
        Dictionary<string, TypeInfo> inferred, bool combineCandidates)
    {
        int spread = pattern.Elements.FindIndex(element => element.IsSpread);
        int prefix = spread < 0 ? pattern.Elements.Count : spread;
        for (int i = 0; i < prefix && i < arguments.Elements.Count; i++)
            InferElement(pattern.Elements[i], arguments.Elements[i]);
        if (spread >= 0)
        {
            int suffix = pattern.Elements.Count - spread - 1;
            for (int i = 0; i < suffix && arguments.Elements.Count - 1 - i >= prefix; i++)
                InferElement(pattern.Elements[pattern.Elements.Count - 1 - i],
                    arguments.Elements[arguments.Elements.Count - 1 - i]);
            var middle = arguments.Elements.Skip(prefix)
                .Take(Math.Max(0, arguments.Elements.Count - prefix - suffix)).ToList();
            TypeInfo variable = pattern.Elements[spread].Type;
            if (variable is TypeInfo.Array array)
            {
                foreach (var argument in middle) InferFromType(array.ElementType, ElementType(argument), inferred, combineCandidates);
            }
            else
                InferFromType(variable, new TypeInfo.Tuple(middle, middle.Count(element => element.IsRequired)), inferred, combineCandidates);
        }
        else if (pattern.RestElementType is { } rest)
        {
            foreach (var argument in arguments.Elements.Skip(prefix))
                InferFromType(rest, ElementType(argument), inferred, combineCandidates);
        }

        void InferElement(TypeInfo.TupleElement parameter, TypeInfo.TupleElement argument)
        {
            TypeInfo type = ElementType(argument);
            if (parameter.IsOptional && type is TypeInfo.Undefined &&
                parameter.Type is TypeInfo.TypeParameter parameterType && inferred.ContainsKey(parameterType.Name)) return;
            InferFromType(parameter.Type, type, inferred, combineCandidates);
        }
        static TypeInfo ElementType(TypeInfo.TupleElement element) => element.IsSpread && element.Type is TypeInfo.Array array
            ? array.ElementType : element.Type;
    }

    /// <summary>
    /// Infers literal types with readonly semantics for const type parameters.
    /// Matches TypeScript 5.0+ behavior: preserves literal types AND marks objects/arrays readonly.
    /// </summary>
    private TypeInfo InferConstLiteralType(TypeInfo argType)
    {
        // Already literal? Keep it
        if (argType is TypeInfo.StringLiteral or TypeInfo.NumberLiteral or TypeInfo.BooleanLiteral or TypeInfo.BigIntLiteral)
            return argType;

        // Tuple: preserve element literal types + mark readonly
        if (argType is TypeInfo.Tuple tuple)
        {
            var constElements = tuple.Elements.Select(e =>
                new TypeInfo.TupleElement(InferConstLiteralType(e.Type), e.Kind, e.Name)).ToList();
            return new TypeInfo.Tuple(constElements, tuple.RequiredCount, tuple.RestElementType, IsReadonly: true);
        }

        // Array: mark as readonly array with recursively processed element type
        if (argType is TypeInfo.Array arr)
        {
            return new TypeInfo.Array(InferConstLiteralType(arr.ElementType), IsReadonly: true);
        }

        // Record: preserve field literal types + mark readonly
        if (argType is TypeInfo.Record rec)
        {
            var constFields = rec.Fields.ToDictionary(
                kvp => kvp.Key,
                kvp => InferConstLiteralType(kvp.Value)
            ).ToFrozenDictionary();
            return new TypeInfo.Record(constFields, rec.StringIndexType, rec.NumberIndexType,
                                       rec.SymbolIndexType, rec.OptionalFields, IsReadonly: true);
        }

        // For other types (primitives, functions, etc.), return as-is
        return argType;
    }

    /// <summary>
    /// Recursively infers type parameter bindings from a parameter type and an argument type.
    /// Supports const type parameters (TypeScript 5.0+) which preserve literal types during inference.
    /// </summary>
    private void InferFromType(
        TypeInfo paramType,
        TypeInfo argType,
        Dictionary<string, TypeInfo> inferred,
        bool combineCandidates = false)
    {
        if (paramType is TypeInfo.TypeParameter tp)
        {
            // Determine the type to infer - const type parameters preserve literals
            TypeInfo inferredType = tp.IsConst ? InferConstLiteralType(argType) : argType;

            if (inferred.TryGetValue(tp.Name, out var existing))
            {
                // JSX attributes are collected into one props object before generic inference.
                // Repeated occurrences of T must therefore contribute a best common candidate
                // (`value="a"; repeated="b"` infers string), rather than letting the first
                // property freeze T to the literal "a". Const parameters retain literal unions.
                if (!TypesEqual(existing, inferredType) && combineCandidates)
                {
                    inferred[tp.Name] = tp.IsConst
                        ? CreateUnion(existing, inferredType)
                        : CreateUnion(WidenLiteralType(existing), WidenLiteralType(inferredType));
                }
                else if (!TypesEqual(existing, inferredType))
                {
                    // Sibling arguments contribute compatible common candidates, but unrelated
                    // primitive domains keep the first inference so argument validation can reject
                    // same(1, "bad"). Numeric/string literals and object candidates can form unions.
                    if (!IsCompatible(existing, inferredType))
                    {
                        if (IsCompatible(inferredType, existing)) inferred[tp.Name] = inferredType;
                        else if (IsNullishInferenceCandidate(existing) || IsNullishInferenceCandidate(inferredType) ||
                            SamePrimitiveInferenceDomain(existing, inferredType) ||
                            IsObjectInferenceCandidate(existing) && IsObjectInferenceCandidate(inferredType))
                            inferred[tp.Name] = CreateUnion(existing, inferredType);
                    }
                }
            }
            else
            {
                inferred[tp.Name] = inferredType;
            }
        }
        else if (paramType is TypeInfo.Tuple tuplePattern && argType is TypeInfo.Tuple tupleArgument)
        {
            InferFromTupleRest(tuplePattern, tupleArgument, inferred, combineCandidates);
        }
        else if (paramType is TypeInfo.Array paramArr && argType is TypeInfo.Array argArr)
        {
            // Recurse into array element types
            InferFromType(paramArr.ElementType, argArr.ElementType, inferred, combineCandidates);
        }
        else if (paramType is TypeInfo.Array tupleParamArr && argType is TypeInfo.Tuple argTuple)
        {
            // Array literals commonly retain tuple precision. A CLR `T[]` parameter still
            // infers T from every fixed/rest tuple element rather than losing inference at
            // the tuple-vs-array representation boundary.
            foreach (var element in argTuple.ElementTypes)
                InferFromType(tupleParamArr.ElementType, element, inferred, combineCandidates);
            if (argTuple.RestElementType != null)
                InferFromType(tupleParamArr.ElementType, argTuple.RestElementType, inferred, combineCandidates);
        }
        else if (paramType is TypeInfo.Function paramFunc && argType is TypeInfo.Function argFunc)
        {
            argFunc = ExpandTupleRestSignature(argFunc);
            // Recurse into function types without merging candidates. Callback parameter and
            // return positions have different inference priorities/variance from sibling JSX
            // properties; treating them as co-equal candidates can incorrectly turn
            // `{ x: string }` and `string` into a union and hide a bad callback return.
            int fixedCount = paramFunc.HasRestParam ? paramFunc.ParamTypes.Count - 1 : paramFunc.ParamTypes.Count;
            for (int i = 0; i < fixedCount && i < argFunc.ParamTypes.Count; i++)
            {
                InferFromType(paramFunc.ParamTypes[i], argFunc.ParamTypes[i], inferred);
            }
            if (paramFunc.HasRestParam && paramFunc.ParamTypes.Count > 0)
            {
                var rest = paramFunc.ParamTypes[^1];
                if (rest is TypeInfo.TypeParameter)
                {
                    int actualFixedCount = argFunc.HasRestParam ? argFunc.ParamTypes.Count - 1 : argFunc.ParamTypes.Count;
                    var restTypes = argFunc.ParamTypes.Skip(fixedCount).Take(Math.Max(0, actualFixedCount - fixedCount)).ToList();
                    var restElement = argFunc.HasRestParam && argFunc.ParamTypes[^1] is TypeInfo.Array restArray
                        ? restArray.ElementType : null;
                    InferFromType(rest, TypeInfo.Tuple.FromTypes(restTypes,
                        Math.Clamp(argFunc.MinArity - fixedCount, 0, restTypes.Count), restElement), inferred);
                }
                else if (rest is TypeInfo.Array restArray)
                {
                    for (int i = fixedCount; i < argFunc.ParamTypes.Count; i++)
                    {
                        var actual = argFunc.ParamTypes[i];
                        bool isRest = argFunc.HasRestParam && i == argFunc.ParamTypes.Count - 1;
                        InferFromType(restArray.ElementType,
                            isRest && actual is TypeInfo.Array actualArray ? actualArray.ElementType : actual, inferred);
                    }
                }
            }
            InferFromType(paramFunc.ReturnType, argFunc.ReturnType, inferred);
        }
        else if (paramType is TypeInfo.ConditionalType conditional)
        {
            foreach (var branch in new[] { conditional.TrueType, conditional.FalseType })
            {
                var branchInferences = new Dictionary<string, TypeInfo>(inferred);
                InferFromType(branch, argType, branchInferences, combineCandidates);
                if (branchInferences.Count == inferred.Count && branchInferences.All(pair =>
                    inferred.TryGetValue(pair.Key, out var previous) && TypesEqual(previous, pair.Value)))
                    continue;
                var resolved = Substitute(conditional, branchInferences);
                if (resolved is TypeInfo.ConditionalType || !IsCompatible(resolved, argType)) continue;
                foreach (var (name, candidate) in branchInferences) inferred[name] = candidate;
                break;
            }
        }
        else if (paramType is TypeInfo.MappedType
                 { Constraint: TypeInfo.KeyOf keyOf, AsClause: null } mapped)
        {
            // Homomorphic mapped types (Readonly<T>, Partial<T>, and their direct forms)
            // preserve T's object shape and are inference sites for JSX/class arguments.
            if (keyOf.SourceType is TypeInfo.TypeParameter source && argType is TypeInfo.Record record &&
                mapped.ValueType is not TypeInfo.IndexedAccess
                {
                    ObjectType: TypeInfo.TypeParameter,
                    IndexType: TypeInfo.TypeParameter
                })
            {
                Dictionary<string, TypeInfo> reverseMembers = [];
                foreach (var (name, member) in record.Fields)
                {
                    var candidate = new TypeInfo.TypeParameter("'mappedValue");
                    var sourceMember = new TypeInfo.Record(new Dictionary<string, TypeInfo>
                        { [name] = candidate }.ToFrozenDictionary());
                    Dictionary<string, TypeInfo> substitutions = new()
                    {
                        [source.Name] = sourceMember,
                        [mapped.ParameterName] = new TypeInfo.StringLiteral(name)
                    };
                    var memberPattern = ResolveIndexedAccessTypes(Substitute(mapped.ValueType, substitutions), substitutions);
                    Dictionary<string, TypeInfo> memberInferences = [];
                    InferFromType(memberPattern, member, memberInferences, combineCandidates);
                    reverseMembers[name] = memberInferences.GetValueOrDefault(candidate.Name) ?? TypeInfo.Unknown.Shared;
                }
                InferFromType(source, new TypeInfo.Record(reverseMembers.ToFrozenDictionary(),
                    OptionalFields: record.OptionalFields, IsReadonly: record.IsReadonly,
                    ReadonlyFields: record.ReadonlyFields), inferred, combineCandidates);
            }
            else
                InferFromType(keyOf.SourceType, argType, inferred, combineCandidates);
        }
        else if (paramType is TypeInfo.Intersection intersection)
        {
            int inferredBefore = inferred.Count;
            foreach (TypeInfo member in intersection.FlattenedTypes.Where(member =>
                         member is not TypeInfo.TypeParameter))
                InferFromType(member, argType, inferred, combineCandidates);
            // A naked parameter in an intersection is a catch-all inference site. Prefer the
            // structured constituents when they inferred anything (Props & BaseProps<Values>),
            // but retain the catch-all for P & { children?: ... } when no structured inference
            // was possible.
            if (inferred.Count == inferredBefore)
                foreach (TypeInfo member in intersection.FlattenedTypes.OfType<TypeInfo.TypeParameter>())
                    InferFromType(member, argType, inferred, combineCandidates);
        }
        else if (paramType is TypeInfo.Record paramRecord && argType is TypeInfo.Record argRecord)
        {
            foreach ((string name, TypeInfo memberType) in paramRecord.Fields)
                if (argRecord.Fields.TryGetValue(name, out TypeInfo? argumentMember))
                    InferFromType(memberType, argumentMember, inferred, combineCandidates);
        }
        else if (paramType is TypeInfo.Interface paramInterface && argType is TypeInfo.Record interfaceArgument)
        {
            foreach ((string name, TypeInfo memberType) in paramInterface.GetAllMembers())
                if (interfaceArgument.Fields.TryGetValue(name, out TypeInfo? argumentMember))
                    InferFromType(memberType, argumentMember, inferred, combineCandidates);
        }
        else if (paramType is TypeInfo.InstantiatedGeneric
                 {
                     GenericDefinition: TypeInfo.GenericInterface genericInterface
                 } instantiatedInterface && argType is TypeInfo.Record genericInterfaceArgument)
        {
            Dictionary<string, TypeInfo> substitutions =
                GenericInterfaceSubs(genericInterface, instantiatedInterface.TypeArguments);
            foreach ((string name, TypeInfo memberType) in genericInterface.Members)
                if (genericInterfaceArgument.Fields.TryGetValue(name, out TypeInfo? argumentMember))
                    InferFromType(Substitute(memberType, substitutions), argumentMember, inferred, combineCandidates);
        }
        else if (paramType is TypeInfo.InstantiatedGeneric paramGen && argType is TypeInfo.InstantiatedGeneric argGen)
        {
            // Same generic base - infer from type arguments
            for (int i = 0; i < paramGen.TypeArguments.Count && i < argGen.TypeArguments.Count; i++)
            {
                InferFromType(paramGen.TypeArguments[i], argGen.TypeArguments[i], inferred, combineCandidates);
            }
        }
    }

    private static bool SamePrimitiveInferenceDomain(TypeInfo left, TypeInfo right)
    {
        static int Domain(TypeInfo type) => type switch
        {
            TypeInfo.String or TypeInfo.StringLiteral => 1,
            TypeInfo.Primitive { Type: Parsing.TokenType.TYPE_NUMBER } or TypeInfo.NumberLiteral => 2,
            TypeInfo.Primitive { Type: Parsing.TokenType.TYPE_BOOLEAN } or TypeInfo.BooleanLiteral => 3,
            TypeInfo.BigInt or TypeInfo.BigIntLiteral => 4,
            TypeInfo.Union union => UnionDomain(union),
            _ => 0
        };
        static int UnionDomain(TypeInfo.Union union)
        {
            var members = union.FlattenedTypes.Where(type => !IsNullishInferenceCandidate(type)).ToList();
            return members.Count > 0 && members.All(type => Domain(type) == Domain(members[0]))
                ? Domain(members[0]) : 0;
        }
        int domain = Domain(left);
        return domain != 0 && domain == Domain(right);
    }

    private static bool IsObjectInferenceCandidate(TypeInfo type) => type switch
    {
        TypeInfo.Record or TypeInfo.Interface or TypeInfo.Instance or TypeInfo.Array or TypeInfo.Tuple or
            TypeInfo.Function or TypeInfo.GenericFunction or TypeInfo.InstantiatedGeneric => true,
        TypeInfo.Union union => union.FlattenedTypes.Where(type => !IsNullishInferenceCandidate(type))
            .All(IsObjectInferenceCandidate),
        _ => false
    };

    private static bool IsNullishInferenceCandidate(TypeInfo type) => type switch
    {
        TypeInfo.Null or TypeInfo.Undefined => true,
        TypeInfo.Union union => union.FlattenedTypes.All(IsNullishInferenceCandidate),
        _ => false
    };

    /// <summary>
    /// Creates a union type from two types. If either is already a union, flattens them.
    /// </summary>
    private static TypeInfo CreateUnion(TypeInfo a, TypeInfo b)
    {
        List<TypeInfo> members = [];

        if (a is TypeInfo.Union ua)
            members.AddRange(ua.Types);
        else
            members.Add(a);

        if (b is TypeInfo.Union ub)
            members.AddRange(ub.Types);
        else
            members.Add(b);

        // tsc union normalization: `any` absorbs everything (any | X = any) and `never`
        // disappears (never | X = X).
        if (members.Any(m => m is TypeInfo.Any))
            return TypeInfo.Any.Shared;
        members.RemoveAll(m => m is TypeInfo.Never);
        if (members.Count == 0)
            return TypeInfo.Never.Shared;

        // Deduplicate (simple reference equality for now)
        var unique = members.Distinct().ToList();
        return unique.Count == 1 ? unique[0] : new TypeInfo.Union(unique);
    }

    /// <summary>
    /// Checks if two types are structurally equal for union deduplication.
    /// </summary>
    private static bool TypesEqual(TypeInfo a, TypeInfo b)
    {
        // Simple equality check - can be enhanced for structural equality
        return a.ToString() == b.ToString();
    }
}

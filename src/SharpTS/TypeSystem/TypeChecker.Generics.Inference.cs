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
        bool combineCandidates = false)
    {
        Dictionary<string, TypeInfo> inferred = [];

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
            else if (restDeclared is TypeInfo.TypeParameter && restArgs.Count > 0)
            {
                InferFromType(restDeclared, TypeInfo.Tuple.FromTypes(restArgs, restArgs.Count), inferred, combineCandidates);
            }
        }

        // A concrete assignment/return context is valid evidence for type parameters
        // that appear only in the result (`const n: number = host.create<T>()`).
        // Calls checked without a context retain the existing constraint/any fallback.
        if (contextualResultType is not null and not TypeInfo.Any)
            InferFromType(gf.ReturnType, contextualResultType, inferred, combineCandidates);

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
            }
            else
            {
                // JSX instantiation applies defaults when inference produced no candidate. Keep
                // ordinary call inference's established constraint/any fallback unchanged.
                result.Add(fallbackToConstraints
                    ? tp.Default ?? tp.Constraint ?? TypeInfo.Any.Shared
                    : tp.Constraint ?? TypeInfo.Any.Shared);
            }
        }

        return result;
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
                if (!TypesEqual(existing, inferredType) && (tp.IsConst || combineCandidates))
                {
                    inferred[tp.Name] = tp.IsConst
                        ? CreateUnion(existing, inferredType)
                        : CreateUnion(WidenLiteralType(existing), WidenLiteralType(inferredType));
                }
                // Non-const: keep existing behavior (first inferred type wins)
            }
            else
            {
                inferred[tp.Name] = inferredType;
            }
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
            // Recurse into function types without merging candidates. Callback parameter and
            // return positions have different inference priorities/variance from sibling JSX
            // properties; treating them as co-equal candidates can incorrectly turn
            // `{ x: string }` and `string` into a union and hide a bad callback return.
            for (int i = 0; i < paramFunc.ParamTypes.Count && i < argFunc.ParamTypes.Count; i++)
            {
                InferFromType(paramFunc.ParamTypes[i], argFunc.ParamTypes[i], inferred);
            }
            InferFromType(paramFunc.ReturnType, argFunc.ReturnType, inferred);
        }
        else if (paramType is TypeInfo.MappedType
                 { Constraint: TypeInfo.KeyOf keyOf, AsClause: null })
        {
            // Homomorphic mapped types (Readonly<T>, Partial<T>, and their direct forms)
            // preserve T's object shape and are inference sites for JSX/class arguments.
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

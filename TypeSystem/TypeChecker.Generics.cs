namespace SharpTS.TypeSystem;

/// <summary>
/// Generic type handling - instantiation, substitution, type argument inference.
/// </summary>
/// <remarks>
/// Contains generic type methods:
/// ParseGenericTypeReference, SplitTypeArguments,
/// InstantiateGenericClass, InstantiateGenericInterface, InstantiateGenericFunction,
/// Substitute, InferTypeArguments, InferFromType.
/// </remarks>
public partial class TypeChecker
{
    /// <summary>
    /// Parses a generic type reference like "Box&lt;number&gt;" or "Map&lt;string, number&gt;".
    /// </summary>
    private TypeInfo ParseGenericTypeReference(string typeName)
    {
        int openAngle = typeName.IndexOf('<');
        string baseName = typeName[..openAngle];
        string argsStr = typeName[(openAngle + 1)..^1];

        // Split type arguments respecting nesting
        var typeArgStrings = SplitTypeArguments(argsStr);
        var typeArgs = typeArgStrings.Select(ToTypeInfo).ToList();

        // Handle built-in generic types
        if (baseName == "Promise")
        {
            if (typeArgs.Count != 1)
            {
                throw new Exception($"Type Error: Promise requires exactly 1 type argument, got {typeArgs.Count}.");
            }
            // Flatten nested Promises: Promise<Promise<T>> -> Promise<T>
            TypeInfo valueType = typeArgs[0];
            while (valueType is TypeInfo.Promise nested)
            {
                valueType = nested.ValueType;
            }
            return new TypeInfo.Promise(valueType);
        }

        if (baseName == "Generator")
        {
            if (typeArgs.Count != 1)
            {
                throw new Exception($"Type Error: Generator requires exactly 1 type argument, got {typeArgs.Count}.");
            }
            return new TypeInfo.Generator(typeArgs[0]);
        }

        // Look up the generic definition
        TypeInfo? genericDef = _environment.Get(baseName);

        return genericDef switch
        {
            TypeInfo.GenericClass gc => new TypeInfo.Instance(InstantiateGenericClass(gc, typeArgs)),
            TypeInfo.GenericInterface gi => InstantiateGenericInterface(gi, typeArgs),
            TypeInfo.GenericFunction gf => InstantiateGenericFunction(gf, typeArgs),
            _ => new TypeInfo.Any() // Unknown generic type - fallback to any
        };
    }

    /// <summary>
    /// Splits type arguments respecting nested angle brackets.
    /// </summary>
    private List<string> SplitTypeArguments(string argsStr)
    {
        List<string> args = [];
        int depth = 0;
        int start = 0;

        for (int i = 0; i < argsStr.Length; i++)
        {
            char c = argsStr[i];
            if (c == '<') depth++;
            else if (c == '>') depth--;
            else if (c == ',' && depth == 0)
            {
                args.Add(argsStr[start..i].Trim());
                start = i + 1;
            }
        }

        if (start < argsStr.Length)
        {
            args.Add(argsStr[start..].Trim());
        }

        return args;
    }

    /// <summary>
    /// Instantiates a generic class with concrete type arguments.
    /// </summary>
    private TypeInfo InstantiateGenericClass(TypeInfo.GenericClass generic, List<TypeInfo> typeArgs)
    {
        if (typeArgs.Count != generic.TypeParams.Count)
        {
            throw new Exception($"Type Error: Generic class '{generic.Name}' requires {generic.TypeParams.Count} type argument(s), got {typeArgs.Count}.");
        }

        // Validate constraints
        for (int i = 0; i < typeArgs.Count; i++)
        {
            var tp = generic.TypeParams[i];
            if (tp.Constraint != null && !IsCompatible(tp.Constraint, typeArgs[i]))
            {
                throw new Exception($"Type Error: Type '{typeArgs[i]}' does not satisfy constraint '{tp.Constraint}' for type parameter '{tp.Name}'.");
            }
        }

        return new TypeInfo.InstantiatedGeneric(generic, typeArgs);
    }

    /// <summary>
    /// Instantiates a generic interface with concrete type arguments.
    /// </summary>
    private TypeInfo InstantiateGenericInterface(TypeInfo.GenericInterface generic, List<TypeInfo> typeArgs)
    {
        if (typeArgs.Count != generic.TypeParams.Count)
        {
            throw new Exception($"Type Error: Generic interface '{generic.Name}' requires {generic.TypeParams.Count} type argument(s), got {typeArgs.Count}.");
        }

        // Validate constraints
        for (int i = 0; i < typeArgs.Count; i++)
        {
            var tp = generic.TypeParams[i];
            if (tp.Constraint != null && !IsCompatible(tp.Constraint, typeArgs[i]))
            {
                throw new Exception($"Type Error: Type '{typeArgs[i]}' does not satisfy constraint '{tp.Constraint}' for type parameter '{tp.Name}'.");
            }
        }

        return new TypeInfo.InstantiatedGeneric(generic, typeArgs);
    }

    /// <summary>
    /// Instantiates a generic function with concrete type arguments.
    /// </summary>
    private TypeInfo InstantiateGenericFunction(TypeInfo.GenericFunction generic, List<TypeInfo> typeArgs)
    {
        if (typeArgs.Count != generic.TypeParams.Count)
        {
            throw new Exception($"Type Error: Generic function requires {generic.TypeParams.Count} type argument(s), got {typeArgs.Count}.");
        }

        // Validate constraints
        for (int i = 0; i < typeArgs.Count; i++)
        {
            var tp = generic.TypeParams[i];
            if (tp.Constraint != null && !IsCompatible(tp.Constraint, typeArgs[i]))
            {
                throw new Exception($"Type Error: Type '{typeArgs[i]}' does not satisfy constraint '{tp.Constraint}' for type parameter '{tp.Name}'.");
            }
        }

        // Create substitution map
        var substitutions = new Dictionary<string, TypeInfo>();
        for (int i = 0; i < typeArgs.Count; i++)
        {
            substitutions[generic.TypeParams[i].Name] = typeArgs[i];
        }

        // Substitute type parameters in the function signature
        var substitutedParams = generic.ParamTypes.Select(p => Substitute(p, substitutions)).ToList();
        var substitutedReturn = Substitute(generic.ReturnType, substitutions);

        return new TypeInfo.Function(substitutedParams, substitutedReturn, generic.RequiredParams, generic.HasRestParam);
    }

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
                new TypeInfo.Tuple(
                    tuple.ElementTypes.Select(e => Substitute(e, substitutions)).ToList(),
                    tuple.RequiredCount,
                    tuple.RestElementType != null ? Substitute(tuple.RestElementType, substitutions) : null),
            TypeInfo.Union union =>
                new TypeInfo.Union(union.Types.Select(t => Substitute(t, substitutions)).ToList()),
            TypeInfo.Record rec =>
                new TypeInfo.Record(rec.Fields.ToDictionary(
                    kvp => kvp.Key,
                    kvp => Substitute(kvp.Value, substitutions))),
            TypeInfo.InstantiatedGeneric ig =>
                new TypeInfo.InstantiatedGeneric(
                    ig.GenericDefinition,
                    ig.TypeArguments.Select(a => Substitute(a, substitutions)).ToList()),
            // Primitives, Any, Void, Never, Unknown, Null pass through unchanged
            _ => type
        };
    }

    /// <summary>
    /// Infers type arguments from call arguments for a generic function.
    /// </summary>
    private List<TypeInfo> InferTypeArguments(TypeInfo.GenericFunction gf, List<TypeInfo> argTypes)
    {
        var inferred = new Dictionary<string, TypeInfo>();

        // Try to infer each type parameter from the corresponding argument
        for (int i = 0; i < gf.ParamTypes.Count && i < argTypes.Count; i++)
        {
            InferFromType(gf.ParamTypes[i], argTypes[i], inferred);
        }

        // Build result list in order of type parameters
        var result = new List<TypeInfo>();
        foreach (var tp in gf.TypeParams)
        {
            if (inferred.TryGetValue(tp.Name, out var inferredType))
            {
                // Validate constraint
                if (tp.Constraint != null && !IsCompatible(tp.Constraint, inferredType))
                {
                    throw new Exception($"Type Error: Inferred type '{inferredType}' does not satisfy constraint '{tp.Constraint}' for type parameter '{tp.Name}'.");
                }
                result.Add(inferredType);
            }
            else
            {
                // Default to constraint or any if not inferred
                result.Add(tp.Constraint ?? new TypeInfo.Any());
            }
        }

        return result;
    }

    /// <summary>
    /// Recursively infers type parameter bindings from a parameter type and an argument type.
    /// </summary>
    private void InferFromType(TypeInfo paramType, TypeInfo argType, Dictionary<string, TypeInfo> inferred)
    {
        if (paramType is TypeInfo.TypeParameter tp)
        {
            // Direct type parameter - infer from argument
            if (!inferred.ContainsKey(tp.Name))
            {
                inferred[tp.Name] = argType;
            }
            // If already inferred, we could unify types here for more sophisticated inference
        }
        else if (paramType is TypeInfo.Array paramArr && argType is TypeInfo.Array argArr)
        {
            // Recurse into array element types
            InferFromType(paramArr.ElementType, argArr.ElementType, inferred);
        }
        else if (paramType is TypeInfo.Function paramFunc && argType is TypeInfo.Function argFunc)
        {
            // Recurse into function types
            for (int i = 0; i < paramFunc.ParamTypes.Count && i < argFunc.ParamTypes.Count; i++)
            {
                InferFromType(paramFunc.ParamTypes[i], argFunc.ParamTypes[i], inferred);
            }
            InferFromType(paramFunc.ReturnType, argFunc.ReturnType, inferred);
        }
        else if (paramType is TypeInfo.InstantiatedGeneric paramGen && argType is TypeInfo.InstantiatedGeneric argGen)
        {
            // Same generic base - infer from type arguments
            for (int i = 0; i < paramGen.TypeArguments.Count && i < argGen.TypeArguments.Count; i++)
            {
                InferFromType(paramGen.TypeArguments[i], argGen.TypeArguments[i], inferred);
            }
        }
    }
}

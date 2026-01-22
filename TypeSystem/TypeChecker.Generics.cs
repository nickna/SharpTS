using SharpTS.TypeSystem.Exceptions;
using System.Collections.Frozen;

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
    /// Also handles array suffixes: "Partial&lt;T&gt;[]", "Box&lt;number&gt;[][]".
    /// </summary>
    private TypeInfo ParseGenericTypeReference(string typeName)
    {
        int openAngle = typeName.IndexOf('<');
        string baseName = typeName[..openAngle];

        // Find matching closing '>' respecting nested angle brackets
        // Skip `>` that is part of `=>` (arrow function syntax)
        int angleDepth = 0;
        int closeAngle = -1;
        for (int i = openAngle; i < typeName.Length; i++)
        {
            char c = typeName[i];
            if (c == '<') angleDepth++;
            else if (c == '>')
            {
                // Skip `>` that is part of `=>` (arrow function return type)
                if (i > 0 && typeName[i - 1] == '=')
                    continue;

                angleDepth--;
                if (angleDepth == 0)
                {
                    closeAngle = i;
                    break;
                }
            }
        }

        string argsStr = typeName[(openAngle + 1)..closeAngle];
        string suffix = typeName[(closeAngle + 1)..];

        // Split type arguments respecting nesting
        var typeArgStrings = SplitTypeArguments(argsStr);
        var typeArgs = typeArgStrings.Select(ToTypeInfo).ToList();

        TypeInfo result;

        // Handle built-in generic types
        if (baseName == "Promise")
        {
            if (typeArgs.Count != 1)
            {
                throw new TypeCheckException($" Promise requires exactly 1 type argument, got {typeArgs.Count}.");
            }
            // Flatten nested Promises: Promise<Promise<T>> -> Promise<T>
            TypeInfo valueType = typeArgs[0];
            while (valueType is TypeInfo.Promise nested)
            {
                valueType = nested.ValueType;
            }
            result = new TypeInfo.Promise(valueType);
        }
        else if (baseName == "Generator")
        {
            if (typeArgs.Count != 1)
            {
                throw new TypeCheckException($" Generator requires exactly 1 type argument, got {typeArgs.Count}.");
            }
            result = new TypeInfo.Generator(typeArgs[0]);
        }
        else if (baseName == "AsyncGenerator")
        {
            if (typeArgs.Count != 1)
            {
                throw new TypeCheckException($" AsyncGenerator requires exactly 1 type argument, got {typeArgs.Count}.");
            }
            result = new TypeInfo.AsyncGenerator(typeArgs[0]);
        }
        // Handle built-in utility types
        else if (baseName == "Partial")
        {
            if (typeArgs.Count != 1)
                throw new TypeCheckException($" Partial<T> requires exactly 1 type argument, got {typeArgs.Count}.");
            result = ExpandPartial(typeArgs[0]);
        }
        else if (baseName == "Required")
        {
            if (typeArgs.Count != 1)
                throw new TypeCheckException($" Required<T> requires exactly 1 type argument, got {typeArgs.Count}.");
            result = ExpandRequired(typeArgs[0]);
        }
        else if (baseName == "Readonly")
        {
            if (typeArgs.Count != 1)
                throw new TypeCheckException($" Readonly<T> requires exactly 1 type argument, got {typeArgs.Count}.");
            result = ExpandReadonly(typeArgs[0]);
        }
        else if (baseName == "Record")
        {
            if (typeArgs.Count != 2)
                throw new TypeCheckException($" Record<K, V> requires exactly 2 type arguments, got {typeArgs.Count}.");
            result = ExpandRecordType(typeArgs[0], typeArgs[1]);
        }
        else if (baseName == "Pick")
        {
            if (typeArgs.Count != 2)
                throw new TypeCheckException($" Pick<T, K> requires exactly 2 type arguments, got {typeArgs.Count}.");
            result = ExpandPick(typeArgs[0], typeArgs[1]);
        }
        else if (baseName == "Omit")
        {
            if (typeArgs.Count != 2)
                throw new TypeCheckException($" Omit<T, K> requires exactly 2 type arguments, got {typeArgs.Count}.");
            result = ExpandOmit(typeArgs[0], typeArgs[1]);
        }
        // Additional utility types
        else if (baseName == "ReturnType")
        {
            if (typeArgs.Count != 1)
                throw new TypeCheckException($" ReturnType<T> requires exactly 1 type argument, got {typeArgs.Count}.");
            result = ExpandReturnType(typeArgs[0]);
        }
        else if (baseName == "Parameters")
        {
            if (typeArgs.Count != 1)
                throw new TypeCheckException($" Parameters<T> requires exactly 1 type argument, got {typeArgs.Count}.");
            result = ExpandParameters(typeArgs[0]);
        }
        else if (baseName == "ConstructorParameters")
        {
            if (typeArgs.Count != 1)
                throw new TypeCheckException($" ConstructorParameters<T> requires exactly 1 type argument, got {typeArgs.Count}.");
            result = ExpandConstructorParameters(typeArgs[0]);
        }
        else if (baseName == "InstanceType")
        {
            if (typeArgs.Count != 1)
                throw new TypeCheckException($" InstanceType<T> requires exactly 1 type argument, got {typeArgs.Count}.");
            result = ExpandInstanceType(typeArgs[0]);
        }
        else if (baseName == "ThisType")
        {
            if (typeArgs.Count != 1)
                throw new TypeCheckException($" ThisType<T> requires exactly 1 type argument, got {typeArgs.Count}.");
            // ThisType<T> is a marker type - it just wraps T for this-context typing
            result = typeArgs[0];
        }
        else if (baseName == "Awaited")
        {
            if (typeArgs.Count != 1)
                throw new TypeCheckException($" Awaited<T> requires exactly 1 type argument, got {typeArgs.Count}.");
            result = ExpandAwaited(typeArgs[0]);
        }
        else if (baseName == "NonNullable")
        {
            if (typeArgs.Count != 1)
                throw new TypeCheckException($" NonNullable<T> requires exactly 1 type argument, got {typeArgs.Count}.");
            result = ExpandNonNullable(typeArgs[0]);
        }
        else if (baseName == "Extract")
        {
            if (typeArgs.Count != 2)
                throw new TypeCheckException($" Extract<T, U> requires exactly 2 type arguments, got {typeArgs.Count}.");
            result = ExpandExtract(typeArgs[0], typeArgs[1]);
        }
        else if (baseName == "Exclude")
        {
            if (typeArgs.Count != 2)
                throw new TypeCheckException($" Exclude<T, U> requires exactly 2 type arguments, got {typeArgs.Count}.");
            result = ExpandExclude(typeArgs[0], typeArgs[1]);
        }
        else if (baseName is "Uppercase" or "Lowercase" or "Capitalize" or "Uncapitalize")
        {
            if (typeArgs.Count != 1)
                throw new TypeCheckException($" {baseName}<T> requires exactly 1 type argument, got {typeArgs.Count}.");

            var operation = baseName switch
            {
                "Uppercase" => StringManipulation.Uppercase,
                "Lowercase" => StringManipulation.Lowercase,
                "Capitalize" => StringManipulation.Capitalize,
                "Uncapitalize" => StringManipulation.Uncapitalize,
                _ => throw new InvalidOperationException()
            };
            result = EvaluateIntrinsicStringType(typeArgs[0], operation);
        }
        else
        {
            // Check for generic type alias first
            var genericAlias = _environment.GetGenericTypeAlias(baseName);
            if (genericAlias != null)
            {
                var (definition, typeParamNames) = genericAlias.Value;
                if (typeArgs.Count != typeParamNames.Count)
                {
                    throw new TypeCheckException($" Type alias '{baseName}' requires {typeParamNames.Count} type argument(s), got {typeArgs.Count}.");
                }

                // Create a unique key for this instantiation to detect recursive references
                string aliasKey = $"{baseName}<{string.Join(",", typeArgStrings)}>";
                _typeAliasExpansionStack ??= new HashSet<string>(StringComparer.Ordinal);

                // Recursive reference detected - return deferred placeholder
                if (_typeAliasExpansionStack.Contains(aliasKey))
                {
                    // Handle array suffix before returning
                    TypeInfo recursiveResult = new TypeInfo.RecursiveTypeAlias(baseName, typeArgs);
                    while (suffix.StartsWith("[]"))
                    {
                        recursiveResult = new TypeInfo.Array(recursiveResult);
                        suffix = suffix[2..];
                    }
                    return recursiveResult;
                }

                _typeAliasExpansionStack.Add(aliasKey);
                try
                {
                    // Substitute type parameters in the definition string
                    string expanded = definition;
                    for (int i = 0; i < typeParamNames.Count; i++)
                    {
                        // Replace type parameter with actual type argument string
                        expanded = SubstituteTypeParamInString(expanded, typeParamNames[i], typeArgStrings[i]);
                    }

                    // Parse the expanded definition
                    result = ToTypeInfo(expanded);

                    // Flatten any spread elements that contain concrete tuples
                    result = FlattenTupleSpreads(result);

                    // Validate spread constraints after type alias instantiation
                    ValidateSpreadConstraints(result);
                }
                finally
                {
                    _typeAliasExpansionStack.Remove(aliasKey);
                }
            }
            else
            {
                // Look up the generic definition
                TypeInfo? genericDef = _environment.Get(baseName);

                result = genericDef switch
                {
                    TypeInfo.GenericClass gc => new TypeInfo.Instance(InstantiateGenericClass(gc, typeArgs)),
                    TypeInfo.GenericInterface gi => InstantiateGenericInterface(gi, typeArgs),
                    TypeInfo.GenericFunction gf => InstantiateGenericFunction(gf, typeArgs),
                    _ => new TypeInfo.Any() // Unknown generic type - fallback to any
                };
            }
        }

        // Handle array suffix(es) after the generic type
        while (suffix.StartsWith("[]"))
        {
            result = new TypeInfo.Array(result);
            suffix = suffix[2..];
        }

        return result;
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
            // Track all bracket types to handle tuples and function types in type arguments
            if (c == '<' || c == '[' || c == '(') depth++;
            else if (c == '>' || c == ']' || c == ')') depth--;
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
    /// Substitutes a type parameter name with an actual type string in a type definition.
    /// Handles word boundaries to avoid partial replacements (e.g., "T" shouldn't match "Type").
    /// </summary>
    private static string SubstituteTypeParamInString(string definition, string paramName, string replacement)
    {
        // Use word boundary matching to avoid partial replacements
        var result = new System.Text.StringBuilder();
        int i = 0;
        while (i < definition.Length)
        {
            // Check if we're at the start of paramName
            if (i + paramName.Length <= definition.Length &&
                definition.Substring(i, paramName.Length) == paramName)
            {
                // Check word boundaries
                bool startBoundary = i == 0 || !char.IsLetterOrDigit(definition[i - 1]);
                bool endBoundary = i + paramName.Length >= definition.Length ||
                                   !char.IsLetterOrDigit(definition[i + paramName.Length]);

                if (startBoundary && endBoundary)
                {
                    result.Append(replacement);
                    i += paramName.Length;
                    continue;
                }
            }
            result.Append(definition[i]);
            i++;
        }
        return result.ToString();
    }

    /// <summary>
    /// Instantiates a generic class with concrete type arguments.
    /// Supports default type parameters - missing arguments are filled with defaults.
    /// </summary>
    private TypeInfo InstantiateGenericClass(TypeInfo.GenericClass generic, List<TypeInfo> typeArgs)
    {
        // Fill in defaults for missing type arguments
        var resolvedTypeArgs = ResolveTypeArgumentsWithDefaults(generic.TypeParams, typeArgs, generic.Name);

        // Build substitution map first (needed for recursive constraints)
        Dictionary<string, TypeInfo> substitutions = [];
        for (int i = 0; i < resolvedTypeArgs.Count; i++)
        {
            substitutions[generic.TypeParams[i].Name] = resolvedTypeArgs[i];
        }

        // Validate constraints - substitute type params in constraint first to handle recursive constraints
        for (int i = 0; i < resolvedTypeArgs.Count; i++)
        {
            var tp = generic.TypeParams[i];
            if (tp.Constraint != null)
            {
                var substitutedConstraint = Substitute(tp.Constraint, substitutions);
                if (!IsCompatible(substitutedConstraint, resolvedTypeArgs[i]))
                {
                    throw new TypeCheckException($" Type '{resolvedTypeArgs[i]}' does not satisfy constraint '{substitutedConstraint}' for type parameter '{tp.Name}'.");
                }
            }
        }

        return new TypeInfo.InstantiatedGeneric(generic, resolvedTypeArgs);
    }

    /// <summary>
    /// Instantiates a generic interface with concrete type arguments.
    /// Supports default type parameters - missing arguments are filled with defaults.
    /// </summary>
    private TypeInfo InstantiateGenericInterface(TypeInfo.GenericInterface generic, List<TypeInfo> typeArgs)
    {
        // Fill in defaults for missing type arguments
        var resolvedTypeArgs = ResolveTypeArgumentsWithDefaults(generic.TypeParams, typeArgs, generic.Name);

        // Build substitution map first (needed for recursive constraints like T extends TreeNode<T>)
        Dictionary<string, TypeInfo> substitutions = [];
        for (int i = 0; i < resolvedTypeArgs.Count; i++)
        {
            substitutions[generic.TypeParams[i].Name] = resolvedTypeArgs[i];
        }

        // Validate constraints - substitute type params in constraint first to handle recursive constraints
        for (int i = 0; i < resolvedTypeArgs.Count; i++)
        {
            var tp = generic.TypeParams[i];
            if (tp.Constraint != null)
            {
                // Substitute type parameters in the constraint (e.g., TreeNode<T> becomes TreeNode<MyNode>)
                var substitutedConstraint = Substitute(tp.Constraint, substitutions);
                if (!IsCompatible(substitutedConstraint, resolvedTypeArgs[i]))
                {
                    throw new TypeCheckException($" Type '{resolvedTypeArgs[i]}' does not satisfy constraint '{substitutedConstraint}' for type parameter '{tp.Name}'.");
                }
            }
        }

        return new TypeInfo.InstantiatedGeneric(generic, resolvedTypeArgs);
    }

    /// <summary>
    /// Instantiates a generic function with concrete type arguments.
    /// Supports default type parameters - missing arguments are filled with defaults.
    /// </summary>
    private TypeInfo InstantiateGenericFunction(TypeInfo.GenericFunction generic, List<TypeInfo> typeArgs)
    {
        // Fill in defaults for missing type arguments
        var resolvedTypeArgs = ResolveTypeArgumentsWithDefaults(generic.TypeParams, typeArgs, "function");

        // Create substitution map first (needed for recursive constraints)
        Dictionary<string, TypeInfo> substitutions = [];
        for (int i = 0; i < resolvedTypeArgs.Count; i++)
        {
            substitutions[generic.TypeParams[i].Name] = resolvedTypeArgs[i];
        }

        // Validate constraints - substitute type params in constraint first to handle recursive constraints
        for (int i = 0; i < resolvedTypeArgs.Count; i++)
        {
            var tp = generic.TypeParams[i];
            if (tp.Constraint != null)
            {
                var substitutedConstraint = Substitute(tp.Constraint, substitutions);
                if (!IsCompatible(substitutedConstraint, resolvedTypeArgs[i]))
                {
                    throw new TypeCheckException($" Type '{resolvedTypeArgs[i]}' does not satisfy constraint '{substitutedConstraint}' for type parameter '{tp.Name}'.");
                }
            }
        }

        // Substitute type parameters in the function signature
        var substitutedParams = generic.ParamTypes.Select(p => Substitute(p, substitutions)).ToList();
        var substitutedReturn = Substitute(generic.ReturnType, substitutions);

        return new TypeInfo.Function(substitutedParams, substitutedReturn, generic.RequiredParams, generic.HasRestParam);
    }

    /// <summary>
    /// Resolves type arguments, filling in defaults for missing arguments.
    /// </summary>
    /// <param name="typeParams">The type parameter definitions (with potential defaults).</param>
    /// <param name="typeArgs">The provided type arguments.</param>
    /// <param name="contextName">Name for error messages (e.g., class name).</param>
    /// <returns>Complete list of type arguments with defaults filled in.</returns>
    private List<TypeInfo> ResolveTypeArgumentsWithDefaults(
        List<TypeInfo.TypeParameter> typeParams,
        List<TypeInfo> typeArgs,
        string contextName)
    {
        // Count required type parameters (those without defaults)
        int requiredCount = typeParams.TakeWhile(tp => tp.Default == null).Count();

        if (typeArgs.Count < requiredCount)
        {
            throw new TypeCheckException($" Generic '{contextName}' requires at least {requiredCount} type argument(s), got {typeArgs.Count}.");
        }

        if (typeArgs.Count > typeParams.Count)
        {
            throw new TypeCheckException($" Generic '{contextName}' has {typeParams.Count} type parameter(s), but got {typeArgs.Count} type argument(s).");
        }

        // Build the resolved list
        List<TypeInfo> resolved = new(typeParams.Count);
        Dictionary<string, TypeInfo> substitutions = [];

        for (int i = 0; i < typeParams.Count; i++)
        {
            TypeInfo argType;
            if (i < typeArgs.Count)
            {
                // Use provided type argument
                argType = typeArgs[i];
            }
            else if (typeParams[i].Default != null)
            {
                // Use default, substituting any already-resolved type parameters
                argType = Substitute(typeParams[i].Default!, substitutions);
            }
            else
            {
                // Should not happen due to requiredCount check, but handle gracefully
                throw new TypeCheckException($" Missing type argument for type parameter '{typeParams[i].Name}' in generic '{contextName}'.");
            }

            resolved.Add(argType);
            substitutions[typeParams[i].Name] = argType;
        }

        return resolved;
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
                    throw new TypeCheckException(
                        $" A rest element type must be an array type. " +
                        $"Type parameter '{tp.Name}' is not constrained to an array type.");
                }
            }
            else if (!IsArrayLikeType(inner))
            {
                throw new TypeCheckException(
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

    /// <summary>
    /// Infers type arguments from call arguments for a generic function.
    /// </summary>
    private List<TypeInfo> InferTypeArguments(TypeInfo.GenericFunction gf, List<TypeInfo> argTypes)
    {
        Dictionary<string, TypeInfo> inferred = [];

        // Try to infer each type parameter from the corresponding argument
        for (int i = 0; i < gf.ParamTypes.Count && i < argTypes.Count; i++)
        {
            InferFromType(gf.ParamTypes[i], argTypes[i], inferred);
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

                    // For Record constraints, check that actual type has all required fields
                    if (substitutedConstraint is TypeInfo.Record constraintRecord && inferredType is TypeInfo.Record actualRecord)
                    {
                        foreach (var (fieldName, _) in constraintRecord.Fields)
                        {
                            if (!actualRecord.Fields.ContainsKey(fieldName))
                            {
                                throw new TypeCheckException($" Type Error: Inferred type '{inferredType}' does not satisfy constraint '{tp.Constraint}' for type parameter '{tp.Name}' - missing required property '{fieldName}'.");
                            }
                        }
                    }
                    else if (!IsCompatible(substitutedConstraint, inferredType))
                    {
                        throw new TypeCheckException($" Type Error: Inferred type '{inferredType}' does not satisfy constraint '{tp.Constraint}' for type parameter '{tp.Name}'.");
                    }
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
    /// Infers literal types with readonly semantics for const type parameters.
    /// Matches TypeScript 5.0+ behavior: preserves literal types AND marks objects/arrays readonly.
    /// </summary>
    private TypeInfo InferConstLiteralType(TypeInfo argType)
    {
        // Already literal? Keep it
        if (argType is TypeInfo.StringLiteral or TypeInfo.NumberLiteral or TypeInfo.BooleanLiteral)
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
    private void InferFromType(TypeInfo paramType, TypeInfo argType, Dictionary<string, TypeInfo> inferred)
    {
        if (paramType is TypeInfo.TypeParameter tp)
        {
            // Determine the type to infer - const type parameters preserve literals
            TypeInfo inferredType = tp.IsConst ? InferConstLiteralType(argType) : argType;

            if (inferred.TryGetValue(tp.Name, out var existing))
            {
                // Multiple arguments with same type param: create union (for const params)
                if (tp.IsConst && !TypesEqual(existing, inferredType))
                {
                    inferred[tp.Name] = CreateUnion(existing, inferredType);
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

    // ==================== KEYOF AND MAPPED TYPE SUPPORT ====================

    /// <summary>
    /// Evaluates keyof T to produce a union of string literal types representing the keys.
    /// </summary>
    private TypeInfo EvaluateKeyOf(TypeInfo sourceType)
    {
        var keys = ExtractKeys(sourceType);

        if (keys.Count == 0)
            return new TypeInfo.Never();

        if (keys.Count == 1)
            return keys[0];

        return new TypeInfo.Union(keys);
    }

    /// <summary>
    /// Extracts property keys from a type as a list of TypeInfo (StringLiteral, NumberLiteral, or primitives for index sigs).
    /// </summary>
    private List<TypeInfo> ExtractKeys(TypeInfo type)
    {
        List<TypeInfo> keys = [];

        switch (type)
        {
            case TypeInfo.Interface itf:
                // Add explicit member keys as string literals
                foreach (var key in itf.Members.Keys)
                    keys.Add(new TypeInfo.StringLiteral(key));
                // Add index signature key types
                if (itf.StringIndexType != null)
                    keys.Add(new TypeInfo.String());
                if (itf.NumberIndexType != null)
                    keys.Add(new TypeInfo.Primitive(Parsing.TokenType.TYPE_NUMBER));
                if (itf.SymbolIndexType != null)
                    keys.Add(new TypeInfo.Symbol());
                break;

            case TypeInfo.Record rec:
                foreach (var key in rec.Fields.Keys)
                    keys.Add(new TypeInfo.StringLiteral(key));
                if (rec.StringIndexType != null)
                    keys.Add(new TypeInfo.String());
                if (rec.NumberIndexType != null)
                    keys.Add(new TypeInfo.Primitive(Parsing.TokenType.TYPE_NUMBER));
                if (rec.SymbolIndexType != null)
                    keys.Add(new TypeInfo.Symbol());
                break;

            case TypeInfo.Class cls:
                // Public methods
                foreach (var key in cls.Methods.Keys)
                {
                    if (cls.MethodAccess.GetValueOrDefault(key, Parsing.AccessModifier.Public) == Parsing.AccessModifier.Public)
                        keys.Add(new TypeInfo.StringLiteral(key));
                }
                // Public fields
                foreach (var key in cls.FieldTypes.Keys)
                {
                    if (cls.FieldAccess.GetValueOrDefault(key, Parsing.AccessModifier.Public) == Parsing.AccessModifier.Public)
                        keys.Add(new TypeInfo.StringLiteral(key));
                }
                // Public getters
                foreach (var key in cls.Getters.Keys)
                    keys.Add(new TypeInfo.StringLiteral(key));
                break;

            case TypeInfo.Instance inst:
                keys.AddRange(ExtractKeys(inst.ClassType));
                break;

            case TypeInfo.InstantiatedGeneric ig:
                // Expand the instantiated generic and extract from that
                var expanded = ExpandInstantiatedGenericForKeyOf(ig);
                keys.AddRange(ExtractKeys(expanded));
                break;

            case TypeInfo.GenericInterface gi:
                // For uninstantiated generic interface, extract keys with type parameters
                foreach (var key in gi.Members.Keys)
                    keys.Add(new TypeInfo.StringLiteral(key));
                break;

            case TypeInfo.GenericClass gc:
                foreach (var key in gc.Methods.Keys)
                {
                    if (gc.MethodAccess.GetValueOrDefault(key, Parsing.AccessModifier.Public) == Parsing.AccessModifier.Public)
                        keys.Add(new TypeInfo.StringLiteral(key));
                }
                foreach (var key in gc.FieldTypes.Keys)
                {
                    if (gc.FieldAccess.GetValueOrDefault(key, Parsing.AccessModifier.Public) == Parsing.AccessModifier.Public)
                        keys.Add(new TypeInfo.StringLiteral(key));
                }
                break;

            case TypeInfo.Union union:
                // keyof (A | B) = (keyof A) & (keyof B) - intersection of keys (common keys)
                if (union.FlattenedTypes.Count > 0)
                {
                    var allKeysSets = union.FlattenedTypes
                        .Select(t => ExtractKeys(t).Select(k => k.ToString()).ToHashSet())
                        .ToList();
                    var commonKeys = allKeysSets.Aggregate((a, b) => { a.IntersectWith(b); return a; });
                    // Convert back to TypeInfo
                    foreach (var keyStr in commonKeys)
                    {
                        if (keyStr.StartsWith("\"") && keyStr.EndsWith("\""))
                            keys.Add(new TypeInfo.StringLiteral(keyStr[1..^1]));
                        else if (keyStr == "string")
                            keys.Add(new TypeInfo.String());
                        else if (keyStr == "number")
                            keys.Add(new TypeInfo.Primitive(Parsing.TokenType.TYPE_NUMBER));
                        else if (keyStr == "symbol")
                            keys.Add(new TypeInfo.Symbol());
                    }
                }
                break;

            case TypeInfo.Intersection intersection:
                // keyof (A & B) = (keyof A) | (keyof B) - union of keys (all keys)
                foreach (var t in intersection.FlattenedTypes)
                    keys.AddRange(ExtractKeys(t));
                break;

            case TypeInfo.TypeParameter tp:
                // Return keyof T as-is for unresolved type parameters
                keys.Add(new TypeInfo.KeyOf(tp));
                break;

            case TypeInfo.Any:
                // keyof any = string | number | symbol
                keys.Add(new TypeInfo.String());
                keys.Add(new TypeInfo.Primitive(Parsing.TokenType.TYPE_NUMBER));
                keys.Add(new TypeInfo.Symbol());
                break;
        }

        return keys.Distinct(TypeInfoEqualityComparer.Instance).ToList();
    }

    /// <summary>
    /// Expands an InstantiatedGeneric to a concrete type for keyof evaluation.
    /// </summary>
    private TypeInfo ExpandInstantiatedGenericForKeyOf(TypeInfo.InstantiatedGeneric ig)
    {
        Dictionary<string, TypeInfo> subs = [];

        switch (ig.GenericDefinition)
        {
            case TypeInfo.GenericInterface gi:
                for (int i = 0; i < gi.TypeParams.Count && i < ig.TypeArguments.Count; i++)
                    subs[gi.TypeParams[i].Name] = ig.TypeArguments[i];

                var substitutedMembers = gi.Members.ToDictionary(
                    kvp => kvp.Key,
                    kvp => Substitute(kvp.Value, subs));
                return new TypeInfo.Interface(gi.Name, substitutedMembers.ToFrozenDictionary(), gi.OptionalMembers);

            case TypeInfo.GenericClass gc:
                for (int i = 0; i < gc.TypeParams.Count && i < ig.TypeArguments.Count; i++)
                    subs[gc.TypeParams[i].Name] = ig.TypeArguments[i];

                return new TypeInfo.Class(
                    gc.Name,
                    gc.Superclass,
                    gc.Methods.ToDictionary(kvp => kvp.Key, kvp => Substitute(kvp.Value, subs)).ToFrozenDictionary(),
                    gc.StaticMethods,
                    gc.StaticProperties,
                    gc.MethodAccess,
                    gc.FieldAccess,
                    gc.ReadonlyFields,
                    gc.Getters.Count > 0 ? gc.Getters.ToDictionary(kvp => kvp.Key, kvp => Substitute(kvp.Value, subs)).ToFrozenDictionary() : FrozenDictionary<string, TypeInfo>.Empty,
                    gc.Setters.Count > 0 ? gc.Setters.ToDictionary(kvp => kvp.Key, kvp => Substitute(kvp.Value, subs)).ToFrozenDictionary() : FrozenDictionary<string, TypeInfo>.Empty,
                    gc.FieldTypes.Count > 0 ? gc.FieldTypes.ToDictionary(kvp => kvp.Key, kvp => Substitute(kvp.Value, subs)).ToFrozenDictionary() : FrozenDictionary<string, TypeInfo>.Empty);
        }

        return ig;
    }

    /// <summary>
    /// Expands a mapped type to a concrete Record/Interface type.
    /// Called lazily when the mapped type is used in compatibility checks or property access.
    /// </summary>
    private TypeInfo ExpandMappedType(TypeInfo.MappedType mapped, Dictionary<string, TypeInfo>? outerSubstitutions = null)
    {
        outerSubstitutions ??= [];

        // Get the keys from the constraint
        TypeInfo constraint = Substitute(mapped.Constraint, outerSubstitutions);

        // Evaluate keyof if present
        if (constraint is TypeInfo.KeyOf keyOf)
        {
            TypeInfo sourceType = Substitute(keyOf.SourceType, outerSubstitutions);
            constraint = EvaluateKeyOf(sourceType);
        }

        // Extract individual keys
        List<TypeInfo> keys = constraint switch
        {
            TypeInfo.Union union => union.FlattenedTypes,
            TypeInfo.StringLiteral => [constraint],
            TypeInfo.NumberLiteral => [constraint],
            TypeInfo.Never => [],
            _ => [constraint]
        };

        // Build the resulting object type
        Dictionary<string, TypeInfo> fields = [];
        HashSet<string> optionalFields = [];

        foreach (var key in keys)
        {
            string? keyName = key switch
            {
                TypeInfo.StringLiteral sl => sl.Value,
                TypeInfo.NumberLiteral nl => nl.Value.ToString(),
                _ => null
            };

            if (keyName == null) continue;

            // Apply as clause for key remapping if present
            string finalKeyName = keyName;
            if (mapped.AsClause != null)
            {
                var remappedKey = ApplyKeyRemapping(keyName, mapped.AsClause, mapped.ParameterName, outerSubstitutions);
                if (remappedKey == null) continue; // Key was filtered out (returned never)
                finalKeyName = remappedKey;
            }

            // Substitute K with the current key in the value type
            var localSubs = new Dictionary<string, TypeInfo>(outerSubstitutions)
            {
                [mapped.ParameterName] = key
            };
            TypeInfo valueType = Substitute(mapped.ValueType, localSubs);

            // Handle indexed access in value type (e.g., T[K])
            valueType = ResolveIndexedAccessTypes(valueType, localSubs);

            fields[finalKeyName] = valueType;

            // Apply modifiers
            if (mapped.Modifiers.HasFlag(MappedTypeModifiers.AddOptional))
            {
                optionalFields.Add(finalKeyName);
            }
            // For -?, we don't add to optionalFields (making it required)
        }

        // Return as Interface to preserve optional info
        return optionalFields.Count > 0
            ? new TypeInfo.Interface("", fields.ToFrozenDictionary(), optionalFields.ToFrozenSet())
            : new TypeInfo.Record(fields.ToFrozenDictionary());
    }

    /// <summary>
    /// Applies key remapping via the as clause (e.g., as Uppercase&lt;K&gt;).
    /// Returns null if the key should be filtered out (as clause evaluates to never).
    /// </summary>
    private string? ApplyKeyRemapping(string key, TypeInfo asClause, string paramName, Dictionary<string, TypeInfo> subs)
    {
        // Substitute the parameter with the current key
        var localSubs = new Dictionary<string, TypeInfo>(subs)
        {
            [paramName] = new TypeInfo.StringLiteral(key)
        };

        TypeInfo remapped = Substitute(asClause, localSubs);

        // Handle built-in string manipulation types (Uppercase, Lowercase, Capitalize, Uncapitalize)
        remapped = EvaluateStringManipulationType(remapped, key);

        return remapped switch
        {
            TypeInfo.StringLiteral sl => sl.Value,
            TypeInfo.Never => null, // Filter out this key
            _ => key // Default: keep original key
        };
    }

    /// <summary>
    /// Evaluates built-in string manipulation utility types.
    /// </summary>
    private TypeInfo EvaluateStringManipulationType(TypeInfo type, string input)
    {
        // Handle intrinsic string manipulation types
        if (type is TypeInfo.InstantiatedGeneric ig)
        {
            string name = ig.GenericDefinition switch
            {
                TypeInfo.GenericInterface gi => gi.Name,
                _ => ""
            };

            return name switch
            {
                "Uppercase" => new TypeInfo.StringLiteral(input.ToUpperInvariant()),
                "Lowercase" => new TypeInfo.StringLiteral(input.ToLowerInvariant()),
                "Capitalize" when input.Length > 0 => new TypeInfo.StringLiteral(char.ToUpperInvariant(input[0]) + input[1..]),
                "Uncapitalize" when input.Length > 0 => new TypeInfo.StringLiteral(char.ToLowerInvariant(input[0]) + input[1..]),
                _ => type
            };
        }
        return type;
    }

    /// <summary>
    /// Resolves IndexedAccess types within a type structure.
    /// </summary>
    private TypeInfo ResolveIndexedAccessTypes(TypeInfo type, Dictionary<string, TypeInfo> subs)
    {
        return type switch
        {
            TypeInfo.IndexedAccess ia => ResolveIndexedAccess(ia, subs),
            TypeInfo.Array arr => new TypeInfo.Array(ResolveIndexedAccessTypes(arr.ElementType, subs)),
            TypeInfo.Union union => new TypeInfo.Union(union.Types.Select(t => ResolveIndexedAccessTypes(t, subs)).ToList()),
            TypeInfo.Function func => new TypeInfo.Function(
                func.ParamTypes.Select(p => ResolveIndexedAccessTypes(p, subs)).ToList(),
                ResolveIndexedAccessTypes(func.ReturnType, subs),
                func.RequiredParams,
                func.HasRestParam),
            _ => type
        };
    }

    /// <summary>
    /// Resolves T[K] to the actual property type.
    /// </summary>
    private TypeInfo ResolveIndexedAccess(TypeInfo.IndexedAccess ia, Dictionary<string, TypeInfo> subs)
    {
        TypeInfo objectType = Substitute(ia.ObjectType, subs);
        TypeInfo indexType = Substitute(ia.IndexType, subs);

        // If object type is still a type parameter, check substitutions
        if (objectType is TypeInfo.TypeParameter tp && subs.TryGetValue(tp.Name, out var subObj))
        {
            objectType = subObj;
        }

        // Get the property type for the given key
        if (indexType is TypeInfo.StringLiteral sl)
        {
            return GetPropertyType(objectType, sl.Value) ?? new TypeInfo.Any();
        }

        // For union of keys, return union of property types
        if (indexType is TypeInfo.Union union)
        {
            var types = union.FlattenedTypes
                .OfType<TypeInfo.StringLiteral>()
                .Select(k => GetPropertyType(objectType, k.Value))
                .Where(t => t != null)
                .Cast<TypeInfo>()
                .Distinct(TypeInfoEqualityComparer.Instance)
                .ToList();

            if (types.Count == 0) return new TypeInfo.Any();
            if (types.Count == 1) return types[0];
            return new TypeInfo.Union(types);
        }

        // For string index type, return the index signature type
        if (indexType is TypeInfo.String)
        {
            return objectType switch
            {
                TypeInfo.Interface itf when itf.StringIndexType != null => itf.StringIndexType,
                TypeInfo.Record rec when rec.StringIndexType != null => rec.StringIndexType,
                _ => new TypeInfo.Any()
            };
        }

        return new TypeInfo.Any();
    }

    /// <summary>
    /// Gets the type of a property on an object type.
    /// </summary>
    private TypeInfo? GetPropertyType(TypeInfo objectType, string propertyName)
    {
        return objectType switch
        {
            TypeInfo.Interface itf => itf.Members.GetValueOrDefault(propertyName),
            TypeInfo.Record rec => rec.Fields.GetValueOrDefault(propertyName),
            TypeInfo.Class cls => cls.Methods.GetValueOrDefault(propertyName)
                               ?? cls.FieldTypes.GetValueOrDefault(propertyName)
                               ?? cls.Getters.GetValueOrDefault(propertyName),
            TypeInfo.Instance inst => GetPropertyType(inst.ClassType, propertyName),
            TypeInfo.InstantiatedGeneric ig => GetPropertyTypeFromInstantiatedGeneric(ig, propertyName),
            _ => null
        };
    }

    /// <summary>
    /// Gets a property type from an instantiated generic, applying substitutions.
    /// </summary>
    private TypeInfo? GetPropertyTypeFromInstantiatedGeneric(TypeInfo.InstantiatedGeneric ig, string propertyName)
    {
        Dictionary<string, TypeInfo> subs = [];

        switch (ig.GenericDefinition)
        {
            case TypeInfo.GenericInterface gi:
                for (int i = 0; i < gi.TypeParams.Count && i < ig.TypeArguments.Count; i++)
                    subs[gi.TypeParams[i].Name] = ig.TypeArguments[i];
                if (gi.Members.TryGetValue(propertyName, out var memberType))
                    return Substitute(memberType, subs);
                break;

            case TypeInfo.GenericClass gc:
                for (int i = 0; i < gc.TypeParams.Count && i < ig.TypeArguments.Count; i++)
                    subs[gc.TypeParams[i].Name] = ig.TypeArguments[i];
                if (gc.Methods.TryGetValue(propertyName, out var methodType))
                    return Substitute(methodType, subs);
                if (gc.FieldTypes.TryGetValue(propertyName, out var fieldType))
                    return Substitute(fieldType, subs);
                if (gc.Getters.TryGetValue(propertyName, out var getterType))
                    return Substitute(getterType, subs);
                break;
        }

        return null;
    }

    // ==================== UTILITY TYPE EXPANSION ====================

    /// <summary>
    /// Expands Partial&lt;T&gt; to an interface where all properties are optional.
    /// Equivalent to: { [K in keyof T]?: T[K] }
    /// </summary>
    private TypeInfo ExpandPartial(TypeInfo sourceType)
    {
        // If T is a type parameter, return a MappedType for lazy evaluation
        if (sourceType is TypeInfo.TypeParameter)
        {
            return new TypeInfo.MappedType(
                "K",
                new TypeInfo.KeyOf(sourceType),
                new TypeInfo.IndexedAccess(sourceType, new TypeInfo.TypeParameter("K")),
                MappedTypeModifiers.AddOptional
            );
        }

        // Handle unions: Partial<A | B> -> Partial<A> | Partial<B>
        if (sourceType is TypeInfo.Union union)
        {
            var expandedTypes = union.FlattenedTypes.Select(ExpandPartial).ToList();
            return new TypeInfo.Union(expandedTypes);
        }

        // Handle intersections: Partial<A & B> -> merge properties then make optional
        if (sourceType is TypeInfo.Intersection intersection)
        {
            var mergedProps = new Dictionary<string, TypeInfo>();
            foreach (var t in intersection.FlattenedTypes)
            {
                foreach (var (key, value) in ExtractPropertiesWithTypes(t))
                    mergedProps[key] = value;
            }
            if (mergedProps.Count == 0) return new TypeInfo.Record(FrozenDictionary<string, TypeInfo>.Empty);
            return new TypeInfo.Interface("", mergedProps.ToFrozenDictionary(), mergedProps.Keys.ToFrozenSet());
        }

        // Otherwise, expand immediately
        var props = ExtractPropertiesWithTypes(sourceType);
        if (props.Count == 0) return new TypeInfo.Record(FrozenDictionary<string, TypeInfo>.Empty);

        // All properties become optional
        return new TypeInfo.Interface("", props.ToFrozenDictionary(), props.Keys.ToFrozenSet());
    }

    /// <summary>
    /// Expands Required&lt;T&gt; to an interface where all properties are required.
    /// Equivalent to: { [K in keyof T]-?: T[K] }
    /// </summary>
    private TypeInfo ExpandRequired(TypeInfo sourceType)
    {
        // If T is a type parameter, return a MappedType for lazy evaluation
        if (sourceType is TypeInfo.TypeParameter)
        {
            return new TypeInfo.MappedType(
                "K",
                new TypeInfo.KeyOf(sourceType),
                new TypeInfo.IndexedAccess(sourceType, new TypeInfo.TypeParameter("K")),
                MappedTypeModifiers.RemoveOptional
            );
        }

        // Handle unions
        if (sourceType is TypeInfo.Union union)
        {
            var expandedTypes = union.FlattenedTypes.Select(ExpandRequired).ToList();
            return new TypeInfo.Union(expandedTypes);
        }

        // Handle intersections
        if (sourceType is TypeInfo.Intersection intersection)
        {
            var mergedProps = new Dictionary<string, TypeInfo>();
            foreach (var t in intersection.FlattenedTypes)
            {
                foreach (var (key, value) in ExtractPropertiesWithTypes(t))
                    mergedProps[key] = value;
            }
            if (mergedProps.Count == 0) return new TypeInfo.Record(FrozenDictionary<string, TypeInfo>.Empty);
            return new TypeInfo.Record(mergedProps.ToFrozenDictionary());
        }

        var props = ExtractPropertiesWithTypes(sourceType);
        if (props.Count == 0) return new TypeInfo.Record(FrozenDictionary<string, TypeInfo>.Empty);

        // All properties are required (empty optional set)
        return new TypeInfo.Record(props.ToFrozenDictionary());
    }

    /// <summary>
    /// Expands Readonly&lt;T&gt; to an interface where all properties are readonly.
    /// Equivalent to: { readonly [K in keyof T]: T[K] }
    /// </summary>
    private TypeInfo ExpandReadonly(TypeInfo sourceType)
    {
        // If T is a type parameter, return a MappedType for lazy evaluation
        if (sourceType is TypeInfo.TypeParameter)
        {
            return new TypeInfo.MappedType(
                "K",
                new TypeInfo.KeyOf(sourceType),
                new TypeInfo.IndexedAccess(sourceType, new TypeInfo.TypeParameter("K")),
                MappedTypeModifiers.AddReadonly
            );
        }

        // Handle unions
        if (sourceType is TypeInfo.Union union)
        {
            var expandedTypes = union.FlattenedTypes.Select(ExpandReadonly).ToList();
            return new TypeInfo.Union(expandedTypes);
        }

        // Handle intersections
        if (sourceType is TypeInfo.Intersection intersection)
        {
            var mergedProps = new Dictionary<string, TypeInfo>();
            var mergedOptionals = new HashSet<string>();
            foreach (var t in intersection.FlattenedTypes)
            {
                foreach (var (key, value) in ExtractPropertiesWithTypes(t))
                    mergedProps[key] = value;
                foreach (var opt in ExtractOptionalProperties(t))
                    mergedOptionals.Add(opt);
            }
            if (mergedProps.Count == 0) return new TypeInfo.Record(FrozenDictionary<string, TypeInfo>.Empty);
            return mergedOptionals.Count > 0
                ? new TypeInfo.Interface("", mergedProps.ToFrozenDictionary(), mergedOptionals.ToFrozenSet())
                : new TypeInfo.Record(mergedProps.ToFrozenDictionary());
        }

        var props = ExtractPropertiesWithTypes(sourceType);
        var optionals = ExtractOptionalProperties(sourceType);

        if (props.Count == 0) return new TypeInfo.Record(FrozenDictionary<string, TypeInfo>.Empty);

        // Note: Readonly modifier is semantic - affects assignment checks
        // The actual enforcement would be at assignment validation time
        return optionals.Count > 0
            ? new TypeInfo.Interface("", props.ToFrozenDictionary(), optionals.ToFrozenSet())
            : new TypeInfo.Record(props.ToFrozenDictionary());
    }

    /// <summary>
    /// Expands Record&lt;K, V&gt; to an object type with keys K and values V.
    /// K must be a valid key type (string, number, symbol, or union of string literals).
    /// </summary>
    private TypeInfo ExpandRecordType(TypeInfo keyType, TypeInfo valueType)
    {
        // Handle union of string literals: Record<"a" | "b", number> -> { a: number; b: number }
        if (keyType is TypeInfo.Union union)
        {
            var allStringLiterals = union.FlattenedTypes.All(t => t is TypeInfo.StringLiteral);
            if (allStringLiterals)
            {
                Dictionary<string, TypeInfo> fields = [];
                foreach (var lit in union.FlattenedTypes.OfType<TypeInfo.StringLiteral>())
                {
                    fields[lit.Value] = valueType;
                }
                return new TypeInfo.Record(fields.ToFrozenDictionary());
            }
        }

        // Handle single string literal: Record<"key", number> -> { key: number }
        if (keyType is TypeInfo.StringLiteral sl)
        {
            return new TypeInfo.Record(
                new Dictionary<string, TypeInfo> { [sl.Value] = valueType }.ToFrozenDictionary()
            );
        }

        // Handle string/number/symbol as key type -> use index signature
        TypeInfo? stringIndexType = null;
        TypeInfo? numberIndexType = null;
        TypeInfo? symbolIndexType = null;

        if (keyType is TypeInfo.String || IsKeyTypeAssignableToString(keyType))
        {
            stringIndexType = valueType;
        }
        else if (keyType is TypeInfo.Primitive { Type: Parsing.TokenType.TYPE_NUMBER })
        {
            numberIndexType = valueType;
        }
        else if (keyType is TypeInfo.Symbol)
        {
            symbolIndexType = valueType;
        }
        else if (keyType is TypeInfo.TypeParameter)
        {
            // For generic K extends string, default to string index
            stringIndexType = valueType;
        }
        else
        {
            // Default to string index for unknown key types
            stringIndexType = valueType;
        }

        return new TypeInfo.Record(
            FrozenDictionary<string, TypeInfo>.Empty,
            stringIndexType,
            numberIndexType,
            symbolIndexType
        );
    }

    /// <summary>
    /// Checks if a key type is assignable to string.
    /// </summary>
    private static bool IsKeyTypeAssignableToString(TypeInfo keyType)
    {
        return keyType switch
        {
            TypeInfo.String => true,
            TypeInfo.StringLiteral => true,
            TypeInfo.Union u => u.FlattenedTypes.All(t => t is TypeInfo.StringLiteral or TypeInfo.String),
            TypeInfo.TypeParameter tp when tp.Constraint != null => IsKeyTypeAssignableToString(tp.Constraint),
            _ => false
        };
    }

    /// <summary>
    /// Expands Pick&lt;T, K&gt; to include only properties from T whose keys are in K.
    /// K should be a union of string literals or keyof T.
    /// </summary>
    private TypeInfo ExpandPick(TypeInfo sourceType, TypeInfo keysType)
    {
        // If source is a type parameter, return a MappedType
        if (sourceType is TypeInfo.TypeParameter)
        {
            return new TypeInfo.MappedType(
                "P",
                keysType, // P in K
                new TypeInfo.IndexedAccess(sourceType, new TypeInfo.TypeParameter("P"))
            );
        }

        // Get the keys to pick
        var keysToPick = ExtractKeyNames(keysType);
        if (keysToPick.Count == 0) return new TypeInfo.Record(FrozenDictionary<string, TypeInfo>.Empty);

        // Get all properties from source
        var allProps = ExtractPropertiesWithTypes(sourceType);
        var optionals = ExtractOptionalProperties(sourceType);

        // Filter to only picked keys
        Dictionary<string, TypeInfo> pickedProps = [];
        HashSet<string> pickedOptionals = [];

        foreach (var key in keysToPick)
        {
            if (allProps.TryGetValue(key, out var propType))
            {
                pickedProps[key] = propType;
                if (optionals.Contains(key))
                    pickedOptionals.Add(key);
            }
        }

        if (pickedProps.Count == 0) return new TypeInfo.Record(FrozenDictionary<string, TypeInfo>.Empty);

        return pickedOptionals.Count > 0
            ? new TypeInfo.Interface("", pickedProps.ToFrozenDictionary(), pickedOptionals.ToFrozenSet())
            : new TypeInfo.Record(pickedProps.ToFrozenDictionary());
    }

    /// <summary>
    /// Expands Omit&lt;T, K&gt; to exclude properties from T whose keys are in K.
    /// </summary>
    private TypeInfo ExpandOmit(TypeInfo sourceType, TypeInfo keysType)
    {
        // If source is a type parameter, we need to handle this differently
        // For now, create a placeholder that will be resolved when T is known
        if (sourceType is TypeInfo.TypeParameter)
        {
            // Return a MappedType with keyof T, but we'll filter during compatibility
            return new TypeInfo.MappedType(
                "P",
                new TypeInfo.KeyOf(sourceType),
                new TypeInfo.IndexedAccess(sourceType, new TypeInfo.TypeParameter("P"))
            );
        }

        // Get the keys to omit
        var keysToOmit = ExtractKeyNames(keysType);

        // Get all properties from source
        var allProps = ExtractPropertiesWithTypes(sourceType);
        var optionals = ExtractOptionalProperties(sourceType);

        // Filter out omitted keys
        Dictionary<string, TypeInfo> remainingProps = [];
        HashSet<string> remainingOptionals = [];

        foreach (var (key, propType) in allProps)
        {
            if (!keysToOmit.Contains(key))
            {
                remainingProps[key] = propType;
                if (optionals.Contains(key))
                    remainingOptionals.Add(key);
            }
        }

        if (remainingProps.Count == 0) return new TypeInfo.Record(FrozenDictionary<string, TypeInfo>.Empty);

        return remainingOptionals.Count > 0
            ? new TypeInfo.Interface("", remainingProps.ToFrozenDictionary(), remainingOptionals.ToFrozenSet())
            : new TypeInfo.Record(remainingProps.ToFrozenDictionary());
    }

    // ==================== ADDITIONAL UTILITY TYPES ====================

    /// <summary>
    /// Expands ReturnType&lt;T&gt; to extract the return type of a function type.
    /// Equivalent to: T extends (...args: any[]) => infer R ? R : never
    /// </summary>
    private TypeInfo ExpandReturnType(TypeInfo functionType)
    {
        // Handle type parameters - defer evaluation
        if (functionType is TypeInfo.TypeParameter)
        {
            // Return a conditional type for lazy evaluation
            return new TypeInfo.ConditionalType(
                functionType,
                new TypeInfo.Function([new TypeInfo.Any()], new TypeInfo.Any(), HasRestParam: true),
                new TypeInfo.InferredTypeParameter("R"),
                new TypeInfo.Never()
            );
        }

        // Handle unions - distribute over union members
        if (functionType is TypeInfo.Union union)
        {
            var returnTypes = union.FlattenedTypes
                .Select(ExpandReturnType)
                .Where(t => t is not TypeInfo.Never)
                .Distinct()
                .ToList();

            return returnTypes.Count switch
            {
                0 => new TypeInfo.Never(),
                1 => returnTypes[0],
                _ => new TypeInfo.Union(returnTypes)
            };
        }

        // Extract return type from function types
        return functionType switch
        {
            TypeInfo.Function fn => fn.ReturnType,
            TypeInfo.OverloadedFunction of => of.Signatures.Count switch
            {
                0 => new TypeInfo.Never(),
                1 => of.Signatures[0].ReturnType,
                _ => new TypeInfo.Union(of.Signatures.Select(s => s.ReturnType).Distinct().ToList())
            },
            TypeInfo.GenericFunction gf => gf.ReturnType,
            _ => new TypeInfo.Never()
        };
    }

    /// <summary>
    /// Expands Parameters&lt;T&gt; to extract the parameter types of a function as a tuple.
    /// Equivalent to: T extends (...args: infer P) => any ? P : never
    /// </summary>
    private TypeInfo ExpandParameters(TypeInfo functionType)
    {
        // Handle type parameters - defer evaluation
        if (functionType is TypeInfo.TypeParameter)
        {
            return new TypeInfo.ConditionalType(
                functionType,
                new TypeInfo.Function([new TypeInfo.Any()], new TypeInfo.Any(), HasRestParam: true),
                new TypeInfo.InferredTypeParameter("P"),
                new TypeInfo.Never()
            );
        }

        // Handle unions - distribute over union members
        if (functionType is TypeInfo.Union union)
        {
            var paramTypes = union.FlattenedTypes
                .Select(ExpandParameters)
                .Where(t => t is not TypeInfo.Never)
                .Distinct()
                .ToList();

            return paramTypes.Count switch
            {
                0 => new TypeInfo.Never(),
                1 => paramTypes[0],
                _ => new TypeInfo.Union(paramTypes)
            };
        }

        // Extract parameter types as tuple from function types
        return functionType switch
        {
            TypeInfo.Function fn => TypeInfo.Tuple.FromTypes(fn.ParamTypes, fn.ParamTypes.Count),
            TypeInfo.OverloadedFunction of when of.Signatures.Count > 0 =>
                // Use the last (most general) signature for overloaded functions
                TypeInfo.Tuple.FromTypes(of.Signatures[^1].ParamTypes, of.Signatures[^1].ParamTypes.Count),
            TypeInfo.GenericFunction gf => TypeInfo.Tuple.FromTypes(gf.ParamTypes, gf.ParamTypes.Count),
            _ => new TypeInfo.Never()
        };
    }

    /// <summary>
    /// Expands ConstructorParameters&lt;T&gt; to extract the constructor parameter types as a tuple.
    /// Equivalent to: T extends abstract new (...args: infer P) => any ? P : never
    /// </summary>
    private TypeInfo ExpandConstructorParameters(TypeInfo classType)
    {
        // Handle type parameters - defer evaluation
        if (classType is TypeInfo.TypeParameter)
        {
            return new TypeInfo.ConditionalType(
                classType,
                new TypeInfo.Any(), // Represents constructor type
                new TypeInfo.InferredTypeParameter("P"),
                new TypeInfo.Never()
            );
        }

        // Handle unions - distribute
        if (classType is TypeInfo.Union union)
        {
            var ctorParams = union.FlattenedTypes
                .Select(ExpandConstructorParameters)
                .Where(t => t is not TypeInfo.Never)
                .Distinct()
                .ToList();

            return ctorParams.Count switch
            {
                0 => new TypeInfo.Never(),
                1 => ctorParams[0],
                _ => new TypeInfo.Union(ctorParams)
            };
        }

        // Extract constructor parameters from class types
        return classType switch
        {
            TypeInfo.Class cls => ExtractConstructorParams(cls),
            TypeInfo.MutableClass mc => ExtractConstructorParams(mc.Freeze()),
            TypeInfo.GenericClass gc => ExtractConstructorParams(gc),
            TypeInfo.InstantiatedGeneric ig when ig.GenericDefinition is TypeInfo.GenericClass gc =>
                SubstituteConstructorParams(gc, ig.TypeArguments),
            _ => new TypeInfo.Never()
        };
    }

    /// <summary>
    /// Extracts constructor parameter types from a class.
    /// </summary>
    private static TypeInfo ExtractConstructorParams(TypeInfo.Class cls)
    {
        // Look for constructor in methods
        if (cls.Methods.TryGetValue("constructor", out var ctorType) && ctorType is TypeInfo.Function fn)
        {
            return TypeInfo.Tuple.FromTypes(fn.ParamTypes, fn.ParamTypes.Count);
        }
        // No explicit constructor - empty tuple
        return new TypeInfo.Tuple([], 0);
    }

    /// <summary>
    /// Extracts constructor parameter types from a generic class.
    /// </summary>
    private static TypeInfo ExtractConstructorParams(TypeInfo.GenericClass gc)
    {
        if (gc.Methods.TryGetValue("constructor", out var ctorType) && ctorType is TypeInfo.Function fn)
        {
            return TypeInfo.Tuple.FromTypes(fn.ParamTypes, fn.ParamTypes.Count);
        }
        return new TypeInfo.Tuple([], 0);
    }

    /// <summary>
    /// Substitutes type parameters in constructor parameters for instantiated generic classes.
    /// </summary>
    private TypeInfo SubstituteConstructorParams(TypeInfo.GenericClass gc, List<TypeInfo> typeArgs)
    {
        if (!gc.Methods.TryGetValue("constructor", out var ctorType) || ctorType is not TypeInfo.Function fn)
        {
            return new TypeInfo.Tuple([], 0);
        }

        // Build substitution map
        var substitutions = new Dictionary<string, TypeInfo>();
        for (int i = 0; i < gc.TypeParams.Count && i < typeArgs.Count; i++)
        {
            substitutions[gc.TypeParams[i].Name] = typeArgs[i];
        }

        // Substitute type parameters in param types
        var substitutedParams = fn.ParamTypes.Select(p => Substitute(p, substitutions)).ToList();
        return TypeInfo.Tuple.FromTypes(substitutedParams, substitutedParams.Count);
    }

    /// <summary>
    /// Expands InstanceType&lt;T&gt; to extract the instance type of a constructor/class.
    /// Equivalent to: T extends abstract new (...args: any) => infer R ? R : never
    /// </summary>
    private TypeInfo ExpandInstanceType(TypeInfo classType)
    {
        // Handle type parameters - defer evaluation
        if (classType is TypeInfo.TypeParameter)
        {
            return new TypeInfo.ConditionalType(
                classType,
                new TypeInfo.Any(),
                new TypeInfo.InferredTypeParameter("R"),
                new TypeInfo.Never()
            );
        }

        // Handle unions - distribute
        if (classType is TypeInfo.Union union)
        {
            var instanceTypes = union.FlattenedTypes
                .Select(ExpandInstanceType)
                .Where(t => t is not TypeInfo.Never)
                .Distinct()
                .ToList();

            return instanceTypes.Count switch
            {
                0 => new TypeInfo.Never(),
                1 => instanceTypes[0],
                _ => new TypeInfo.Union(instanceTypes)
            };
        }

        // Extract instance type from class types
        return classType switch
        {
            TypeInfo.Class cls => new TypeInfo.Instance(cls),
            TypeInfo.MutableClass mc => new TypeInfo.Instance(mc.Freeze()),
            TypeInfo.GenericClass gc => new TypeInfo.Instance(gc),
            TypeInfo.InstantiatedGeneric ig => new TypeInfo.Instance(ig),
            _ => new TypeInfo.Never()
        };
    }

    /// <summary>
    /// Expands Awaited&lt;T&gt; to recursively unwrap Promise types.
    /// Equivalent to: T extends PromiseLike&lt;infer U&gt; ? Awaited&lt;U&gt; : T
    /// </summary>
    private TypeInfo ExpandAwaited(TypeInfo type)
    {
        // Handle type parameters - defer evaluation
        if (type is TypeInfo.TypeParameter)
        {
            return new TypeInfo.ConditionalType(
                type,
                new TypeInfo.Promise(new TypeInfo.Any()),
                new TypeInfo.InferredTypeParameter("U"),
                type
            );
        }

        // Handle unions - distribute
        if (type is TypeInfo.Union union)
        {
            var awaitedTypes = union.FlattenedTypes
                .Select(ExpandAwaited)
                .Distinct()
                .ToList();

            return awaitedTypes.Count switch
            {
                0 => new TypeInfo.Never(),
                1 => awaitedTypes[0],
                _ => new TypeInfo.Union(awaitedTypes)
            };
        }

        // Recursively unwrap Promise types
        return type switch
        {
            TypeInfo.Promise p => ExpandAwaited(p.ValueType),
            _ => type
        };
    }

    /// <summary>
    /// Expands NonNullable&lt;T&gt; to remove null and undefined from a type.
    /// Equivalent to: T extends null | undefined ? never : T
    /// </summary>
    private TypeInfo ExpandNonNullable(TypeInfo type)
    {
        // Handle type parameters - defer evaluation
        if (type is TypeInfo.TypeParameter)
        {
            return new TypeInfo.ConditionalType(
                type,
                new TypeInfo.Union([new TypeInfo.Null(), new TypeInfo.Undefined()]),
                new TypeInfo.Never(),
                type
            );
        }

        // Handle unions - filter out null and undefined
        if (type is TypeInfo.Union union)
        {
            var filtered = union.FlattenedTypes
                .Where(t => t is not TypeInfo.Null and not TypeInfo.Undefined)
                .ToList();

            return filtered.Count switch
            {
                0 => new TypeInfo.Never(),
                1 => filtered[0],
                _ => new TypeInfo.Union(filtered)
            };
        }

        // Single type - return never if null/undefined, otherwise return type
        return type switch
        {
            TypeInfo.Null => new TypeInfo.Never(),
            TypeInfo.Undefined => new TypeInfo.Never(),
            _ => type
        };
    }

    /// <summary>
    /// Expands Extract&lt;T, U&gt; to extract union members from T that are assignable to U.
    /// Equivalent to: T extends U ? T : never
    /// </summary>
    private TypeInfo ExpandExtract(TypeInfo type, TypeInfo constraint)
    {
        // Handle type parameters - defer evaluation
        if (type is TypeInfo.TypeParameter)
        {
            return new TypeInfo.ConditionalType(
                type,
                constraint,
                type,
                new TypeInfo.Never()
            );
        }

        // Handle unions - filter members assignable to constraint
        if (type is TypeInfo.Union union)
        {
            var extracted = union.FlattenedTypes
                .Where(t => IsCompatible(constraint, t))
                .ToList();

            return extracted.Count switch
            {
                0 => new TypeInfo.Never(),
                1 => extracted[0],
                _ => new TypeInfo.Union(extracted)
            };
        }

        // Single type - keep if assignable to constraint
        return IsCompatible(constraint, type) ? type : new TypeInfo.Never();
    }

    /// <summary>
    /// Expands Exclude&lt;T, U&gt; to remove union members from T that are assignable to U.
    /// Equivalent to: T extends U ? never : T
    /// </summary>
    private TypeInfo ExpandExclude(TypeInfo type, TypeInfo constraint)
    {
        // Handle type parameters - defer evaluation
        if (type is TypeInfo.TypeParameter)
        {
            return new TypeInfo.ConditionalType(
                type,
                constraint,
                new TypeInfo.Never(),
                type
            );
        }

        // Handle unions - filter out members assignable to constraint
        if (type is TypeInfo.Union union)
        {
            var remaining = union.FlattenedTypes
                .Where(t => !IsCompatible(constraint, t))
                .ToList();

            return remaining.Count switch
            {
                0 => new TypeInfo.Never(),
                1 => remaining[0],
                _ => new TypeInfo.Union(remaining)
            };
        }

        // Single type - remove if assignable to constraint
        return IsCompatible(constraint, type) ? new TypeInfo.Never() : type;
    }

    // ==================== UTILITY TYPE HELPERS ====================

    /// <summary>
    /// Extracts property names and their types from an object-like type.
    /// </summary>
    private Dictionary<string, TypeInfo> ExtractPropertiesWithTypes(TypeInfo type)
    {
        Dictionary<string, TypeInfo> props = [];

        switch (type)
        {
            case TypeInfo.Interface itf:
                foreach (var (name, propType) in itf.Members)
                    props[name] = propType;
                break;

            case TypeInfo.Record rec:
                foreach (var (name, propType) in rec.Fields)
                    props[name] = propType;
                break;

            case TypeInfo.Class cls:
                // Public fields
                foreach (var (name, propType) in cls.FieldTypes)
                {
                    if (cls.FieldAccess.GetValueOrDefault(name, Parsing.AccessModifier.Public) == Parsing.AccessModifier.Public)
                        props[name] = propType;
                }
                // Public getters (property accessors)
                foreach (var (name, propType) in cls.Getters)
                    props[name] = propType;
                break;

            case TypeInfo.Instance inst:
                return ExtractPropertiesWithTypes(inst.ClassType);

            case TypeInfo.InstantiatedGeneric ig:
                var expanded = ExpandInstantiatedGenericForKeyOf(ig);
                return ExtractPropertiesWithTypes(expanded);

            case TypeInfo.MappedType mapped:
                var expandedMapped = ExpandMappedType(mapped);
                return ExtractPropertiesWithTypes(expandedMapped);
        }

        return props;
    }

    /// <summary>
    /// Extracts the set of optional property names from an object-like type.
    /// </summary>
    private HashSet<string> ExtractOptionalProperties(TypeInfo type)
    {
        return type switch
        {
            TypeInfo.Interface itf => [.. itf.OptionalMembers],
            TypeInfo.MappedType mapped => ExtractOptionalProperties(ExpandMappedType(mapped)),
            TypeInfo.InstantiatedGeneric ig => ExtractOptionalProperties(ExpandInstantiatedGenericForKeyOf(ig)),
            _ => []
        };
    }

    /// <summary>
    /// Extracts key names from a key type (union of string literals, keyof result, etc.)
    /// </summary>
    private HashSet<string> ExtractKeyNames(TypeInfo keyType)
    {
        HashSet<string> keys = [];

        switch (keyType)
        {
            case TypeInfo.StringLiteral sl:
                keys.Add(sl.Value);
                break;

            case TypeInfo.Union union:
                foreach (var t in union.FlattenedTypes)
                {
                    if (t is TypeInfo.StringLiteral lit)
                        keys.Add(lit.Value);
                }
                break;

            case TypeInfo.KeyOf keyOf:
                var evaluated = EvaluateKeyOf(keyOf.SourceType);
                return ExtractKeyNames(evaluated);
        }

        return keys;
    }

    // ==================== CONDITIONAL TYPE EVALUATION ====================

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

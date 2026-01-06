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
            // Handle new mapped type constructs
            TypeInfo.KeyOf keyOf =>
                new TypeInfo.KeyOf(Substitute(keyOf.SourceType, substitutions)),
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
        var keys = new List<TypeInfo>();

        switch (type)
        {
            case TypeInfo.Interface itf:
                // Add explicit member keys as string literals
                foreach (var key in itf.Members.Keys)
                    keys.Add(new TypeInfo.StringLiteral(key));
                // Add index signature key types
                if (itf.StringIndexType != null)
                    keys.Add(new TypeInfo.Primitive(Parsing.TokenType.TYPE_STRING));
                if (itf.NumberIndexType != null)
                    keys.Add(new TypeInfo.Primitive(Parsing.TokenType.TYPE_NUMBER));
                if (itf.SymbolIndexType != null)
                    keys.Add(new TypeInfo.Symbol());
                break;

            case TypeInfo.Record rec:
                foreach (var key in rec.Fields.Keys)
                    keys.Add(new TypeInfo.StringLiteral(key));
                if (rec.StringIndexType != null)
                    keys.Add(new TypeInfo.Primitive(Parsing.TokenType.TYPE_STRING));
                if (rec.NumberIndexType != null)
                    keys.Add(new TypeInfo.Primitive(Parsing.TokenType.TYPE_NUMBER));
                if (rec.SymbolIndexType != null)
                    keys.Add(new TypeInfo.Symbol());
                break;

            case TypeInfo.Class cls:
                // Public methods
                foreach (var key in cls.Methods.Keys)
                {
                    if (cls.MethodAccessModifiers.GetValueOrDefault(key, Parsing.AccessModifier.Public) == Parsing.AccessModifier.Public)
                        keys.Add(new TypeInfo.StringLiteral(key));
                }
                // Public fields
                foreach (var key in cls.DeclaredFieldTypes.Keys)
                {
                    if (cls.FieldAccessModifiers.GetValueOrDefault(key, Parsing.AccessModifier.Public) == Parsing.AccessModifier.Public)
                        keys.Add(new TypeInfo.StringLiteral(key));
                }
                // Public getters
                foreach (var key in cls.GetterTypes.Keys)
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
                    if (gc.MethodAccessModifiers.GetValueOrDefault(key, Parsing.AccessModifier.Public) == Parsing.AccessModifier.Public)
                        keys.Add(new TypeInfo.StringLiteral(key));
                }
                foreach (var key in gc.DeclaredFieldTypes.Keys)
                {
                    if (gc.FieldAccessModifiers.GetValueOrDefault(key, Parsing.AccessModifier.Public) == Parsing.AccessModifier.Public)
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
                            keys.Add(new TypeInfo.Primitive(Parsing.TokenType.TYPE_STRING));
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
                keys.Add(new TypeInfo.Primitive(Parsing.TokenType.TYPE_STRING));
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
        var subs = new Dictionary<string, TypeInfo>();

        switch (ig.GenericDefinition)
        {
            case TypeInfo.GenericInterface gi:
                for (int i = 0; i < gi.TypeParams.Count && i < ig.TypeArguments.Count; i++)
                    subs[gi.TypeParams[i].Name] = ig.TypeArguments[i];

                var substitutedMembers = gi.Members.ToDictionary(
                    kvp => kvp.Key,
                    kvp => Substitute(kvp.Value, subs));
                return new TypeInfo.Interface(gi.Name, substitutedMembers, gi.OptionalMemberSet);

            case TypeInfo.GenericClass gc:
                for (int i = 0; i < gc.TypeParams.Count && i < ig.TypeArguments.Count; i++)
                    subs[gc.TypeParams[i].Name] = ig.TypeArguments[i];

                return new TypeInfo.Class(
                    gc.Name,
                    gc.Superclass,
                    gc.Methods.ToDictionary(kvp => kvp.Key, kvp => Substitute(kvp.Value, subs)),
                    gc.StaticMethods,
                    gc.StaticProperties,
                    gc.MethodAccess,
                    gc.FieldAccess,
                    gc.ReadonlyFields,
                    gc.Getters?.ToDictionary(kvp => kvp.Key, kvp => Substitute(kvp.Value, subs)),
                    gc.Setters?.ToDictionary(kvp => kvp.Key, kvp => Substitute(kvp.Value, subs)),
                    gc.FieldTypes?.ToDictionary(kvp => kvp.Key, kvp => Substitute(kvp.Value, subs)));
        }

        return ig;
    }

    /// <summary>
    /// Expands a mapped type to a concrete Record/Interface type.
    /// Called lazily when the mapped type is used in compatibility checks or property access.
    /// </summary>
    private TypeInfo ExpandMappedType(TypeInfo.MappedType mapped, Dictionary<string, TypeInfo>? outerSubstitutions = null)
    {
        outerSubstitutions ??= new Dictionary<string, TypeInfo>();

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
        var fields = new Dictionary<string, TypeInfo>();
        var optionalFields = new HashSet<string>();

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
            ? new TypeInfo.Interface("", fields, optionalFields)
            : new TypeInfo.Record(fields);
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
        if (indexType is TypeInfo.Primitive { Type: Parsing.TokenType.TYPE_STRING })
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
                               ?? cls.DeclaredFieldTypes.GetValueOrDefault(propertyName)
                               ?? cls.GetterTypes.GetValueOrDefault(propertyName),
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
        var subs = new Dictionary<string, TypeInfo>();

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
                if (gc.DeclaredFieldTypes.TryGetValue(propertyName, out var fieldType))
                    return Substitute(fieldType, subs);
                if (gc.GetterTypes.TryGetValue(propertyName, out var getterType))
                    return Substitute(getterType, subs);
                break;
        }

        return null;
    }
}

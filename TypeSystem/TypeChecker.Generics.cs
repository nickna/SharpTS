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
            throw new TypeCheckException($" Generic class '{generic.Name}' requires {generic.TypeParams.Count} type argument(s), got {typeArgs.Count}.");
        }

        // Validate constraints
        for (int i = 0; i < typeArgs.Count; i++)
        {
            var tp = generic.TypeParams[i];
            if (tp.Constraint != null && !IsCompatible(tp.Constraint, typeArgs[i]))
            {
                throw new TypeCheckException($" Type '{typeArgs[i]}' does not satisfy constraint '{tp.Constraint}' for type parameter '{tp.Name}'.");
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
            throw new TypeCheckException($" Generic interface '{generic.Name}' requires {generic.TypeParams.Count} type argument(s), got {typeArgs.Count}.");
        }

        // Validate constraints
        for (int i = 0; i < typeArgs.Count; i++)
        {
            var tp = generic.TypeParams[i];
            if (tp.Constraint != null && !IsCompatible(tp.Constraint, typeArgs[i]))
            {
                throw new TypeCheckException($" Type '{typeArgs[i]}' does not satisfy constraint '{tp.Constraint}' for type parameter '{tp.Name}'.");
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
            throw new TypeCheckException($" Generic function requires {generic.TypeParams.Count} type argument(s), got {typeArgs.Count}.");
        }

        // Validate constraints
        for (int i = 0; i < typeArgs.Count; i++)
        {
            var tp = generic.TypeParams[i];
            if (tp.Constraint != null && !IsCompatible(tp.Constraint, typeArgs[i]))
            {
                throw new TypeCheckException($" Type '{typeArgs[i]}' does not satisfy constraint '{tp.Constraint}' for type parameter '{tp.Name}'.");
            }
        }

        // Create substitution map
        Dictionary<string, TypeInfo> substitutions = [];
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
                // Validate constraint
                if (tp.Constraint != null && !IsCompatible(tp.Constraint, inferredType))
                {
                    throw new TypeCheckException($" Inferred type '{inferredType}' does not satisfy constraint '{tp.Constraint}' for type parameter '{tp.Name}'.");
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
}

using System.Collections.Frozen;

namespace SharpTS.TypeSystem;

/// <summary>
/// KeyOf evaluation, mapped type expansion, and indexed access resolution.
/// </summary>
/// <remarks>
/// Contains methods: EvaluateKeyOf, ExtractKeys, ExpandInstantiatedGenericForKeyOf,
/// ExpandMappedType, ApplyKeyRemapping, EvaluateStringManipulationType,
/// ResolveIndexedAccessTypes, ResolveIndexedAccess, GetPropertyType,
/// GetPropertyTypeFromInstantiatedGeneric.
/// </remarks>
public partial class TypeChecker
{
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
}

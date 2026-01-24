using System.Collections.Frozen;

namespace SharpTS.TypeSystem;

/// <summary>
/// Built-in utility type expansions (Partial, Required, Pick, Omit, etc.)
/// </summary>
/// <remarks>
/// Contains methods: ExpandPartial, ExpandRequired, ExpandReadonly, ExpandRecordType,
/// IsKeyTypeAssignableToString, ExpandPick, ExpandOmit, ExpandReturnType, ExpandParameters,
/// ExpandConstructorParameters, ExtractConstructorParams, SubstituteConstructorParams,
/// ExpandInstanceType, ExpandAwaited, ExpandNonNullable, ExpandExtract, ExpandExclude,
/// ExtractPropertiesWithTypes, ExtractOptionalProperties, ExtractKeyNames.
/// </remarks>
public partial class TypeChecker
{
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
}

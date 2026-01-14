using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Maps TypeScript types to .NET CLR types for IL compilation.
/// </summary>
/// <remarks>
/// Converts <see cref="TypeInfo"/> records and type annotation strings to .NET
/// <see cref="Type"/> instances. Primitives map directly (number→double, string→string,
/// boolean→bool). Complex types (arrays, functions, records, unions) map to object
/// since they use dynamic runtime representations. Used by <see cref="ILCompiler"/>
/// and <see cref="ILEmitter"/> for parameter/return type declarations.
/// </remarks>
/// <seealso cref="TypeInfo"/>
/// <seealso cref="ILCompiler"/>
public class TypeMapper
{
    private readonly ModuleBuilder _moduleBuilder;
    private readonly TypeProvider _types;
    private Dictionary<string, TypeBuilder>? _classBuilders;
    private UnionTypeGenerator? _unionGenerator;
    private readonly Dictionary<string, Type> _externalTypes = [];  // @DotNetType mappings

    public TypeMapper(ModuleBuilder moduleBuilder, TypeProvider? types = null)
    {
        _moduleBuilder = moduleBuilder;
        _types = types ?? TypeProvider.Runtime;
    }

    /// <summary>
    /// Registers an external .NET type for a TypeScript class name.
    /// Used for @DotNetType decorator support.
    /// </summary>
    public void RegisterExternalType(string typeScriptName, Type dotNetType)
    {
        _externalTypes[typeScriptName] = dotNetType;
    }

    /// <summary>
    /// Gets the external types dictionary for external access.
    /// </summary>
    public IReadOnlyDictionary<string, Type> ExternalTypes => _externalTypes;

    /// <summary>
    /// Gets the TypeProvider used for type resolution.
    /// </summary>
    public TypeProvider Types => _types;

    /// <summary>
    /// Sets the class builders dictionary for resolving TypeScript class types to their actual .NET types.
    /// Must be called before using MapTypeInfoStrict() with class types.
    /// </summary>
    public void SetClassBuilders(Dictionary<string, TypeBuilder> classBuilders)
    {
        _classBuilders = classBuilders;
    }

    /// <summary>
    /// Sets the union type generator for creating discriminated union types.
    /// Must be called before using MapTypeInfoStrict() with union types.
    /// </summary>
    public void SetUnionGenerator(UnionTypeGenerator unionGenerator)
    {
        _unionGenerator = unionGenerator;
    }

    public Type MapTypeInfo(TypeInfo typeInfo) => typeInfo switch
    {
        TypeInfo.Primitive p => MapPrimitive(p),
        TypeInfo.String => _types.String, // String type maps to System.String
        TypeInfo.BigInt => _types.BigInteger, // BigInt maps to BigInteger
        TypeInfo.Array => _types.Object, // Will be TSArray at runtime
        TypeInfo.Function => _types.Object, // Will be delegate at runtime
        TypeInfo.Promise p => MapPromiseType(p), // Promise<T> maps to Task<T>
        TypeInfo.Class c => GetClassType(c.Name),
        TypeInfo.Instance i => i.ClassType switch
        {
            TypeInfo.Class c => GetClassType(c.Name),
            TypeInfo.InstantiatedGeneric => _types.Object,
            _ => _types.Object
        },
        TypeInfo.Record => _types.Object, // Will be TSObject at runtime
        TypeInfo.Void => _types.Void,
        TypeInfo.Any => _types.Object,
        TypeInfo.Union => _types.Object, // Union types are dynamic at runtime
        TypeInfo.Intersection => _types.Object, // Intersection types are dynamic at runtime
        TypeInfo.Null => _types.Object, // Null maps to object
        TypeInfo.Unknown => _types.Object, // Unknown is dynamic at runtime
        TypeInfo.Never => _types.Void, // Never represents no return
        // Generic types erase to object at runtime (type checking is compile-time only)
        TypeInfo.TypeParameter => _types.Object,
        TypeInfo.GenericClass => _types.Object,
        TypeInfo.GenericFunction => _types.Object,
        TypeInfo.GenericInterface => _types.Object,
        TypeInfo.InstantiatedGeneric => _types.Object,
        // Conditional types should be fully evaluated during type checking
        // If they reach IL emission, fall back to object
        TypeInfo.ConditionalType => _types.Object,
        TypeInfo.InferredTypeParameter => _types.Object,
        _ => _types.Object
    };

    private Type MapPromiseType(TypeInfo.Promise promise)
    {
        Type innerType = MapTypeInfo(promise.ValueType);
        if (_types.IsVoid(innerType))
            return _types.Task;
        return _types.MakeGenericType(_types.TaskOpen, innerType);
    }

    private Type MapPrimitive(TypeInfo.Primitive p) => p.Type switch
    {
        TokenType.TYPE_NUMBER => _types.Double,
        TokenType.TYPE_STRING => _types.String,
        TokenType.TYPE_BOOLEAN => _types.Boolean,
        _ => _types.Object
    };

    /// <summary>
    /// Gets the .NET type for a TypeScript class, resolving to the actual TypeBuilder if available.
    /// </summary>
    public Type GetClassType(string className)
    {
        // Check external types first (from @DotNetType)
        if (_externalTypes.TryGetValue(className, out var externalType))
            return externalType;

        // Then check TypeScript class builders
        if (_classBuilders != null && _classBuilders.TryGetValue(className, out var typeBuilder))
            return typeBuilder;

        return _types.Object;
    }

    /// <summary>
    /// Maps TypeScript types to .NET types with strict type resolution.
    /// Unlike MapTypeInfo(), this method resolves class types to their actual TypeBuilders
    /// and generates discriminated union types instead of falling back to object.
    /// </summary>
    /// <remarks>
    /// Use this method for typed interop scenarios where you need actual .NET types
    /// for method signatures, property types, and field types.
    /// </remarks>
    public Type MapTypeInfoStrict(TypeInfo typeInfo) => typeInfo switch
    {
        TypeInfo.Primitive p => MapPrimitive(p),
        TypeInfo.String => _types.String, // String type maps to System.String
        TypeInfo.BigInt => _types.BigInteger,
        TypeInfo.Array arr => MapArrayTypeStrict(arr),
        TypeInfo.Function => _types.Delegate, // Functions map to Delegate for typed interop
        TypeInfo.Promise p => MapPromiseTypeStrict(p),
        TypeInfo.Class c => GetClassType(c.Name),
        TypeInfo.Instance i => MapInstanceTypeStrict(i),
        TypeInfo.Record => _types.Object, // Records remain dynamic objects
        TypeInfo.Void => _types.Void,
        TypeInfo.Any => _types.Object,
        TypeInfo.Union u => MapUnionTypeStrict(u),
        TypeInfo.Intersection => _types.Object, // Intersections are complex, fall back to object
        TypeInfo.Null => _types.Object,
        TypeInfo.Unknown => _types.Object,
        TypeInfo.Never => _types.Void,
        TypeInfo.Map m => MapMapTypeStrict(m),
        TypeInfo.Set s => MapSetTypeStrict(s),
        TypeInfo.WeakMap => _types.Object, // WeakMap is opaque in .NET interop
        TypeInfo.WeakSet => _types.Object, // WeakSet is opaque in .NET interop
        TypeInfo.Date => _types.DateTime,
        TypeInfo.RegExp => _types.Regex,
        TypeInfo.Symbol => _types.String, // Symbols map to string keys
        // Generic types - attempt to resolve if instantiated
        TypeInfo.TypeParameter => _types.Object,
        TypeInfo.GenericClass gc => GetClassType(gc.Name),
        TypeInfo.GenericFunction => _types.Delegate,
        TypeInfo.GenericInterface => _types.Object,
        TypeInfo.InstantiatedGeneric ig => MapInstantiatedGenericStrict(ig),
        // Conditional types should be evaluated during type checking
        TypeInfo.ConditionalType => _types.Object,
        TypeInfo.InferredTypeParameter => _types.Object,
        _ => _types.Object
    };

    private Type MapInstanceTypeStrict(TypeInfo.Instance instance) => instance.ClassType switch
    {
        TypeInfo.Class c => GetClassType(c.Name),
        TypeInfo.GenericClass gc => GetClassType(gc.Name),
        TypeInfo.InstantiatedGeneric ig => MapInstantiatedGenericStrict(ig),
        _ => _types.Object
    };

    private Type MapInstantiatedGenericStrict(TypeInfo.InstantiatedGeneric ig)
    {
        // For instantiated generics, try to resolve the base type
        if (ig.GenericDefinition is TypeInfo.GenericClass gc)
            return GetClassType(gc.Name);
        return _types.Object;
    }

    private Type MapArrayTypeStrict(TypeInfo.Array arr)
    {
        Type elementType = MapTypeInfoStrict(arr.ElementType);
        // Use List<T> for typed arrays
        return _types.MakeGenericType(_types.ListOpen, elementType);
    }

    private Type MapMapTypeStrict(TypeInfo.Map map)
    {
        Type keyType = MapTypeInfoStrict(map.KeyType);
        Type valueType = MapTypeInfoStrict(map.ValueType);
        return _types.MakeGenericType(_types.DictionaryOpen, keyType, valueType);
    }

    private Type MapSetTypeStrict(TypeInfo.Set set)
    {
        Type elementType = MapTypeInfoStrict(set.ElementType);
        return _types.MakeGenericType(_types.HashSetOpen, elementType);
    }

    private Type MapPromiseTypeStrict(TypeInfo.Promise promise)
    {
        Type innerType = MapTypeInfoStrict(promise.ValueType);
        if (_types.IsVoid(innerType))
            return _types.Task;
        return _types.MakeGenericType(_types.TaskOpen, innerType);
    }

    private Type MapUnionTypeStrict(TypeInfo.Union union)
    {
        var types = union.FlattenedTypes;

        // Special case: T | null for value types → Nullable<T>
        if (types.Count == 2)
        {
            var nullType = types.FirstOrDefault(t => t is TypeInfo.Null);
            if (nullType != null)
            {
                var nonNullType = types.First(t => t is not TypeInfo.Null);
                var mapped = MapTypeInfoStrict(nonNullType);
                if (mapped.IsValueType && !_types.IsVoid(mapped))
                    return _types.MakeNullable(mapped);
                // Reference types are already nullable
                return mapped;
            }
        }

        // Special case: single type after flattening
        if (types.Count == 1)
            return MapTypeInfoStrict(types[0]);

        // Generate discriminated union type
        if (_unionGenerator != null)
            return _unionGenerator.GetOrCreateUnionType(union, _moduleBuilder);

        // Fallback to object if no union generator configured
        return _types.Object;
    }

    public static Type GetClrType(string typeAnnotation) => typeAnnotation switch
    {
        "number" => typeof(double),
        "string" => typeof(string),
        "boolean" => typeof(bool),
        "bigint" => typeof(System.Numerics.BigInteger),
        "void" => typeof(void),
        "any" => typeof(object),
        "unknown" => typeof(object),
        "never" => typeof(void),
        _ when typeAnnotation.EndsWith("[]") => typeof(object), // Array type
        _ when typeAnnotation.StartsWith("Promise<") => GetPromiseClrType(typeAnnotation),
        _ => typeof(object) // Class or interface type
    };

    private static Type GetPromiseClrType(string typeAnnotation)
    {
        // Extract inner type from Promise<T>
        string inner = typeAnnotation.Substring(8, typeAnnotation.Length - 9);
        if (inner == "void")
            return typeof(Task);
        Type innerType = GetClrType(inner);
        if (innerType == typeof(void))
            return typeof(Task);
        return typeof(Task<>).MakeGenericType(innerType);
    }
}

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
    private Dictionary<string, TypeBuilder>? _classBuilders;
    private UnionTypeGenerator? _unionGenerator;

    public TypeMapper(ModuleBuilder moduleBuilder)
    {
        _moduleBuilder = moduleBuilder;
    }

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
        TypeInfo.BigInt => typeof(System.Numerics.BigInteger), // BigInt maps to BigInteger
        TypeInfo.Array => typeof(object), // Will be TSArray at runtime
        TypeInfo.Function => typeof(object), // Will be delegate at runtime
        TypeInfo.Promise p => MapPromiseType(p), // Promise<T> maps to Task<T>
        TypeInfo.Class c => GetClassType(c.Name),
        TypeInfo.Instance i => i.ClassType switch
        {
            TypeInfo.Class c => GetClassType(c.Name),
            TypeInfo.InstantiatedGeneric => typeof(object),
            _ => typeof(object)
        },
        TypeInfo.Record => typeof(object), // Will be TSObject at runtime
        TypeInfo.Void => typeof(void),
        TypeInfo.Any => typeof(object),
        TypeInfo.Union => typeof(object), // Union types are dynamic at runtime
        TypeInfo.Intersection => typeof(object), // Intersection types are dynamic at runtime
        TypeInfo.Null => typeof(object), // Null maps to object
        TypeInfo.Unknown => typeof(object), // Unknown is dynamic at runtime
        TypeInfo.Never => typeof(void), // Never represents no return
        // Generic types erase to object at runtime (type checking is compile-time only)
        TypeInfo.TypeParameter => typeof(object),
        TypeInfo.GenericClass => typeof(object),
        TypeInfo.GenericFunction => typeof(object),
        TypeInfo.GenericInterface => typeof(object),
        TypeInfo.InstantiatedGeneric => typeof(object),
        _ => typeof(object)
    };

    private Type MapPromiseType(TypeInfo.Promise promise)
    {
        Type innerType = MapTypeInfo(promise.ValueType);
        if (innerType == typeof(void))
            return typeof(Task);
        return typeof(Task<>).MakeGenericType(innerType);
    }

    private static Type MapPrimitive(TypeInfo.Primitive p) => p.Type switch
    {
        TokenType.TYPE_NUMBER => typeof(double),
        TokenType.TYPE_STRING => typeof(string),
        TokenType.TYPE_BOOLEAN => typeof(bool),
        _ => typeof(object)
    };

    /// <summary>
    /// Gets the .NET type for a TypeScript class, resolving to the actual TypeBuilder if available.
    /// </summary>
    public Type GetClassType(string className)
    {
        if (_classBuilders != null && _classBuilders.TryGetValue(className, out var typeBuilder))
            return typeBuilder;
        return typeof(object);
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
        TypeInfo.BigInt => typeof(System.Numerics.BigInteger),
        TypeInfo.Array arr => MapArrayTypeStrict(arr),
        TypeInfo.Function => typeof(Delegate), // Functions map to Delegate for typed interop
        TypeInfo.Promise p => MapPromiseTypeStrict(p),
        TypeInfo.Class c => GetClassType(c.Name),
        TypeInfo.Instance i => MapInstanceTypeStrict(i),
        TypeInfo.Record => typeof(object), // Records remain dynamic objects
        TypeInfo.Void => typeof(void),
        TypeInfo.Any => typeof(object),
        TypeInfo.Union u => MapUnionTypeStrict(u),
        TypeInfo.Intersection => typeof(object), // Intersections are complex, fall back to object
        TypeInfo.Null => typeof(object),
        TypeInfo.Unknown => typeof(object),
        TypeInfo.Never => typeof(void),
        TypeInfo.Map m => MapMapTypeStrict(m),
        TypeInfo.Set s => MapSetTypeStrict(s),
        TypeInfo.Date => typeof(DateTime),
        TypeInfo.RegExp => typeof(System.Text.RegularExpressions.Regex),
        TypeInfo.Symbol => typeof(string), // Symbols map to string keys
        // Generic types - attempt to resolve if instantiated
        TypeInfo.TypeParameter => typeof(object),
        TypeInfo.GenericClass gc => GetClassType(gc.Name),
        TypeInfo.GenericFunction => typeof(Delegate),
        TypeInfo.GenericInterface => typeof(object),
        TypeInfo.InstantiatedGeneric ig => MapInstantiatedGenericStrict(ig),
        _ => typeof(object)
    };

    private Type MapInstanceTypeStrict(TypeInfo.Instance instance) => instance.ClassType switch
    {
        TypeInfo.Class c => GetClassType(c.Name),
        TypeInfo.GenericClass gc => GetClassType(gc.Name),
        TypeInfo.InstantiatedGeneric ig => MapInstantiatedGenericStrict(ig),
        _ => typeof(object)
    };

    private Type MapInstantiatedGenericStrict(TypeInfo.InstantiatedGeneric ig)
    {
        // For instantiated generics, try to resolve the base type
        if (ig.GenericDefinition is TypeInfo.GenericClass gc)
            return GetClassType(gc.Name);
        return typeof(object);
    }

    private Type MapArrayTypeStrict(TypeInfo.Array arr)
    {
        Type elementType = MapTypeInfoStrict(arr.ElementType);
        // Use List<T> for typed arrays
        return typeof(List<>).MakeGenericType(elementType);
    }

    private Type MapMapTypeStrict(TypeInfo.Map map)
    {
        Type keyType = MapTypeInfoStrict(map.KeyType);
        Type valueType = MapTypeInfoStrict(map.ValueType);
        return typeof(Dictionary<,>).MakeGenericType(keyType, valueType);
    }

    private Type MapSetTypeStrict(TypeInfo.Set set)
    {
        Type elementType = MapTypeInfoStrict(set.ElementType);
        return typeof(HashSet<>).MakeGenericType(elementType);
    }

    private Type MapPromiseTypeStrict(TypeInfo.Promise promise)
    {
        Type innerType = MapTypeInfoStrict(promise.ValueType);
        if (innerType == typeof(void))
            return typeof(Task);
        return typeof(Task<>).MakeGenericType(innerType);
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
                if (mapped.IsValueType && mapped != typeof(void))
                    return typeof(Nullable<>).MakeGenericType(mapped);
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
        return typeof(object);
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

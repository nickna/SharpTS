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
    // @DotNetOverload hints keyed by .NET type, then by TS method name ("constructor" for ctors).
    private readonly Dictionary<Type, Dictionary<string, string>> _externalOverloadHints = [];

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
    /// Associates <c>@DotNetOverload</c> hints with an external .NET type. Hints are
    /// keyed by TS method name (or <c>"constructor"</c>). Subsequent calls for the
    /// same type merge into the existing map.
    /// </summary>
    public void RegisterOverloadHints(Type dotNetType, IReadOnlyDictionary<string, string> hints)
    {
        if (hints.Count == 0) return;
        if (!_externalOverloadHints.TryGetValue(dotNetType, out var map))
        {
            map = new Dictionary<string, string>(StringComparer.Ordinal);
            _externalOverloadHints[dotNetType] = map;
        }
        foreach (var kv in hints)
        {
            map[kv.Key] = kv.Value;
        }
    }

    /// <summary>
    /// Returns the <c>@DotNetOverload</c> hint for a given method on an external type,
    /// or null if none was declared.
    /// </summary>
    public string? GetOverloadHint(Type dotNetType, string methodName)
    {
        if (_externalOverloadHints.TryGetValue(dotNetType, out var map) &&
            map.TryGetValue(methodName, out var hint))
        {
            return hint;
        }
        return null;
    }

    /// <summary>
    /// Gets the TypeProvider used for type resolution.
    /// </summary>
    public TypeProvider Types => _types;

    /// <summary>
    /// The module builder that owns all emitted types. Exposed so per-feature emitters
    /// (e.g. <see cref="DelegateAdapterEmitter"/>) can define new nested types alongside
    /// user classes and the <c>$Runtime</c> helpers.
    /// </summary>
    public ModuleBuilder ModuleBuilder => _moduleBuilder;

    private EmittedRuntime? _runtime;
    private DelegateAdapterEmitter? _delegateAdapters;

    /// <summary>
    /// Called by <see cref="ILCompiler"/> once the runtime has been emitted. Enables
    /// features like <see cref="DelegateAdapters"/> that need <c>$TSFunction</c> and
    /// other emitted builders.
    /// </summary>
    public void SetRuntime(EmittedRuntime runtime) => _runtime = runtime;

    /// <summary>
    /// Per-compilation cache of TS-closure-to-.NET-delegate adapter types. Lazily
    /// constructed on first access. Each unique delegate signature gets one adapter
    /// class emitted into the module; subsequent uses reuse the same class.
    /// </summary>
    public DelegateAdapterEmitter DelegateAdapters
    {
        get
        {
            if (_runtime == null)
            {
                throw new InvalidOperationException(
                    "TypeMapper.DelegateAdapters accessed before SetRuntime was called.");
            }
            return _delegateAdapters ??= new DelegateAdapterEmitter(_moduleBuilder, _runtime, _types);
        }
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
        TypeInfo.String => _types.String, // String type maps to System.String
        TypeInfo.BigInt or TypeInfo.BigIntLiteral => _types.BigInteger, // BigInt maps to BigInteger
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
        TypeInfo.Object => _types.Object, // object type maps to System.Object
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
        // Type predicates: "x is T" returns bool, "asserts x is T" returns void
        TypeInfo.TypePredicate pred => pred.IsAssertion ? _types.Void : _types.Boolean,
        TypeInfo.AssertsNonNull => _types.Void, // "asserts x" returns void
        _ => _types.Object
    };

    private Type MapPromiseType(TypeInfo.Promise promise)
    {
        Type innerType = MapTypeInfo(promise.ValueType);
        if (_types.IsVoid(innerType))
            return _types.Task;
        return _types.MakeGenericType(_types.TaskOpen, innerType);
    }

    private Type MapPrimitive(TypeInfo.Primitive p) =>
        PrimitiveTypeMappings.TokenToClrType.TryGetValue(p.Type, out var clrType)
            ? clrType
            : _types.Object;

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
        TypeInfo.StringLiteral => _types.String, // "foo" literal widens to string
        TypeInfo.NumberLiteral => _types.Double, // 42 literal widens to number
        TypeInfo.BooleanLiteral => _types.Boolean, // true/false literal widens to boolean
        TypeInfo.BigInt or TypeInfo.BigIntLiteral => _types.BigInteger, // 1n literal widens to bigint
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
        TypeInfo.Object => _types.Object, // object type maps to System.Object
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
        // Type predicates: "x is T" returns bool, "asserts x is T" returns void
        TypeInfo.TypePredicate pred => pred.IsAssertion ? _types.Void : _types.Boolean,
        TypeInfo.AssertsNonNull => _types.Void, // "asserts x" returns void
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
        Type elementType = MapCollectionElementStrict(arr.ElementType);
        // Use List<T> for typed arrays
        return _types.MakeGenericType(_types.ListOpen, elementType);
    }

    private Type MapMapTypeStrict(TypeInfo.Map map)
    {
        Type keyType = MapCollectionElementStrict(map.KeyType);
        Type valueType = MapCollectionElementStrict(map.ValueType);
        return _types.MakeGenericType(_types.DictionaryOpen, keyType, valueType);
    }

    private Type MapSetTypeStrict(TypeInfo.Set set)
    {
        Type elementType = MapCollectionElementStrict(set.ElementType);
        return _types.MakeGenericType(_types.HashSetOpen, elementType);
    }

    /// <summary>
    /// Maps a collection element/key/value type for a strict <c>List&lt;T&gt;</c> / <c>Dictionary&lt;,&gt;</c>
    /// / <c>HashSet&lt;T&gt;</c> slot, substituting <c>object</c> when the element maps to <c>System.Void</c>.
    /// A <c>never</c>/<c>void</c> element (e.g. an empty generator's <c>never</c> yield type spread into an
    /// array — <c>function* g() {}</c> then <c>[...g()]</c>) would otherwise make <c>MakeGenericType</c>
    /// throw "System.Void may not be used as a type argument", and such a collection is an empty dynamic
    /// <c>$Array</c>/<c>TSMap</c>/<c>TSSet</c> carried as <c>object</c> at runtime regardless (#548).
    /// </summary>
    private Type MapCollectionElementStrict(TypeInfo element)
    {
        Type mapped = MapTypeInfoStrict(element);
        if (_types.IsVoid(mapped))
            return _types.Object;
        // A generated Union_* struct as a List/Dictionary/HashSet element makes the closed
        // generic a TypeBuilderInstantiation. Most reflection members on such a type throw
        // NotSupportedException "Specified method is not supported." — e.g. ResolveReturnType's
        // IsSubclassOf(Delegate) probe (ParameterTypeResolver.cs) fires before its
        // IsDynamicRuntimeType→object fallback can collapse it. At runtime such a collection is a
        // dynamic $Array/$Map/$Set of boxed objects — never an instance of the union struct — so
        // object is the sound backing element type, matching the IsDynamicRuntimeType→object slot
        // precedent. Covers both collection-member (number[]|number) and scalar-member
        // (number|string) unions used as collection elements. (#954)
        if (UnionTypeHelper.IsUnionType(mapped))
            return _types.Object;
        return mapped;
    }

    /// <summary>
    /// Reports whether <paramref name="mappedType"/> is one of the CLR collection types that
    /// <see cref="MapTypeInfoStrict"/> produces for a typed array/map/set
    /// (<c>List&lt;T&gt;</c>, <c>Dictionary&lt;,&gt;</c>, <c>HashSet&lt;T&gt;</c>). The runtime
    /// representation of those TypeScript values is a dynamic <c>$Array</c>/<c>TSMap</c>/<c>TSSet</c>
    /// carried as <see cref="object"/>, not an instance of the declared collection. A declared slot
    /// of this CLR type (e.g. a method return) is therefore not assignable from the runtime value,
    /// so callers must fall back to <see cref="object"/>. (#278)
    /// </summary>
    public bool IsDynamicRuntimeCollection(Type mappedType)
    {
        if (!mappedType.IsGenericType)
            return false;
        Type def = mappedType.GetGenericTypeDefinition();
        return def == _types.ListOpen || def == _types.DictionaryOpen || def == _types.HashSetOpen;
    }

    /// <summary>
    /// Reports whether <paramref name="mappedType"/> is a CLR type that <see cref="MapTypeInfoStrict"/>
    /// produces but whose TypeScript runtime value is carried dynamically as <see cref="object"/> — a
    /// <c>$TSDate</c>/<c>$RegExp</c>/<c>$Array</c>/<c>$Map</c>/<c>$Set</c>, not an instance of that CLR
    /// type. Extends <see cref="IsDynamicRuntimeCollection"/> (the <c>List&lt;T&gt;</c>/
    /// <c>Dictionary&lt;,&gt;</c>/<c>HashSet&lt;T&gt;</c> mappings for array/Map/Set) with the
    /// non-collection BCL mappings <c>DateTime</c> (Date) and <c>Regex</c> (RegExp). A parameter or
    /// return slot of such a type is not assignable from the runtime value: it fails strict ILVerify
    /// with StackUnexpected and a castclass at the call/return site throws InvalidCastException, so
    /// callers fall back to <see cref="object"/>. (#278, #573)
    /// </summary>
    public bool IsDynamicRuntimeType(Type mappedType) =>
        mappedType == _types.DateTime ||
        mappedType == _types.Regex ||
        IsDynamicRuntimeCollection(mappedType);

    private Type MapPromiseTypeStrict(TypeInfo.Promise promise)
    {
        Type innerType = MapTypeInfoStrict(promise.ValueType);
        if (_types.IsVoid(innerType))
            return _types.Task;
        // Promise<number|string> would otherwise produce Task<Union_*> — a TypeBuilderInstantiation
        // that throws on the same reflection probes as List<Union_*> (see MapCollectionElementStrict).
        // Collapse the union inner to object so the closed generic stays a RuntimeType. (#954)
        if (UnionTypeHelper.IsUnionType(innerType))
            innerType = _types.Object;
        return _types.MakeGenericType(_types.TaskOpen, innerType);
    }

    private Type MapUnionTypeStrict(TypeInfo.Union union)
    {
        var types = union.FlattenedTypes;

        // Special case: T | null or T | undefined for value types → Nullable<T>
        // For reference types, they're already nullable (can hold $Undefined.Instance)
        if (types.Count == 2)
        {
            var nullishType = types.FirstOrDefault(t => t is TypeInfo.Null or TypeInfo.Undefined);
            if (nullishType != null)
            {
                var nonNullishType = types.First(t => t is not TypeInfo.Null and not TypeInfo.Undefined);
                var mapped = MapTypeInfoStrict(nonNullishType);
                if (mapped.IsValueType && !_types.IsVoid(mapped))
                    return _types.MakeNullable(mapped);
                // Reference types are already nullable
                return mapped;
            }
        }

        // Special case: single type after flattening
        if (types.Count == 1)
            return MapTypeInfoStrict(types[0]);

        // Collapse unions where all members resolve to the same .NET type.
        // e.g., "yes" | "no" → string, 1 | 2 | 3 → double, true | false → bool
        if (types.Count >= 2)
        {
            var firstMapped = MapTypeInfoStrict(types[0]);
            bool allSame = true;
            for (int i = 1; i < types.Count; i++)
            {
                if (MapTypeInfoStrict(types[i]) != firstMapped)
                {
                    allSame = false;
                    break;
                }
            }
            if (allSame && firstMapped != _types.Object)
                return firstMapped;
        }

        // Generate discriminated union type
        if (_unionGenerator != null)
            return _unionGenerator.GetOrCreateUnionType(union, _moduleBuilder);

        // Fallback to object if no union generator configured
        return _types.Object;
    }

    public static Type GetClrType(string typeAnnotation)
    {
        // Delegate primitive type resolution to centralized mappings
        if (PrimitiveTypeMappings.StringToClrType.TryGetValue(typeAnnotation, out var clrType))
            return clrType;

        // Handle array types
        if (typeAnnotation.EndsWith("[]"))
            return typeof(object);

        // Handle Promise types
        if (typeAnnotation.StartsWith("Promise<"))
            return GetPromiseClrType(typeAnnotation);

        // Class or interface type - fallback to object
        return typeof(object);
    }

    private static Type GetPromiseClrType(string typeAnnotation)
    {
        // Extract inner type from Promise<T>
        string inner = typeAnnotation.Substring(8, typeAnnotation.Length - 9);
        if (inner == "void")
            return typeof(Task);
        Type innerType = GetClrType(inner);
        if (innerType == typeof(void))
            return typeof(Task);
        return EmitGenerics.MakeGenericType(typeof(Task<>), innerType);
    }
}

using SharpTS.Parsing;
using SharpTS.Runtime.Types;

namespace SharpTS.TypeSystem;

/// <summary>
/// Resolves TypeInfo or runtime objects to their TypeCategory.
/// Single source of truth for type classification across TypeChecker, Interpreter, and ILEmitter.
/// </summary>
public static class TypeCategoryResolver
{
    /// <summary>
    /// Classifies a TypeInfo (for TypeChecker and ILEmitter).
    /// </summary>
    public static TypeCategory Classify(TypeInfo type) => type switch
    {
        // Primitives
        TypeInfo.String or TypeInfo.StringLiteral => TypeCategory.String,
        TypeInfo.Primitive p when p.Type == TokenType.TYPE_NUMBER => TypeCategory.Number,
        TypeInfo.NumberLiteral => TypeCategory.Number,
        TypeInfo.Primitive p when p.Type == TokenType.TYPE_BOOLEAN => TypeCategory.Boolean,
        TypeInfo.BooleanLiteral => TypeCategory.Boolean,
        TypeInfo.BigInt => TypeCategory.BigInt,
        TypeInfo.Symbol or TypeInfo.UniqueSymbol => TypeCategory.Symbol,

        // Built-in object types
        TypeInfo.Array => TypeCategory.Array,
        TypeInfo.Tuple => TypeCategory.Tuple,
        TypeInfo.Map => TypeCategory.Map,
        TypeInfo.Set => TypeCategory.Set,
        TypeInfo.WeakMap => TypeCategory.WeakMap,
        TypeInfo.WeakSet => TypeCategory.WeakSet,
        TypeInfo.Date => TypeCategory.Date,
        TypeInfo.RegExp => TypeCategory.RegExp,
        TypeInfo.Error => TypeCategory.Error,
        TypeInfo.Promise => TypeCategory.Promise,
        TypeInfo.Timeout => TypeCategory.Timeout,
        TypeInfo.Buffer => TypeCategory.Buffer,
        TypeInfo.Iterator => TypeCategory.Iterator,
        TypeInfo.Generator => TypeCategory.Generator,
        TypeInfo.AsyncGenerator => TypeCategory.AsyncGenerator,

        // User-defined types
        TypeInfo.Class or TypeInfo.GenericClass or TypeInfo.MutableClass => TypeCategory.Class,
        TypeInfo.Instance => TypeCategory.Instance,
        TypeInfo.Interface or TypeInfo.GenericInterface => TypeCategory.Interface,
        TypeInfo.Record => TypeCategory.Record,
        TypeInfo.Enum => TypeCategory.Enum,
        TypeInfo.Namespace => TypeCategory.Namespace,

        // Instantiated generics - determine based on the generic definition
        TypeInfo.InstantiatedGeneric ig => ClassifyInstantiatedGeneric(ig),

        // Special types
        TypeInfo.TypeParameter => TypeCategory.TypeParameter,
        TypeInfo.Union => TypeCategory.Union,
        TypeInfo.Intersection => TypeCategory.Intersection,
        TypeInfo.Function or TypeInfo.GenericFunction or TypeInfo.OverloadedFunction => TypeCategory.Function,
        TypeInfo.Any => TypeCategory.Any,
        TypeInfo.Unknown => TypeCategory.Unknown,
        TypeInfo.Never => TypeCategory.Never,
        TypeInfo.Void => TypeCategory.Void,
        TypeInfo.Null => TypeCategory.Null,
        TypeInfo.Undefined => TypeCategory.Undefined,
        TypeInfo.ExternalDotNetType => TypeCategory.External,

        // Default fallback
        _ => TypeCategory.Unknown
    };

    /// <summary>
    /// Classifies an instantiated generic type based on its definition.
    /// </summary>
    private static TypeCategory ClassifyInstantiatedGeneric(TypeInfo.InstantiatedGeneric ig) =>
        ig.GenericDefinition switch
        {
            TypeInfo.GenericClass => TypeCategory.Class,
            TypeInfo.GenericInterface => TypeCategory.Interface,
            TypeInfo.GenericFunction => TypeCategory.Function,
            _ => TypeCategory.Unknown
        };

    /// <summary>
    /// Classifies a runtime object (for Interpreter).
    /// </summary>
    public static TypeCategory ClassifyRuntime(object? obj) => obj switch
    {
        null => TypeCategory.Null,
        SharpTSUndefined => TypeCategory.Undefined,

        // Primitives (C# types that map to TS primitives)
        string => TypeCategory.String,
        double or int or long or float => TypeCategory.Number,
        bool => TypeCategory.Boolean,
        SharpTSBigInt => TypeCategory.BigInt,
        SharpTSSymbol => TypeCategory.Symbol,

        // Built-in object types
        SharpTSArray => TypeCategory.Array,
        SharpTSMap => TypeCategory.Map,
        SharpTSSet => TypeCategory.Set,
        SharpTSWeakMap => TypeCategory.WeakMap,
        SharpTSWeakSet => TypeCategory.WeakSet,
        SharpTSDate => TypeCategory.Date,
        SharpTSRegExp => TypeCategory.RegExp,
        SharpTSError => TypeCategory.Error,
        SharpTSPromise => TypeCategory.Promise,
        SharpTSTimeout => TypeCategory.Timeout,
        SharpTSBuffer => TypeCategory.Buffer,
        SharpTSIterator => TypeCategory.Iterator,
        SharpTSGenerator => TypeCategory.Generator,
        SharpTSAsyncGenerator => TypeCategory.AsyncGenerator,

        // User-defined types
        SharpTSClass => TypeCategory.Class,
        SharpTSInstance => TypeCategory.Instance,
        SharpTSObject => TypeCategory.Record,
        SharpTSEnum => TypeCategory.Enum,
        ConstEnumValues => TypeCategory.Enum,
        SharpTSNamespace => TypeCategory.Namespace,

        // Function types
        SharpTSFunction or SharpTSArrowFunction or SharpTSAsyncFunction => TypeCategory.Function,
        SharpTSGeneratorFunction or SharpTSAsyncGeneratorFunction => TypeCategory.Function,

        // Default fallback
        _ => TypeCategory.Unknown
    };

    /// <summary>
    /// Checks if a category represents a built-in type with explicit member validation.
    /// Only returns true for types that have BuiltInTypes.GetXxxMemberType() resolution.
    /// Types like Promise, Iterator, Generator return false to allow fallback to Any.
    /// </summary>
    public static bool HasBuiltInMemberValidation(TypeCategory category) => category switch
    {
        TypeCategory.String => true,
        TypeCategory.Array => true,
        TypeCategory.Tuple => true,
        TypeCategory.Map => true,
        TypeCategory.Set => true,
        TypeCategory.WeakMap => true,
        TypeCategory.WeakSet => true,
        TypeCategory.Date => true,
        TypeCategory.RegExp => true,
        TypeCategory.Error => true,
        TypeCategory.Timeout => true,
        TypeCategory.Buffer => true,
        TypeCategory.Function => true,
        _ => false
    };

    /// <summary>
    /// Checks if a category represents a built-in type (for runtime dispatch).
    /// </summary>
    public static bool IsBuiltInCategory(TypeCategory category) => category switch
    {
        TypeCategory.String => true,
        TypeCategory.Array => true,
        TypeCategory.Tuple => true,
        TypeCategory.Map => true,
        TypeCategory.Set => true,
        TypeCategory.WeakMap => true,
        TypeCategory.WeakSet => true,
        TypeCategory.Date => true,
        TypeCategory.RegExp => true,
        TypeCategory.Error => true,
        TypeCategory.Promise => true,
        TypeCategory.Timeout => true,
        TypeCategory.Buffer => true,
        TypeCategory.Iterator => true,
        TypeCategory.Generator => true,
        TypeCategory.AsyncGenerator => true,
        _ => false
    };

    /// <summary>
    /// Checks if a category represents a user-defined type.
    /// </summary>
    public static bool IsUserDefinedCategory(TypeCategory category) => category switch
    {
        TypeCategory.Class => true,
        TypeCategory.Instance => true,
        TypeCategory.Interface => true,
        TypeCategory.Record => true,
        TypeCategory.Enum => true,
        TypeCategory.Namespace => true,
        _ => false
    };

    /// <summary>
    /// Checks if a category represents a primitive type.
    /// </summary>
    public static bool IsPrimitiveCategory(TypeCategory category) => category switch
    {
        TypeCategory.String => true,
        TypeCategory.Number => true,
        TypeCategory.Boolean => true,
        TypeCategory.BigInt => true,
        TypeCategory.Symbol => true,
        _ => false
    };
}

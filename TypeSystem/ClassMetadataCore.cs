using System.Collections.Frozen;
using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

/// <summary>
/// Immutable core metadata shared between Class and GenericClass.
/// Contains all structural information about a class definition.
/// </summary>
public sealed record ClassMetadataCore(
    string Name,
    TypeInfo? Superclass,
    FrozenDictionary<string, TypeInfo> Methods,
    FrozenDictionary<string, TypeInfo> StaticMethods,
    FrozenDictionary<string, TypeInfo> StaticProperties,
    FrozenDictionary<string, AccessModifier> MethodAccess,
    FrozenDictionary<string, AccessModifier> FieldAccess,
    FrozenSet<string> ReadonlyFields,
    FrozenDictionary<string, TypeInfo> Getters,
    FrozenDictionary<string, TypeInfo> Setters,
    FrozenDictionary<string, TypeInfo> FieldTypes,
    bool IsAbstract = false,
    FrozenSet<string>? AbstractMethods = null,
    FrozenSet<string>? AbstractGetters = null,
    FrozenSet<string>? AbstractSetters = null,
    FrozenDictionary<string, TypeInfo>? PrivateFields = null,
    FrozenDictionary<string, TypeInfo>? PrivateMethods = null,
    FrozenDictionary<string, TypeInfo>? StaticPrivateFields = null,
    FrozenDictionary<string, TypeInfo>? StaticPrivateMethods = null
)
{
    /// <summary>Abstract method names with empty fallback.</summary>
    public FrozenSet<string> AbstractMethodSet => AbstractMethods ?? FrozenSet<string>.Empty;

    /// <summary>Abstract getter names with empty fallback.</summary>
    public FrozenSet<string> AbstractGetterSet => AbstractGetters ?? FrozenSet<string>.Empty;

    /// <summary>Abstract setter names with empty fallback.</summary>
    public FrozenSet<string> AbstractSetterSet => AbstractSetters ?? FrozenSet<string>.Empty;

    /// <summary>Private field types with empty dictionary fallback.</summary>
    public FrozenDictionary<string, TypeInfo> PrivateFieldTypes =>
        PrivateFields ?? FrozenDictionary<string, TypeInfo>.Empty;

    /// <summary>Private method types with empty dictionary fallback.</summary>
    public FrozenDictionary<string, TypeInfo> PrivateMethodTypes =>
        PrivateMethods ?? FrozenDictionary<string, TypeInfo>.Empty;

    /// <summary>Static private field types with empty dictionary fallback.</summary>
    public FrozenDictionary<string, TypeInfo> StaticPrivateFieldTypes =>
        StaticPrivateFields ?? FrozenDictionary<string, TypeInfo>.Empty;

    /// <summary>Static private method types with empty dictionary fallback.</summary>
    public FrozenDictionary<string, TypeInfo> StaticPrivateMethodTypes =>
        StaticPrivateMethods ?? FrozenDictionary<string, TypeInfo>.Empty;
}

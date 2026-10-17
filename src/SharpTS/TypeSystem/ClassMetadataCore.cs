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
    FrozenDictionary<string, TypeInfo>? StaticPrivateMethods = null,
    TypeInfo? StringIndexType = null,
    TypeInfo? NumberIndexType = null,
    TypeInfo? SymbolIndexType = null,
    int DeclarationId = 0,
    FrozenDictionary<string, AccessModifier>? StaticMethodAccess = null,
    FrozenDictionary<string, AccessModifier>? StaticFieldAccess = null,
    FrozenSet<string>? StaticReadonlyFields = null,
    FrozenDictionary<string, TypeInfo>? StaticSetters = null
)
{
    /// <summary>Independent accessor write visibility; null entries identify getter-only members.</summary>
    public FrozenDictionary<(bool IsStatic, string Name), AccessModifier?>? AccessorSetterAccess { get; init; }

    /// <summary>Source optionality retained with its class across checker inputs and generic projections.</summary>
    public FrozenSet<(bool IsStatic, string Name)>? OrdinaryOptionalMembers { get; init; }

    /// <summary>Constructor index signatures, independent of instance indices.</summary>
    public TypeInfo? StaticStringIndexType { get; init; }
    public TypeInfo? StaticNumberIndexType { get; init; }
    public TypeInfo? StaticSymbolIndexType { get; init; }

    // CLR interop accepts the declared camelCase member's PascalCase spelling.
    internal bool IsDotNetMapped { get; init; }

    /// <summary>True when the instance facet declares any index signature.</summary>
    public bool HasIndexSignature => StringIndexType != null || NumberIndexType != null || SymbolIndexType != null;

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

    /// <summary>
    /// Access modifiers for static methods, keyed by name. Kept separate from
    /// <see cref="MethodAccess"/> (instance methods) so a static and an instance
    /// method sharing a name don't overwrite each other's visibility (issue #722).
    /// Empty dictionary fallback.
    /// </summary>
    public FrozenDictionary<string, AccessModifier> StaticMethodAccessMap =>
        StaticMethodAccess ?? FrozenDictionary<string, AccessModifier>.Empty;

    /// <summary>
    /// Access modifiers for static fields/properties, keyed by name. Kept separate
    /// from <see cref="FieldAccess"/> (instance fields) so a static and an instance
    /// field sharing a name don't overwrite each other's visibility (issue #722).
    /// Empty dictionary fallback.
    /// </summary>
    public FrozenDictionary<string, AccessModifier> StaticFieldAccessMap =>
        StaticFieldAccess ?? FrozenDictionary<string, AccessModifier>.Empty;

    /// <summary>Static accessor write types, which may differ from their getter read types.</summary>
    public FrozenDictionary<string, TypeInfo> StaticSetterTypes =>
        StaticSetters ?? FrozenDictionary<string, TypeInfo>.Empty;
}

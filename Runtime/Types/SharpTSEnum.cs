using SharpTS.TypeSystem;
using System.Collections.Frozen;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of a TypeScript enum.
/// </summary>
/// <remarks>
/// Stores enum member name-to-value mappings. Supports reverse mapping (value-to-name)
/// for numeric enums, enabling <c>MyEnum[0]</c> syntax to retrieve member names.
/// Handles numeric, string, and heterogeneous (mixed) enum types via <see cref="EnumKind"/>.
/// </remarks>
public class SharpTSEnum(string name, Dictionary<string, object> members, EnumKind kind) : ITypeCategorized
{
    /// <inheritdoc />
    public TypeCategory RuntimeCategory => TypeCategory.Enum;

    public string Name { get; } = name;
    public EnumKind Kind { get; } = kind;
    private readonly FrozenDictionary<string, object> _members = members.ToFrozenDictionary();

    // Only build reverse mapping for numeric members
    private readonly FrozenDictionary<double, string> _reverse =
        members.Where(kvp => kvp.Value is double)
               .ToFrozenDictionary(kvp => (double)kvp.Value!, kvp => kvp.Key);

    public object GetMember(string name) =>
        _members.TryGetValue(name, out var v) ? v
        : throw new Exception($"Runtime Error: '{name}' not in enum '{Name}'.");

    public string GetReverse(double value) =>
        _reverse.TryGetValue(value, out var n) ? n
        : throw new Exception($"Runtime Error: Value {value} not in enum '{Name}'. " +
                              "Note: Reverse mapping only works for numeric enum members.");

    public bool HasMember(string name) => _members.ContainsKey(name);

    public bool HasReverseMapping => Kind == EnumKind.Numeric || Kind == EnumKind.Heterogeneous;

    public override string ToString() => Name;
}

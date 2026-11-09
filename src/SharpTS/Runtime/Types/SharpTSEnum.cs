using SharpTS.Runtime;
using SharpTS.TypeSystem;

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
    public EnumKind Kind { get; internal set; } = kind;
    private readonly Dictionary<string, object> _members = new(members);

    private readonly Dictionary<double, string> _reverse = BuildReverseMapping(members);

    private static Dictionary<double, string> BuildReverseMapping(Dictionary<string, object> members)
    {
        var reverse = new Dictionary<double, string>();
        foreach (var (name, value) in members)
        {
            if (value is double number)
                reverse[number] = name;
        }
        return reverse;
    }

    internal void AddMember(string name, object value)
    {
        _members[name] = value;
        if (value is double number)
            _reverse[number] = name;
    }

    public object GetMember(string name) =>
        _members.TryGetValue(name, out var v) ? v
        : throw new Exception($"Runtime Error: '{name}' not in enum '{Name}'.");

    public RuntimeValue GetMemberRV(string name) => RuntimeValue.FromBoxed(GetMember(name));

    public string GetReverse(double value) =>
        _reverse.TryGetValue(value, out var n) ? n
        : throw new Exception($"Runtime Error: Value {value} not in enum '{Name}'. " +
                              "Note: Reverse mapping only works for numeric enum members.");

    internal string? FindReverseProperty(string key)
        => PropertyKeyConverter.TryParseCanonicalNumericKey(key, out double number)
            ? _reverse.GetValueOrDefault(number) : null;

    public bool HasMember(string name) => _members.ContainsKey(name);

    public bool HasReverseMapping => Kind == EnumKind.Numeric || Kind == EnumKind.Heterogeneous;

    public override string ToString() => Name;
}

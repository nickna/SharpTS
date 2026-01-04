namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of a TypeScript const enum.
/// </summary>
/// <remarks>
/// Const enums are compile-time inlined and do not have runtime objects in TypeScript.
/// This wrapper exists for SharpTS interpreter support but does not support reverse mapping.
/// For IL compilation, const enum values are fully inlined at compile time.
/// </remarks>
public class ConstEnumValues(string name, Dictionary<string, object> members)
{
    public string Name { get; } = name;
    private readonly Dictionary<string, object> _members = members;

    public object GetMember(string name) =>
        _members.TryGetValue(name, out var v) ? v
        : throw new Exception($"Runtime Error: '{name}' not in const enum '{Name}'.");

    public bool HasMember(string name) => _members.ContainsKey(name);

    public override string ToString() => $"const {Name}";
}

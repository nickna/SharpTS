namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of a TypeScript namespace.
/// </summary>
/// <remarks>
/// At runtime, namespaces are objects with their exported members as properties.
/// Classes become constructor functions, functions remain functions, and variables are values.
/// Supports declaration merging via the Merge method.
/// </remarks>
public class SharpTSNamespace
{
    public string Name { get; }
    private readonly Dictionary<string, object?> _members = [];

    public SharpTSNamespace(string name)
    {
        Name = name;
    }

    /// <summary>
    /// Gets a member value by name.
    /// </summary>
    public object? Get(string name)
    {
        return _members.TryGetValue(name, out var value) ? value : null;
    }

    /// <summary>
    /// Sets a member value.
    /// </summary>
    public void Set(string name, object? value)
    {
        _members[name] = value;
    }

    /// <summary>
    /// Checks if a member exists.
    /// </summary>
    public bool HasMember(string name)
    {
        return _members.ContainsKey(name);
    }

    /// <summary>
    /// Gets all member names.
    /// </summary>
    public IEnumerable<string> GetMemberNames() => _members.Keys;

    /// <summary>
    /// Merges another namespace's members into this one (for declaration merging).
    /// </summary>
    public void Merge(SharpTSNamespace other)
    {
        foreach (var (name, value) in other._members)
        {
            _members[name] = value;
        }
    }

    public override string ToString() => $"[namespace {Name}]";
}

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of plain object literals.
/// </summary>
/// <remarks>
/// Represents <c>{ key: value }</c> object literals (not class instances).
/// Stores fields in a dictionary with dynamic Get/Set access. Used for structural
/// typing, object destructuring, and <c>Object.keys()</c> support.
/// Unlike <see cref="SharpTSInstance"/>, plain objects have no associated class or methods.
/// </remarks>
/// <seealso cref="SharpTSInstance"/>
/// <seealso cref="SharpTSArray"/>
public class SharpTSObject(Dictionary<string, object?> fields)
{
    private readonly Dictionary<string, object?> _fields = fields;

    /// <summary>
    /// Expose fields for Object.keys() and object rest patterns
    /// </summary>
    public IReadOnlyDictionary<string, object?> Fields => _fields;

    public object? Get(string name)
    {
        if (_fields.TryGetValue(name, out object? value))
        {
            return value;
        }
        return null; // or throw undefined
    }

    public void Set(string name, object? value)
    {
        _fields[name] = value;
    }

    public bool HasProperty(string name)
    {
        return _fields.ContainsKey(name);
    }

    public override string ToString() => $"{{ {string.Join(", ", _fields.Select(f => $"{f.Key}: {f.Value}"))} }}";
}

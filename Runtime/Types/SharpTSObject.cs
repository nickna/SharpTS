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
public class SharpTSObject(Dictionary<string, object?> fields) : ISharpTSPropertyAccessor
{
    private readonly Dictionary<string, object?> _fields = fields;
    private readonly Dictionary<SharpTSSymbol, object?> _symbolFields = new();

    /// <summary>
    /// Whether this object is frozen (no property additions, removals, or modifications).
    /// </summary>
    public bool IsFrozen { get; private set; }

    /// <summary>
    /// Whether this object is sealed (no property additions or removals, but modifications allowed).
    /// </summary>
    public bool IsSealed { get; private set; }

    /// <summary>
    /// Freezes this object, preventing any property changes.
    /// </summary>
    public void Freeze()
    {
        IsFrozen = true;
        IsSealed = true; // Frozen implies sealed
    }

    /// <summary>
    /// Seals this object, preventing property additions/removals but allowing modifications.
    /// </summary>
    public void Seal()
    {
        IsSealed = true;
    }

    /// <summary>
    /// Expose fields for Object.keys() and object rest patterns
    /// </summary>
    public IReadOnlyDictionary<string, object?> Fields => _fields;

    /// <inheritdoc />
    public IEnumerable<string> PropertyNames => _fields.Keys;

    /// <inheritdoc />
    public object? GetProperty(string name)
    {
        if (_fields.TryGetValue(name, out object? value))
        {
            return value;
        }
        return null;
    }

    /// <inheritdoc />
    public void SetProperty(string name, object? value)
    {
        if (IsFrozen)
        {
            // Frozen objects silently ignore property modifications (JavaScript behavior)
            return;
        }

        bool exists = _fields.ContainsKey(name);
        if (IsSealed && !exists)
        {
            // Sealed objects silently ignore new property additions
            return;
        }

        _fields[name] = value;
    }

    /// <summary>
    /// Removes a property by name. Respects frozen/sealed state.
    /// </summary>
    public bool DeleteProperty(string name)
    {
        if (IsFrozen || IsSealed)
        {
            // Frozen and sealed objects silently ignore property deletions
            return false;
        }
        return _fields.Remove(name);
    }

    public bool HasProperty(string name)
    {
        return _fields.ContainsKey(name);
    }

    /// <summary>
    /// Gets a value by symbol key.
    /// </summary>
    public object? GetBySymbol(SharpTSSymbol symbol)
    {
        return _symbolFields.TryGetValue(symbol, out var value) ? value : null;
    }

    /// <summary>
    /// Sets a value by symbol key.
    /// </summary>
    public void SetBySymbol(SharpTSSymbol symbol, object? value)
    {
        if (IsFrozen)
        {
            return;
        }

        bool exists = _symbolFields.ContainsKey(symbol);
        if (IsSealed && !exists)
        {
            return;
        }

        _symbolFields[symbol] = value;
    }

    /// <summary>
    /// Checks if the object has a property with the given symbol key.
    /// </summary>
    public bool HasSymbolProperty(SharpTSSymbol symbol)
    {
        return _symbolFields.ContainsKey(symbol);
    }

    public override string ToString() => $"{{ {string.Join(", ", _fields.Select(f => $"{f.Key}: {f.Value}"))} }}";
}

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
    private Dictionary<string, ISharpTSCallable>? _getters;
    private Dictionary<string, ISharpTSCallable>? _setters;

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
            // Frozen objects silently ignore property modifications (JavaScript behavior in non-strict mode)
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
    /// Sets a property value with strict mode behavior.
    /// In strict mode, throws TypeError for modifications to frozen objects or new properties on sealed objects.
    /// </summary>
    /// <param name="name">The property name to set.</param>
    /// <param name="value">The value to set.</param>
    /// <param name="strictMode">Whether strict mode is enabled.</param>
    public void SetPropertyStrict(string name, object? value, bool strictMode)
    {
        if (IsFrozen)
        {
            if (strictMode)
            {
                throw new Exception($"TypeError: Cannot assign to read only property '{name}' of object");
            }
            return;
        }

        bool exists = _fields.ContainsKey(name);
        if (IsSealed && !exists)
        {
            if (strictMode)
            {
                throw new Exception($"TypeError: Cannot add property '{name}' to a sealed object");
            }
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

    /// <summary>
    /// Removes a property by name with strict mode behavior.
    /// In strict mode, throws TypeError for deletions on frozen/sealed objects.
    /// </summary>
    /// <param name="name">The property name to delete.</param>
    /// <param name="strictMode">Whether strict mode is enabled.</param>
    /// <returns>True if the property was deleted, false otherwise.</returns>
    public bool DeletePropertyStrict(string name, bool strictMode)
    {
        if (IsFrozen || IsSealed)
        {
            if (strictMode)
            {
                throw new Exception($"TypeError: Cannot delete property '{name}' of a frozen or sealed object");
            }
            return false;
        }
        return _fields.Remove(name);
    }

    public bool HasProperty(string name)
    {
        return _fields.ContainsKey(name) || (_getters?.ContainsKey(name) ?? false);
    }

    /// <summary>
    /// Defines a getter for a property.
    /// </summary>
    public void DefineGetter(string name, ISharpTSCallable getter)
    {
        _getters ??= new Dictionary<string, ISharpTSCallable>();
        _getters[name] = getter;
    }

    /// <summary>
    /// Defines a setter for a property.
    /// </summary>
    public void DefineSetter(string name, ISharpTSCallable setter)
    {
        _setters ??= new Dictionary<string, ISharpTSCallable>();
        _setters[name] = setter;
    }

    /// <summary>
    /// Checks if a property has a getter.
    /// </summary>
    public bool HasGetter(string name)
    {
        return _getters?.ContainsKey(name) ?? false;
    }

    /// <summary>
    /// Checks if a property has a setter.
    /// </summary>
    public bool HasSetter(string name)
    {
        return _setters?.ContainsKey(name) ?? false;
    }

    /// <summary>
    /// Gets the getter function for a property, or null if none.
    /// </summary>
    public ISharpTSCallable? GetGetter(string name)
    {
        return _getters?.GetValueOrDefault(name);
    }

    /// <summary>
    /// Gets the setter function for a property, or null if none.
    /// </summary>
    public ISharpTSCallable? GetSetter(string name)
    {
        return _setters?.GetValueOrDefault(name);
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
    /// Sets a value by symbol key with strict mode behavior.
    /// </summary>
    public void SetBySymbolStrict(SharpTSSymbol symbol, object? value, bool strictMode)
    {
        if (IsFrozen)
        {
            if (strictMode)
            {
                throw new Exception($"TypeError: Cannot assign to read only symbol property of object");
            }
            return;
        }

        bool exists = _symbolFields.ContainsKey(symbol);
        if (IsSealed && !exists)
        {
            if (strictMode)
            {
                throw new Exception($"TypeError: Cannot add symbol property to a sealed object");
            }
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

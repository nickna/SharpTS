using SharpTS.Parsing;
using SharpTS.Execution;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of an instantiated class object.
/// </summary>
/// <remarks>
/// Created when a <see cref="SharpTSClass"/> is invoked (e.g., <c>new MyClass()</c>).
/// Stores instance field values in a dictionary. Property access delegates to the
/// class for method lookups, getters, and setters. Uses <see cref="Token"/> for
/// property names to enable precise error reporting with source location.
/// </remarks>
/// <seealso cref="SharpTSClass"/>
/// <seealso cref="SharpTSObject"/>
public class SharpTSInstance(SharpTSClass klass) : ISharpTSPropertyAccessor
{
    private readonly SharpTSClass _klass = klass;
    private readonly Dictionary<string, object?> _fields = [];
    private readonly Dictionary<SharpTSSymbol, object?> _symbolFields = new();
    private Interpreter? _interpreter;

    // Property lookup caches to avoid expensive inheritance chain walks
    private readonly Dictionary<string, PropertyResolution> _lookupCache = [];
    private readonly Dictionary<string, SharpTSFunction?> _setterCache = [];

    /// <summary>
    /// Whether this instance is frozen (no property additions, removals, or modifications).
    /// </summary>
    public bool IsFrozen { get; private set; }

    /// <summary>
    /// Whether this instance is sealed (no property additions or removals, but modifications allowed).
    /// </summary>
    public bool IsSealed { get; private set; }

    /// <summary>
    /// Freezes this instance, preventing any property changes.
    /// </summary>
    public void Freeze()
    {
        IsFrozen = true;
        IsSealed = true; // Frozen implies sealed
    }

    /// <summary>
    /// Seals this instance, preventing property additions/removals but allowing modifications.
    /// </summary>
    public void Seal()
    {
        IsSealed = true;
    }

    private enum ResolutionType
    {
        Getter,      // Resolved to a getter (invoke on each access)
        Field,       // Resolved to an instance field (read from _fields)
        Method,      // Resolved to a method (bind on access)
        NotFound     // Property doesn't exist (cache negative lookups)
    }

    private sealed class PropertyResolution
    {
        public required ResolutionType Type { get; init; }
        public SharpTSFunction? Function { get; init; }
    }

    public void SetInterpreter(Interpreter interpreter) => _interpreter = interpreter;

    private PropertyResolution ResolveProperty(string name)
    {
        // Check for getter first
        SharpTSFunction? getter = _klass.FindGetter(name);
        if (getter != null)
        {
            return new PropertyResolution
            {
                Type = ResolutionType.Getter,
                Function = getter
            };
        }

        // Check if it's a field (don't cache the value!)
        if (_fields.ContainsKey(name))
        {
            return new PropertyResolution { Type = ResolutionType.Field };
        }

        // Check for method
        SharpTSFunction? method = _klass.FindMethod(name);
        if (method != null)
        {
            return new PropertyResolution
            {
                Type = ResolutionType.Method,
                Function = method
            };
        }

        // Cache negative result to avoid repeated failed lookups
        return new PropertyResolution { Type = ResolutionType.NotFound };
    }

    public object? Get(Token name)
    {
        string propName = name.Lexeme;

        // Check cache first
        if (!_lookupCache.TryGetValue(propName, out PropertyResolution? resolution))
        {
            // Cache miss - perform full lookup and cache the resolution
            resolution = ResolveProperty(propName);
            _lookupCache[propName] = resolution;
        }

        // Use cached resolution
        return resolution.Type switch
        {
            ResolutionType.Getter => resolution.Function!.Bind(this).Call(_interpreter!, []),
            ResolutionType.Field => _fields[propName],
            ResolutionType.Method => resolution.Function!.Bind(this),
            ResolutionType.NotFound => throw new Exception($"Undefined property '{propName}'."),
            _ => throw new InvalidOperationException("Unknown resolution type")
        };
    }

    public void Set(Token name, object? value)
    {
        string propName = name.Lexeme;

        // Check frozen state first
        if (IsFrozen)
        {
            // Frozen objects silently ignore property modifications (JavaScript behavior)
            return;
        }

        // Check sealed state for new property addition
        bool exists = _fields.ContainsKey(propName);
        if (IsSealed && !exists)
        {
            // Sealed objects silently ignore new property additions
            return;
        }

        // Check cache for setter resolution
        if (!_setterCache.TryGetValue(propName, out SharpTSFunction? cachedSetter))
        {
            cachedSetter = _klass.FindSetter(propName);
            _setterCache[propName] = cachedSetter; // Cache null if no setter exists
        }

        if (cachedSetter != null && _interpreter != null)
        {
            cachedSetter.Bind(this).Call(_interpreter, [value]);
            return;
        }

        // No cache invalidation needed - we update _fields, Get() reads fresh value
        _fields[propName] = value;

        // Ensure property is in lookup cache as a field for dynamic property addition
        if (!_lookupCache.ContainsKey(propName))
        {
            _lookupCache[propName] = new PropertyResolution { Type = ResolutionType.Field };
        }
    }

    /// <summary>
    /// Sets a property with strict mode behavior.
    /// In strict mode, throws TypeError for modifications to frozen objects or new properties on sealed objects.
    /// </summary>
    public void SetStrict(Token name, object? value, bool strictMode)
    {
        string propName = name.Lexeme;

        if (IsFrozen)
        {
            if (strictMode)
            {
                throw new Exception($"TypeError: Cannot assign to read only property '{propName}' of object");
            }
            return;
        }

        bool exists = _fields.ContainsKey(propName);
        if (IsSealed && !exists)
        {
            if (strictMode)
            {
                throw new Exception($"TypeError: Cannot add property '{propName}' to a sealed object");
            }
            return;
        }

        // Check cache for setter resolution
        if (!_setterCache.TryGetValue(propName, out SharpTSFunction? cachedSetter))
        {
            cachedSetter = _klass.FindSetter(propName);
            _setterCache[propName] = cachedSetter;
        }

        if (cachedSetter != null && _interpreter != null)
        {
            cachedSetter.Bind(this).Call(_interpreter, [value]);
            return;
        }

        _fields[propName] = value;

        if (!_lookupCache.ContainsKey(propName))
        {
            _lookupCache[propName] = new PropertyResolution { Type = ResolutionType.Field };
        }
    }

    public bool HasProperty(string name)
    {
        if (!_lookupCache.TryGetValue(name, out PropertyResolution? resolution))
        {
            resolution = ResolveProperty(name);
            _lookupCache[name] = resolution;
        }
        return resolution.Type != ResolutionType.NotFound;
    }

    public SharpTSClass GetClass() => _klass;

    /// <summary>
    /// Get all field names for Object.keys() support
    /// </summary>
    public IEnumerable<string> GetFieldNames() => _fields.Keys;

    /// <inheritdoc />
    public IEnumerable<string> PropertyNames => GetFieldNames();

    /// <summary>
    /// Gets a field value directly without invoking getters or binding methods.
    /// Used for Object.keys(), JSON serialization, and object rest patterns.
    /// </summary>
    public object? GetRawField(string name) => _fields.TryGetValue(name, out var value) ? value : null;

    /// <inheritdoc />
    public object? GetProperty(string name) => GetRawField(name);

    /// <summary>
    /// Sets a field value directly without invoking setters.
    /// Used for bracket notation assignment and constructor initialization.
    /// Respects frozen/sealed state.
    /// </summary>
    public void SetRawField(string name, object? value)
    {
        if (IsFrozen)
        {
            return;
        }

        bool exists = _fields.ContainsKey(name);
        if (IsSealed && !exists)
        {
            return;
        }

        _fields[name] = value;
    }

    /// <summary>
    /// Sets a field value directly with strict mode behavior.
    /// In strict mode, throws TypeError for modifications to frozen objects or new properties on sealed objects.
    /// </summary>
    public void SetRawFieldStrict(string name, object? value, bool strictMode)
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
    /// Removes a field by name. Respects frozen/sealed state.
    /// </summary>
    public bool DeleteField(string name)
    {
        if (IsFrozen || IsSealed)
        {
            return false;
        }
        return _fields.Remove(name);
    }

    /// <summary>
    /// Removes a field by name with strict mode behavior.
    /// In strict mode, throws TypeError for deletions on frozen/sealed objects.
    /// </summary>
    public bool DeleteFieldStrict(string name, bool strictMode)
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

    /// <inheritdoc />
    public void SetProperty(string name, object? value) => SetRawField(name, value);

    /// <summary>
    /// Gets a value by symbol key.
    /// </summary>
    public object? GetBySymbol(SharpTSSymbol symbol)
    {
        return _symbolFields.TryGetValue(symbol, out var value) ? value : null;
    }

    /// <summary>
    /// Sets a value by symbol key. Respects frozen/sealed state.
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

    public override string ToString() => _klass.Name + " instance";
}

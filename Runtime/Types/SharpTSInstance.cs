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
    /// Get a field value by name for object rest patterns
    /// </summary>
    public object? GetFieldValue(string name) => _fields.TryGetValue(name, out var value) ? value : null;

    /// <inheritdoc />
    public object? GetProperty(string name) => GetFieldValue(name);

    /// <summary>
    /// Set a field value by name for bracket notation assignment
    /// </summary>
    public void SetFieldValue(string name, object? value) => _fields[name] = value;

    /// <inheritdoc />
    public void SetProperty(string name, object? value) => SetFieldValue(name, value);

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
        _symbolFields[symbol] = value;
    }

    public override string ToString() => _klass.Name + " instance";
}

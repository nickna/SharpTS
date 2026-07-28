using SharpTS.Parsing;
using SharpTS.Execution;
using SharpTS.Runtime;
using SharpTS.TypeSystem;

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
public class SharpTSInstance(SharpTSClass klass) : ISharpTSPropertyAccessor, ITypeCategorized
{
    private readonly SharpTSClass _klass = klass;

    /// <summary>
    /// Gets the class that this instance was created from.
    /// Used for ES2022 private field brand checking.
    /// </summary>
    public SharpTSClass RuntimeClass => _klass;

    /// <inheritdoc />
    public TypeCategory RuntimeCategory => TypeCategory.Instance;
    private readonly Dictionary<string, object?> _fields = [];
    private readonly Dictionary<SharpTSSymbol, object?> _symbolFields = new();
    private Dictionary<string, PropertyDescriptorFlags>? _descriptors;
    private Interpreter? _interpreter;

    // Property lookup caches to avoid expensive inheritance chain walks
    private readonly Dictionary<string, PropertyResolution> _lookupCache = [];
    private readonly Dictionary<string, SharpTSFunction?> _setterCache = [];

    // Bound method cache to avoid repeated Bind() allocations
    // Key is the method name (since we already resolved the method via _lookupCache)
    private readonly Dictionary<string, ISharpTSCallable> _boundMethodCache = [];

    /// <summary>
    /// Whether this instance is frozen (no property additions, removals, or modifications).
    /// </summary>
    public bool IsFrozen { get; private set; }

    /// <summary>
    /// Whether this instance is sealed (no property additions or removals, but modifications allowed).
    /// </summary>
    public bool IsSealed { get; private set; }

    /// <summary>
    /// Whether this instance is extensible (can have new properties added).
    /// </summary>
    public bool IsExtensible { get; private set; } = true;

    /// <summary>
    /// The prototype of this class instance.
    /// For class instances, this is null as SharpTS doesn't implement a full prototype chain for classes.
    /// </summary>
    public object? Prototype => null;

    /// <summary>
    /// Freezes this instance, preventing any property changes.
    /// </summary>
    public void Freeze()
    {
        SetOwnPropertyIntegrityLevel(frozen: true);
        IsFrozen = true;
        IsSealed = true; // Frozen implies sealed
        IsExtensible = false; // Frozen implies non-extensible
    }

    /// <summary>
    /// Seals this instance, preventing property additions/removals but allowing modifications.
    /// </summary>
    public void Seal()
    {
        SetOwnPropertyIntegrityLevel(frozen: false);
        IsSealed = true;
        IsExtensible = false;
    }

    /// <summary>
    /// Applies SetIntegrityLevel's descriptor changes to every own instance
    /// field, preserving writability when sealing and clearing it when freezing.
    /// </summary>
    private void SetOwnPropertyIntegrityLevel(bool frozen)
    {
        _descriptors ??= [];
        foreach (var name in _fields.Keys)
        {
            var current = GetPropertyFlags(name);
            _descriptors[name] = PropertyDescriptorFlags.ForDefineProperty(
                writable: frozen ? false : current.Writable,
                enumerable: current.Enumerable,
                configurable: false);
        }
    }

    /// <summary>
    /// Prevents adding new properties to this instance.
    /// </summary>
    public void PreventExtensions()
    {
        IsExtensible = false;
    }

    /// <summary>
    /// Gets all symbol-keyed property names.
    /// </summary>
    public IEnumerable<SharpTSSymbol> GetSymbolPropertyNames()
    {
        return _symbolFields.Keys;
    }

    private enum ResolutionType
    {
        Getter,      // Resolved to a getter (invoke on each access)
        Field,       // Resolved to an instance field (read from _fields)
        Method,      // Resolved to a method (bind on access)
        AutoAccessor,// Resolved to an auto-accessor (TypeScript 4.9+)
        ClassSelf,   // The special `constructor` property — returns the class
        NotFound     // Property doesn't exist (cache negative lookups)
    }

    private sealed class PropertyResolution
    {
        public required ResolutionType Type { get; init; }
        public ISharpTSCallable? Function { get; init; }
    }

    public void SetInterpreter(Interpreter interpreter) => _interpreter = interpreter;

    private PropertyResolution ResolveProperty(string name)
    {
        // JS spec: instance.constructor returns the class (function) itself,
        // not whatever was registered as a "constructor" method on the class.
        // Without this short-circuit, `err.constructor === TypeError` fails
        // because FindMethod returns ErrorConstructorCallable (a distinct
        // callable per access), breaking Test262's assert.throws identity
        // check.
        if (name == "constructor")
        {
            return new PropertyResolution { Type = ResolutionType.ClassSelf };
        }

        // Check for auto-accessor first (TypeScript 4.9+)
        // Auto-accessors take precedence since they're a specific declaration
        if (_klass.HasInstanceAutoAccessor(name))
        {
            return new PropertyResolution { Type = ResolutionType.AutoAccessor };
        }

        // Check for getter
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
        ISharpTSCallable? method = _klass.FindMethod(name);
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
            ResolutionType.AutoAccessor => _klass.GetAutoAccessorValue(this, propName),
            ResolutionType.Getter => ((SharpTSFunction)resolution.Function!).Bind(this).Call(_interpreter!, []),
            ResolutionType.Field => _fields[propName],
            ResolutionType.Method => GetOrCreateBoundMethod(propName, resolution.Function!),
            ResolutionType.ClassSelf => _klass,
            ResolutionType.NotFound => SharpTSUndefined.Instance, // JavaScript semantics: missing properties return undefined
            _ => throw new InvalidOperationException("Unknown resolution type")
        };
    }

    public RuntimeValue GetRV(Token name) => RuntimeValue.FromBoxed(Get(name));

    /// <summary>
    /// Gets a cached bound method or creates and caches a new one.
    /// Avoids repeated Bind() allocations for frequently accessed methods.
    /// Handles both sync and async methods via SharpTSClass.BindMethod.
    /// </summary>
    private ISharpTSCallable GetOrCreateBoundMethod(string methodName, ISharpTSCallable method)
    {
        if (!_boundMethodCache.TryGetValue(methodName, out var boundMethod))
        {
            boundMethod = SharpTSClass.BindMethod(method, this);
            _boundMethodCache[methodName] = boundMethod;
        }
        return boundMethod;
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

        // Check extensibility for new property addition
        bool exists = _fields.ContainsKey(propName);
        if (!IsExtensible && !exists)
        {
            // Non-extensible objects silently ignore new property additions
            return;
        }

        // Check for auto-accessor first (TypeScript 4.9+)
        if (_klass.HasInstanceAutoAccessor(propName))
        {
            // Check if readonly - readonly auto-accessors have no setter
            var accessor = _klass.GetAutoAccessorDeclaration(propName);
            if (accessor != null && accessor.IsReadonly)
            {
                throw new Exception($"Cannot assign to '{propName}' because it is a readonly auto-accessor.");
            }
            _klass.SetAutoAccessorValue(this, propName, value);
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
        _lookupCache.TryAdd(propName, new PropertyResolution { Type = ResolutionType.Field });
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
        if (!IsExtensible && !exists)
        {
            if (strictMode)
            {
                throw new Exception($"TypeError: Cannot add property '{propName}' to a non-extensible object");
            }
            return;
        }

        // Check for auto-accessor first (TypeScript 4.9+)
        if (_klass.HasInstanceAutoAccessor(propName))
        {
            // Check if readonly - readonly auto-accessors have no setter
            var accessor = _klass.GetAutoAccessorDeclaration(propName);
            if (accessor != null && accessor.IsReadonly)
            {
                throw new Exception($"Cannot assign to '{propName}' because it is a readonly auto-accessor.");
            }
            _klass.SetAutoAccessorValue(this, propName, value);
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

        _lookupCache.TryAdd(propName, new PropertyResolution { Type = ResolutionType.Field });
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
    /// Check whether a field exists by name. O(1) dictionary lookup.
    /// </summary>
    public bool HasField(string name) => _fields.ContainsKey(name);

    /// <summary>
    /// Get all field names for Object.keys() support
    /// </summary>
    public IEnumerable<string> GetFieldNames() => _fields.Keys;

    /// <summary>
    /// Enumerates own string keys whose descriptors are enumerable.
    /// Built-in instances can carry non-enumerable internal fields alongside
    /// ordinary user-assigned properties, so spec algorithms must not treat
    /// every backing field as enumerable.
    /// </summary>
    internal IEnumerable<string> OwnEnumerableKeys()
    {
        foreach (var key in _fields.Keys)
            if (GetPropertyFlags(key).Enumerable)
                yield return key;
    }

    internal void MarkNonEnumerable(string name)
    {
        if (!_fields.ContainsKey(name)) return;
        var flags = GetPropertyFlags(name);
        _descriptors ??= [];
        _descriptors[name] = PropertyDescriptorFlags.ForDefineProperty(
            flags.Writable, enumerable: false, flags.Configurable);
    }

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
    /// Respects frozen/extensible state and writable flags.
    /// </summary>
    public void SetRawField(string name, object? value)
    {
        if (IsFrozen)
        {
            return;
        }

        bool exists = _fields.ContainsKey(name);
        if (!IsExtensible && !exists)
        {
            return;
        }

        // Check writable flag for properties defined via defineProperty
        if (exists && _descriptors?.TryGetValue(name, out var flags) == true && flags.HasExplicitDescriptor && !flags.Writable)
        {
            return;
        }

        _fields[name] = value;
    }

    /// <summary>
    /// Sets a field value directly with strict mode behavior.
    /// In strict mode, throws TypeError for modifications to frozen objects, new properties on non-extensible objects,
    /// or assignments to non-writable properties.
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
        if (!IsExtensible && !exists)
        {
            if (strictMode)
            {
                throw new Exception($"TypeError: Cannot add property '{name}' to a non-extensible object");
            }
            return;
        }

        // Check writable flag for properties defined via defineProperty
        if (exists && _descriptors?.TryGetValue(name, out var flags) == true && flags.HasExplicitDescriptor && !flags.Writable)
        {
            if (strictMode)
            {
                throw new Exception($"TypeError: Cannot assign to read only property '{name}'");
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
    /// Sets a value by symbol key. Respects frozen/extensible state.
    /// </summary>
    public void SetBySymbol(SharpTSSymbol symbol, object? value)
    {
        if (IsFrozen)
        {
            return;
        }

        bool exists = _symbolFields.ContainsKey(symbol);
        if (!IsExtensible && !exists)
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
        if (!IsExtensible && !exists)
        {
            if (strictMode)
            {
                throw new Exception($"TypeError: Cannot add symbol property to a non-extensible object");
            }
            return;
        }

        _symbolFields[symbol] = value;
    }

    /// <summary>
    /// Removes a property by symbol key. Respects frozen/sealed state.
    /// </summary>
    public bool DeleteBySymbol(SharpTSSymbol symbol)
    {
        if (IsFrozen || IsSealed)
        {
            return false;
        }
        return _symbolFields.Remove(symbol);
    }

    /// <summary>
    /// Removes a property by symbol key with strict mode behavior.
    /// </summary>
    public bool DeleteBySymbolStrict(SharpTSSymbol symbol, bool strictMode)
    {
        if (IsFrozen || IsSealed)
        {
            if (strictMode)
            {
                throw new Exception($"TypeError: Cannot delete symbol property of a frozen or sealed object");
            }
            return false;
        }
        return _symbolFields.Remove(symbol);
    }

    /// <summary>
    /// Checks if the instance has a property with the given symbol key.
    /// </summary>
    public bool HasSymbolProperty(SharpTSSymbol symbol)
    {
        return _symbolFields.ContainsKey(symbol);
    }

    /// <summary>
    /// Defines or modifies a property with the given descriptor.
    /// Returns true on success, false if the operation is not allowed.
    /// Note: For class instances, this only affects instance fields, not class methods/getters/setters.
    /// </summary>
    public bool DefineProperty(string name, SharpTSPropertyDescriptor descriptor)
    {
        // Get existing descriptor flags if any
        bool hasExisting = _fields.ContainsKey(name);
        PropertyDescriptorFlags existingFlags = default;

        if (hasExisting && _descriptors?.TryGetValue(name, out existingFlags) != true)
        {
            existingFlags = PropertyDescriptorFlags.Default;
        }

        // Check if we can modify the property
        if (hasExisting && existingFlags.HasExplicitDescriptor && !existingFlags.Configurable)
        {
            if (descriptor.Writable != existingFlags.Writable ||
                descriptor.Enumerable != existingFlags.Enumerable ||
                descriptor.Configurable != existingFlags.Configurable)
            {
                return false;
            }
            if (!existingFlags.Writable && descriptor.Value != null)
            {
                var currentValue = _fields.TryGetValue(name, out var v) ? v : null;
                if (!ReferenceEquals(currentValue, descriptor.Value) &&
                    (currentValue == null || !currentValue.Equals(descriptor.Value)))
                {
                    return false;
                }
            }
        }

        // Check sealed/frozen/extensible state
        if (IsFrozen)
        {
            return false;
        }
        if (!IsExtensible && !hasExisting)
        {
            return false;
        }

        // Store the descriptor flags
        _descriptors ??= new Dictionary<string, PropertyDescriptorFlags>();
        _descriptors[name] = PropertyDescriptorFlags.ForDefineProperty(
            descriptor.Writable,
            descriptor.Enumerable,
            descriptor.Configurable
        );

        // Set the value (class instances only support data properties via defineProperty)
        _fields[name] = descriptor.Value;

        // Update lookup cache
        _lookupCache[name] = new PropertyResolution { Type = ResolutionType.Field };

        return true;
    }

    /// <summary>
    /// Gets the property descriptor for the given property name.
    /// Returns null if the property doesn't exist as an own property.
    /// Note: Only returns descriptors for instance fields, not class methods/getters/setters.
    /// </summary>
    public SharpTSPropertyDescriptor? GetOwnPropertyDescriptor(string name)
    {
        if (!_fields.TryGetValue(name, out var fieldValue))
        {
            return null;
        }

        // Get descriptor flags (defaults if not explicitly set)
        PropertyDescriptorFlags flags = default;
        if (_descriptors?.TryGetValue(name, out flags) != true)
        {
            flags = PropertyDescriptorFlags.Default;
        }

        return new SharpTSPropertyDescriptor
        {
            Value = fieldValue,
            Writable = flags.Writable,
            Enumerable = flags.Enumerable,
            Configurable = flags.Configurable
        };
    }

    /// <summary>
    /// Gets the descriptor flags for a property, or default flags if not explicitly set.
    /// </summary>
    public PropertyDescriptorFlags GetPropertyFlags(string name)
    {
        if (_descriptors?.TryGetValue(name, out var flags) == true)
        {
            return flags;
        }
        return PropertyDescriptorFlags.Default;
    }

    public override string ToString() => _klass.Name + " instance";
}

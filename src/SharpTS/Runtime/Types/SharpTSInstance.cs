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
    private Dictionary<string, object?> _fields = [];
    private Dictionary<string, ISharpTSCallable?>? _getters;
    private Dictionary<string, ISharpTSCallable?>? _setters;
    private SharpTSObject? _symbolProperties;
    private Dictionary<string, PropertyDescriptorFlags>? _descriptors;
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
    /// Whether this instance is extensible (can have new properties added).
    /// </summary>
    public bool IsExtensible { get; private set; } = true;

    /// <summary>
    /// The instance's effective prototype. Construction may select another
    /// constructor's prototype while retaining this instance's private brands.
    /// </summary>
    public object? Prototype => HasExplicitPrototype ? _explicitPrototype : _klass.Prototype;
    private object? _explicitPrototype;
    internal bool HasExplicitPrototype { get; private set; }
    internal bool UsesDynamicPrototypeChain
        => HasExplicitPrototype || _klass.HasModifiedInstancePrototypeChain;

    internal void SetExplicitPrototype(object? prototype)
    {
        _explicitPrototype = prototype;
        HasExplicitPrototype = true;
    }

    /// <summary>
    /// Freezes this instance, preventing any property changes.
    /// </summary>
    public void Freeze()
    {
        SetOwnPropertyIntegrityLevel(frozen: true);
        _symbolProperties?.Freeze();
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
        _symbolProperties?.Seal();
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
        _symbolProperties?.PreventExtensions();
    }

    /// <summary>
    /// Gets all symbol-keyed property names.
    /// </summary>
    public IEnumerable<SharpTSSymbol> GetSymbolPropertyNames()
    {
        return _symbolProperties?.GetSymbolPropertyNames() ?? [];
    }

    private enum ResolutionType
    {
        Getter,      // Resolved to a getter (invoke on each access)
        OwnAccessor, // Accessor installed with Object.defineProperty
        Field,       // Resolved to an instance field (read from _fields)
        Method,      // Resolved to the canonical prototype method
        AutoAccessor,// Resolved to an auto-accessor (TypeScript 4.9+)
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
        if ((_getters?.ContainsKey(name) ?? false) || (_setters?.ContainsKey(name) ?? false))
            return new PropertyResolution { Type = ResolutionType.OwnAccessor };

        // Own data properties shadow inherited accessors (don't cache the value!).
        if (_fields.ContainsKey(name))
        {
            return new PropertyResolution { Type = ResolutionType.Field };
        }

        // Check for an inherited auto-accessor (TypeScript 4.9+).
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
        if (propName == "constructor")
        {
            // Constructor is an ordinary live prototype property. Its default
            // back-reference must not hide own data/accessors or be cached over
            // a computed method, replacement, deletion, or changed prototype.
            if (_getters?.TryGetValue(propName, out var getter) == true)
                return InvokeOwnAccessor(getter, []);
            if (_setters?.ContainsKey(propName) == true) return SharpTSUndefined.Instance;
            if (_fields.TryGetValue(propName, out var value)) return value;
            if (_interpreter != null) return _interpreter.GetInstancePrototypeProperty(this, propName);
            // Native errors can expose their canonical constructor before an
            // interpreter is attached. Preserve that default without a null call.
            return Prototype is SharpTSClassPrototype prototype
                ? prototype.GetMember(propName) ?? SharpTSUndefined.Instance : SharpTSUndefined.Instance;
        }
        if (UsesDynamicPrototypeChain)
        {
            if (_getters?.TryGetValue(propName, out var getter) == true)
                return InvokeOwnAccessor(getter, []);
            if (_setters?.ContainsKey(propName) == true)
                return SharpTSUndefined.Instance;
            if (_fields.TryGetValue(propName, out var value)) return value;
            return _interpreter!.GetInstancePrototypeProperty(this, propName);
        }
        if (!_fields.ContainsKey(propName) && _getters?.ContainsKey(propName) != true
            && _setters?.ContainsKey(propName) != true && _klass.HasInstanceAccessorDeclaration(propName))
        {
            var descriptor = _klass.FindInstanceAccessorDescriptor(propName);
            if (descriptor is { HasGet: true } or { HasSet: true })
                return descriptor.Get is { } getter
                    ? BuiltIns.FunctionBuiltIns.CallWithThis(_interpreter!, getter, this, [])
                    : SharpTSUndefined.Instance;
            return descriptor == null ? SharpTSUndefined.Instance : descriptor.Value;
        }

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
            ResolutionType.OwnAccessor => _getters?.TryGetValue(propName, out var ownGetter) == true
                ? InvokeOwnAccessor(ownGetter, [])
                : SharpTSUndefined.Instance,
            ResolutionType.Field => _fields[propName],
            ResolutionType.Method => resolution.Function,
            ResolutionType.NotFound => SharpTSUndefined.Instance, // JavaScript semantics: missing properties return undefined
            _ => throw new InvalidOperationException("Unknown resolution type")
        };
    }

    public RuntimeValue GetRV(Token name) => RuntimeValue.FromBoxed(Get(name));

    private object? InvokeOwnAccessor(ISharpTSCallable? callable, List<object?> args)
        => callable switch
        {
            null => SharpTSUndefined.Instance,
            SharpTSFunction function => function.Bind(this).Call(_interpreter!, args),
            SharpTSArrowFunction function => function.Bind(this).Call(_interpreter!, args),
            _ => callable.Call(_interpreter!, args),
        };

    public void Set(Token name, object? value)
    {
        string propName = name.Lexeme;
        if (UsesDynamicPrototypeChain)
        {
            _interpreter!.SetInstancePropertyWithExplicitPrototype(this, propName, value, false);
            return;
        }
        if (TrySetClassAccessor(propName, value, false)) return;

        if (_setters?.TryGetValue(propName, out var ownSetter) == true)
        {
            if (ownSetter is not null)
                InvokeOwnAccessor(ownSetter, [value]);
            return;
        }

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

        _fields[propName] = value;
        // A successful own write can replace a cached miss or inherited method.
        // Re-resolve the name while preserving own accessor precedence.
        _lookupCache.Remove(propName);
    }

    /// <summary>
    /// Sets a property with strict mode behavior.
    /// In strict mode, throws TypeError for modifications to frozen objects or new properties on sealed objects.
    /// </summary>
    public void SetStrict(Token name, object? value, bool strictMode)
    {
        string propName = name.Lexeme;
        if (UsesDynamicPrototypeChain)
        {
            _interpreter!.SetInstancePropertyWithExplicitPrototype(this, propName, value, strictMode);
            return;
        }
        if (TrySetClassAccessor(propName, value, strictMode)) return;

        if (_setters?.TryGetValue(propName, out var ownSetter) == true)
        {
            if (ownSetter is not null)
                InvokeOwnAccessor(ownSetter, [value]);
            else if (strictMode)
                throw StrictModeErrors.TypeError($"Cannot set property '{propName}' which has only a getter");
            return;
        }

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
        _lookupCache.Remove(propName);
    }

    private bool TrySetClassAccessor(string name, object? value, bool strictMode)
    {
        if (_fields.ContainsKey(name) || _getters?.ContainsKey(name) == true || _setters?.ContainsKey(name) == true
            || !_klass.HasInstanceAccessorDeclaration(name)) return false;
        var descriptor = _klass.FindInstanceAccessorDescriptor(name);
        if (descriptor is not ({ HasGet: true } or { HasSet: true }))
        {
            if (descriptor is { HasWritable: true, Writable: false })
            {
                if (strictMode) throw StrictModeErrors.TypeError($"Cannot assign to read only property '{name}'");
                return true;
            }
            if (strictMode) SetRawFieldStrict(name, value, strictMode);
            else SetRawField(name, value);
            return true;
        }
        if (descriptor.Set is { } setter)
            BuiltIns.FunctionBuiltIns.CallWithThis(_interpreter!, setter, this, [value]);
        else if (strictMode)
            throw StrictModeErrors.TypeError($"Cannot set property '{name}' which has only a getter");
        return true;
    }

    public bool HasProperty(string name)
    {
        if (name == "constructor")
        {
            if (GetOwnPropertyDescriptor(name) != null) return true;
            return _interpreter?.HasProperty(Prototype, name)
                ?? (Prototype is SharpTSClassPrototype prototype && prototype.HasOwnProperty(name));
        }
        if (UsesDynamicPrototypeChain)
            return GetOwnPropertyDescriptor(name) is not null || _interpreter!.HasProperty(Prototype, name);
        // Get resolves live inherited accessor descriptors before its nominal
        // cache. Presence must agree so callers retain that getter's result.
        if (_klass.HasInstanceAccessorDeclaration(name)) return true;
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
    public IEnumerable<string> GetFieldNames()
        => _fields.Keys
            // A deferred enumerator may retain an older store after deletion.
            .Where(name => _fields.ContainsKey(name))
            .Concat((_getters?.Keys as IEnumerable<string> ?? Enumerable.Empty<string>())
                .Where(name => _getters?.ContainsKey(name) == true))
            .Concat((_setters?.Keys as IEnumerable<string> ?? Enumerable.Empty<string>())
                .Where(name => _setters?.ContainsKey(name) == true))
            .Distinct();

    /// <summary>
    /// Enumerates own string keys whose descriptors are enumerable.
    /// Built-in instances can carry non-enumerable internal fields alongside
    /// ordinary user-assigned properties, so spec algorithms must not treat
    /// every backing field as enumerable.
    /// </summary>
    internal IEnumerable<string> OwnEnumerableKeys()
    {
        foreach (var key in GetFieldNames())
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
        // A base constructor can read this name before a derived field exists.
        // Re-resolve after the successful write, preserving own accessor precedence.
        _lookupCache.Remove(name);
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
        _lookupCache.Remove(name);
    }

    /// <summary>
    /// Removes a field by name. Respects frozen/sealed state.
    /// </summary>
    public bool DeleteField(string name)
    {
        if (IsFrozen || IsSealed || !IsConfigurableField(name))
        {
            return false;
        }
        return RemoveField(name);
    }

    /// <summary>
    /// False when <paramref name="name"/> carries an explicit
    /// <c>configurable: false</c> descriptor from <c>Object.defineProperty</c>. ECMA-262
    /// §10.1.10 makes such a property undeletable; without this check
    /// <c>delete</c> removed it and the property silently reappeared as configurable to
    /// <c>propertyHelper.js</c>'s delete-based probe.
    /// </summary>
    private bool IsConfigurableField(string name)
        => _descriptors is null
            || !_descriptors.TryGetValue(name, out var flags)
            || !flags.HasExplicitDescriptor
            || flags.Configurable;

    /// <summary>Drops an own data/accessor property plus its descriptor flags and stale resolution-cache entry.</summary>
    private bool RemoveField(string name)
    {
        bool removed = _fields.Remove(name);
        if (removed)
        {
            // Dictionary reuses deleted slots; dense survivors make recreation append.
            var fields = new Dictionary<string, object?>(_fields.Count, _fields.Comparer);
            foreach (var field in _fields)
                fields.Add(field.Key, field.Value);
            _fields = fields;
        }
        if (_getters?.Remove(name) == true)
        {
            removed = true;
            var getters = new Dictionary<string, ISharpTSCallable?>(_getters.Count, _getters.Comparer);
            foreach (var getter in _getters)
                getters.Add(getter.Key, getter.Value);
            _getters = getters;
        }
        if (_setters?.Remove(name) == true)
        {
            removed = true;
            var setters = new Dictionary<string, ISharpTSCallable?>(_setters.Count, _setters.Comparer);
            foreach (var setter in _setters)
                setters.Add(setter.Key, setter.Value);
            _setters = setters;
        }
        _descriptors?.Remove(name);
        _lookupCache.Remove(name);
        return removed;
    }

    /// <summary>
    /// Removes a field by name with strict mode behavior.
    /// In strict mode, throws TypeError when an existing own property cannot be deleted.
    /// </summary>
    public bool DeleteFieldStrict(string name, bool strictMode)
    {
        if (GetOwnPropertyDescriptor(name) is null)
        {
            return true;
        }
        if (IsFrozen || IsSealed)
        {
            if (strictMode)
            {
                throw new Exception($"TypeError: Cannot delete property '{name}' of a frozen or sealed object");
            }
            return false;
        }
        if (!IsConfigurableField(name))
        {
            if (strictMode)
            {
                throw new Exception($"TypeError: Cannot delete property '{name}'");
            }
            return false;
        }
        return RemoveField(name);
    }

    /// <inheritdoc />
    public void SetProperty(string name, object? value) => SetRawField(name, value);

    /// <summary>
    /// Gets a value by symbol key.
    /// </summary>
    public object? GetBySymbol(SharpTSSymbol symbol)
    {
        return _symbolProperties?.GetBySymbol(symbol);
    }

    private SharpTSObject SymbolProperties
    {
        get
        {
            if (_symbolProperties is not null) return _symbolProperties;
            var properties = _symbolProperties = new SharpTSObject([]);
            if (IsFrozen) properties.Freeze();
            else if (IsSealed) properties.Seal();
            else if (!IsExtensible) properties.PreventExtensions();
            return properties;
        }
    }

    internal bool DefineProperty(SharpTSSymbol symbol, SharpTSPropertyDescriptor descriptor)
        => SymbolProperties.DefineProperty(symbol, descriptor);

    internal SharpTSPropertyDescriptor? GetOwnPropertyDescriptor(SharpTSSymbol symbol)
        => _symbolProperties?.GetOwnPropertyDescriptor(symbol);

    /// <summary>
    /// Sets a value by symbol key. Respects frozen/extensible state.
    /// </summary>
    public void SetBySymbol(SharpTSSymbol symbol, object? value)
        => SymbolProperties.SetBySymbol(symbol, value);

    internal bool DefineDataField(SharpTSSymbol symbol, object? value)
        => DefineProperty(symbol, new SharpTSPropertyDescriptor(value,
            writable: true, enumerable: true, configurable: true) { HasValue = true });

    /// <summary>
    /// Sets a value by symbol key with strict mode behavior.
    /// </summary>
    public void SetBySymbolStrict(SharpTSSymbol symbol, object? value, bool strictMode)
        => SymbolProperties.SetBySymbolStrict(symbol, value, strictMode);

    /// <summary>
    /// Removes a property by symbol key. Respects frozen/sealed state.
    /// </summary>
    public bool DeleteBySymbol(SharpTSSymbol symbol)
        => DeleteBySymbolStrict(symbol, strictMode: false);

    /// <summary>
    /// Removes a property by symbol key with strict mode behavior.
    /// </summary>
    public bool DeleteBySymbolStrict(SharpTSSymbol symbol, bool strictMode)
        => _symbolProperties?.DeleteBySymbolStrict(symbol, strictMode) ?? true;

    /// <summary>
    /// Checks if the instance has a property with the given symbol key.
    /// </summary>
    public bool HasSymbolProperty(SharpTSSymbol symbol)
    {
        return _symbolProperties?.HasSymbolProperty(symbol) == true;
    }

    /// <summary>
    /// Defines or modifies a property with the given descriptor.
    /// Returns true on success, false if the operation is not allowed.
    /// Note: For class instances, this only affects instance fields, not class methods/getters/setters.
    /// </summary>
    public bool DefineProperty(string name, SharpTSPropertyDescriptor descriptor)
    {
        // Get existing descriptor flags if any
        bool hasExisting = _fields.ContainsKey(name)
            || (_getters?.ContainsKey(name) ?? false)
            || (_setters?.ContainsKey(name) ?? false);
        bool existingIsAccessor = (_getters?.ContainsKey(name) ?? false)
            || (_setters?.ContainsKey(name) ?? false);
        bool descriptorIsAccessor = descriptor.HasGet || descriptor.HasSet;
        bool descriptorIsData = descriptor.HasValue || descriptor.HasWritable;
        bool isAccessor = descriptorIsAccessor || (existingIsAccessor && !descriptorIsData);
        PropertyDescriptorFlags existingFlags = default;

        if (hasExisting && _descriptors?.TryGetValue(name, out existingFlags) != true)
        {
            existingFlags = PropertyDescriptorFlags.Default;
        }

        // Check if we can modify the property
        if (hasExisting && existingFlags.HasExplicitDescriptor && !existingFlags.Configurable)
        {
            if ((descriptor.HasConfigurable && descriptor.Configurable) ||
                (descriptor.HasEnumerable && descriptor.Enumerable != existingFlags.Enumerable))
            {
                return false;
            }
            if (existingIsAccessor)
            {
                if (descriptorIsData
                    || descriptor.HasGet && !PropertySemantics.SameValue(descriptor.Get, _getters?.GetValueOrDefault(name))
                    || descriptor.HasSet && !PropertySemantics.SameValue(descriptor.Set, _setters?.GetValueOrDefault(name)))
                    return false;
            }
            else
            {
                if (descriptorIsAccessor) return false;
                if (!existingFlags.Writable
                    && (descriptor.HasWritable && descriptor.Writable
                        || descriptor.HasValue && !PropertySemantics.SameValue(descriptor.Value, _fields[name])))
                    return false;
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
            !isAccessor && (descriptor.HasWritable ? descriptor.Writable
                : hasExisting && !existingIsAccessor && existingFlags.Writable),
            descriptor.HasEnumerable ? descriptor.Enumerable : hasExisting && existingFlags.Enumerable,
            descriptor.HasConfigurable ? descriptor.Configurable : hasExisting && existingFlags.Configurable
        );

        if (isAccessor)
        {
            _fields.Remove(name);
            var getter = descriptor.HasGet ? descriptor.Get
                : existingIsAccessor ? _getters?.GetValueOrDefault(name) : null;
            var setter = descriptor.HasSet ? descriptor.Set
                : existingIsAccessor ? _setters?.GetValueOrDefault(name) : null;
            _getters ??= [];
            _setters ??= [];
            _getters[name] = getter;
            _setters[name] = setter;
        }
        else
        {
            _getters?.Remove(name);
            _setters?.Remove(name);
            if (descriptor.HasValue)
                _fields[name] = descriptor.Value;
            else if (!hasExisting || existingIsAccessor)
                _fields[name] = SharpTSUndefined.Instance;
        }

        // Update lookup cache
        _lookupCache[name] = new PropertyResolution
        {
            Type = isAccessor
                ? ResolutionType.OwnAccessor
                : ResolutionType.Field
        };

        return true;
    }

    /// <summary>
    /// Gets the property descriptor for the given property name.
    /// Returns null if the property doesn't exist as an own property.
    /// Note: Only returns descriptors for instance fields, not class methods/getters/setters.
    /// </summary>
    public SharpTSPropertyDescriptor? GetOwnPropertyDescriptor(string name)
    {
        bool isAccessor = (_getters?.ContainsKey(name) ?? false)
            || (_setters?.ContainsKey(name) ?? false);
        if (!_fields.TryGetValue(name, out var fieldValue) && !isAccessor)
        {
            return null;
        }

        // Get descriptor flags (defaults if not explicitly set)
        PropertyDescriptorFlags flags = default;
        if (_descriptors?.TryGetValue(name, out flags) != true)
        {
            flags = PropertyDescriptorFlags.Default;
        }

        if (isAccessor)
        {
            return new SharpTSPropertyDescriptor
            {
                Get = _getters?.GetValueOrDefault(name),
                Set = _setters?.GetValueOrDefault(name),
                RawGet = _getters?.GetValueOrDefault(name),
                RawSet = _setters?.GetValueOrDefault(name),
                HasGet = true,
                HasSet = true,
                Enumerable = flags.Enumerable,
                HasEnumerable = true,
                Configurable = flags.Configurable,
                HasConfigurable = true,
            };
        }

        return new SharpTSPropertyDescriptor
        {
            Value = fieldValue,
            HasValue = true,
            Writable = flags.Writable,
            HasWritable = true,
            Enumerable = flags.Enumerable,
            HasEnumerable = true,
            Configurable = flags.Configurable,
            HasConfigurable = true,
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

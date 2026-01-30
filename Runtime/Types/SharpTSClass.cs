using SharpTS.Execution;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using System.Collections.Frozen;
using System.Runtime.CompilerServices;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of a TypeScript class definition.
/// </summary>
/// <remarks>
/// Stores class metadata: name, superclass reference, instance methods, static methods,
/// static properties, and getter/setter accessors. Implements <see cref="ISharpTSCallable"/>
/// so that calling a class (e.g., <c>new MyClass()</c>) constructs a new <see cref="SharpTSInstance"/>.
/// Method and property lookups walk the inheritance chain via <see cref="Superclass"/>.
/// </remarks>
/// <seealso cref="SharpTSInstance"/>
/// <seealso cref="SharpTSFunction"/>
public class SharpTSClass(
    string name,
    SharpTSClass? superclass,
    Dictionary<string, SharpTSFunction> methods,
    Dictionary<string, SharpTSFunction> staticMethods,
    Dictionary<string, object?> staticProperties,
    Dictionary<string, SharpTSFunction>? getters = null,
    Dictionary<string, SharpTSFunction>? setters = null,
    bool isAbstract = false,
    List<Stmt.Field>? instanceFields = null,
    List<Stmt.Field>? instancePrivateFields = null,
    Dictionary<string, SharpTSFunction>? privateMethods = null,
    Dictionary<string, object?>? staticPrivateFields = null,
    Dictionary<string, SharpTSFunction>? staticPrivateMethods = null,
    List<Stmt.AutoAccessor>? instanceAutoAccessors = null,
    Dictionary<string, object?>? staticAutoAccessors = null) : ISharpTSCallable, ITypeCategorized
{
    /// <inheritdoc />
    public TypeCategory RuntimeCategory => TypeCategory.Class;

    public string Name { get; } = name;
    public SharpTSClass? Superclass { get; } = superclass;
    public bool IsAbstract { get; } = isAbstract;
    private readonly FrozenDictionary<string, SharpTSFunction> _methods = methods.ToFrozenDictionary();
    private readonly FrozenDictionary<string, SharpTSFunction> _staticMethods = staticMethods.ToFrozenDictionary();
    private readonly Dictionary<string, object?> _staticProperties = staticProperties;
    private readonly FrozenDictionary<string, SharpTSFunction> _getters = getters?.ToFrozenDictionary() ?? FrozenDictionary<string, SharpTSFunction>.Empty;
    private readonly FrozenDictionary<string, SharpTSFunction> _setters = setters?.ToFrozenDictionary() ?? FrozenDictionary<string, SharpTSFunction>.Empty;
    private readonly List<Stmt.Field> _instanceFields = instanceFields ?? [];

    // Method lookup cache - avoids repeated inheritance chain walks
    // Key: method name, Value: method (null means not found in entire chain)
    private readonly Dictionary<string, SharpTSFunction?> _methodCache = [];
    private readonly Dictionary<string, SharpTSFunction?> _staticMethodCache = [];
    private readonly Dictionary<string, SharpTSFunction?> _getterCache = [];
    private readonly Dictionary<string, SharpTSFunction?> _setterCache = [];

    // ES2022 Private class elements
    // Instance private storage - ConditionalWeakTable for GC-friendly per-instance storage
    private readonly ConditionalWeakTable<object, Dictionary<string, object?>> _privateFieldStorage = new();
    private readonly List<Stmt.Field> _instancePrivateFields = instancePrivateFields ?? [];
    private readonly FrozenDictionary<string, SharpTSFunction> _privateMethods = privateMethods?.ToFrozenDictionary() ?? FrozenDictionary<string, SharpTSFunction>.Empty;
    private readonly Dictionary<string, object?> _staticPrivateFields = staticPrivateFields ?? [];
    private readonly FrozenDictionary<string, SharpTSFunction> _staticPrivateMethods = staticPrivateMethods?.ToFrozenDictionary() ?? FrozenDictionary<string, SharpTSFunction>.Empty;

    // Auto-accessor backing storage (TypeScript 4.9+)
    // Instance auto-accessor storage - ConditionalWeakTable for per-instance backing values
    private readonly ConditionalWeakTable<object, Dictionary<string, object?>> _autoAccessorStorage = new();
    private readonly List<Stmt.AutoAccessor> _instanceAutoAccessors = instanceAutoAccessors ?? [];
    // Static auto-accessor backing storage
    private readonly Dictionary<string, object?> _staticAutoAccessorStorage = staticAutoAccessors ?? [];

    public int Arity()
    {
        SharpTSFunction? constructor = FindMethod("constructor");
        if (constructor != null) return constructor.Arity();
        return Superclass?.Arity() ?? 0;
    }

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        SharpTSInstance instance = new(this);

        // Initialize instance fields with their initializers (before constructor runs)
        // Superclass fields are initialized first via inheritance chain
        InitializeInstanceFields(interpreter, instance);

        // Initialize ES2022 private fields (not inherited, each class has its own storage)
        InitializePrivateFields(interpreter, instance);

        // Initialize auto-accessor backing storage (before constructor runs)
        InitializeAutoAccessors(interpreter, instance);

        SharpTSFunction? constructor = FindMethod("constructor");
        if (constructor != null)
        {
            constructor.Bind(instance).Call(interpreter, arguments);
        }
        else if (Superclass != null)
        {
            // If no constructor, call super constructor
            Superclass.Call(interpreter, arguments);
        }

        return instance;
    }

    private void InitializeInstanceFields(Interpreter interpreter, SharpTSInstance instance)
    {
        // First initialize superclass fields
        Superclass?.InitializeInstanceFields(interpreter, instance);

        // Then initialize this class's fields
        foreach (var field in _instanceFields)
        {
            if (field.Initializer != null)
            {
                object? value = interpreter.Evaluate(field.Initializer);
                instance.SetRawField(field.Name.Lexeme, value);
            }
            else
            {
                // Fields without initializers are set to null/undefined
                instance.SetRawField(field.Name.Lexeme, null);
            }
        }
    }

    public SharpTSFunction? FindMethod(string name)
    {
        // Check cache first
        if (_methodCache.TryGetValue(name, out SharpTSFunction? cached))
        {
            return cached;
        }

        // Look up in this class's methods
        if (_methods.TryGetValue(name, out SharpTSFunction? method))
        {
            _methodCache[name] = method;
            return method;
        }

        // Look up in superclass chain
        var result = Superclass?.FindMethod(name);
        _methodCache[name] = result;
        return result;
    }

    public SharpTSFunction? FindStaticMethod(string name)
    {
        // Check cache first
        if (_staticMethodCache.TryGetValue(name, out SharpTSFunction? cached))
        {
            return cached;
        }

        // Look up in this class's static methods
        if (_staticMethods.TryGetValue(name, out SharpTSFunction? method))
        {
            _staticMethodCache[name] = method;
            return method;
        }

        // Look up in superclass chain
        var result = Superclass?.FindStaticMethod(name);
        _staticMethodCache[name] = result;
        return result;
    }

    public object? GetStaticProperty(string name)
    {
        if (_staticProperties.TryGetValue(name, out var value))
        {
            return value;
        }

        return Superclass?.GetStaticProperty(name);
    }

    public bool HasStaticProperty(string name)
    {
        if (_staticProperties.ContainsKey(name))
        {
            return true;
        }

        return Superclass?.HasStaticProperty(name) ?? false;
    }

    public void SetStaticProperty(string name, object? value)
    {
        _staticProperties[name] = value;
    }

    public SharpTSFunction? FindGetter(string name)
    {
        // Check cache first
        if (_getterCache.TryGetValue(name, out SharpTSFunction? cached))
        {
            return cached;
        }

        // Look up in this class's getters
        if (_getters.TryGetValue(name, out SharpTSFunction? getter))
        {
            _getterCache[name] = getter;
            return getter;
        }

        // Look up in superclass chain
        var result = Superclass?.FindGetter(name);
        _getterCache[name] = result;
        return result;
    }

    public SharpTSFunction? FindSetter(string name)
    {
        // Check cache first
        if (_setterCache.TryGetValue(name, out SharpTSFunction? cached))
        {
            return cached;
        }

        // Look up in this class's setters
        if (_setters.TryGetValue(name, out SharpTSFunction? setter))
        {
            _setterCache[name] = setter;
            return setter;
        }

        // Look up in superclass chain
        var result = Superclass?.FindSetter(name);
        _setterCache[name] = result;
        return result;
    }

    public bool HasGetter(string name) => _getters.ContainsKey(name) || (Superclass?.HasGetter(name) ?? false);
    public bool HasSetter(string name) => _setters.ContainsKey(name) || (Superclass?.HasSetter(name) ?? false);

    #region ES2022 Private Class Elements

    /// <summary>
    /// Initializes private fields for an instance. Each class in the hierarchy
    /// has its own private field storage - private fields are NOT inherited.
    /// </summary>
    private void InitializePrivateFields(Interpreter interpreter, SharpTSInstance instance)
    {
        // Note: Private fields are NOT inherited, so we don't call superclass here
        // Each class's private fields are completely independent

        var fields = new Dictionary<string, object?>();
        foreach (var field in _instancePrivateFields)
        {
            object? value = field.Initializer != null
                ? interpreter.Evaluate(field.Initializer)
                : null;
            fields[field.Name.Lexeme] = value;
        }

        // Add to ConditionalWeakTable - GC-friendly storage
        _privateFieldStorage.Add(instance, fields);
    }

    /// <summary>
    /// Gets a private field value from an instance.
    /// Throws if the instance doesn't have this class's private fields (brand check).
    /// </summary>
    public object? GetPrivateField(object instance, string name)
    {
        if (!_privateFieldStorage.TryGetValue(instance, out var fields))
        {
            throw new Exception($"TypeError: Cannot read private member {name} from an object whose class did not declare it");
        }

        if (!fields.TryGetValue(name, out var value))
        {
            throw new Exception($"TypeError: Private field {name} not found");
        }

        return value;
    }

    /// <summary>
    /// Sets a private field value on an instance.
    /// Throws if the instance doesn't have this class's private fields (brand check).
    /// </summary>
    public void SetPrivateField(object instance, string name, object? value)
    {
        if (!_privateFieldStorage.TryGetValue(instance, out var fields))
        {
            throw new Exception($"TypeError: Cannot write private member {name} to an object whose class did not declare it");
        }

        fields[name] = value;
    }

    /// <summary>
    /// Gets a private instance method. Private methods are NOT inherited.
    /// </summary>
    public SharpTSFunction? GetPrivateMethod(string name)
    {
        return _privateMethods.GetValueOrDefault(name);
    }

    /// <summary>
    /// Gets a static private field value.
    /// </summary>
    public object? GetStaticPrivateField(string name)
    {
        if (!_staticPrivateFields.TryGetValue(name, out var value))
        {
            throw new Exception($"TypeError: Static private field {name} not found");
        }
        return value;
    }

    /// <summary>
    /// Sets a static private field value.
    /// </summary>
    public void SetStaticPrivateField(string name, object? value)
    {
        _staticPrivateFields[name] = value;
    }

    /// <summary>
    /// Gets a static private method. Static private methods are NOT inherited.
    /// </summary>
    public SharpTSFunction? GetStaticPrivateMethod(string name)
    {
        return _staticPrivateMethods.GetValueOrDefault(name);
    }

    /// <summary>
    /// Checks if this class has an instance private field with the given name.
    /// </summary>
    public bool HasInstancePrivateField(string name)
    {
        return _instancePrivateFields.Any(f => f.Name.Lexeme == name);
    }

    /// <summary>
    /// Checks if this class has an instance private method with the given name.
    /// </summary>
    public bool HasPrivateMethod(string name)
    {
        return _privateMethods.ContainsKey(name);
    }

    /// <summary>
    /// Checks if this class has a static private field with the given name.
    /// </summary>
    public bool HasStaticPrivateField(string name)
    {
        return _staticPrivateFields.ContainsKey(name);
    }

    /// <summary>
    /// Checks if this class has a static private method with the given name.
    /// </summary>
    public bool HasStaticPrivateMethod(string name)
    {
        return _staticPrivateMethods.ContainsKey(name);
    }

    #endregion

    #region Auto-Accessor Support (TypeScript 4.9+)

    /// <summary>
    /// Initializes auto-accessor backing storage for an instance.
    /// Auto-accessors are inherited through the superclass chain.
    /// </summary>
    private void InitializeAutoAccessors(Interpreter interpreter, SharpTSInstance instance)
    {
        // First initialize superclass auto-accessors
        Superclass?.InitializeAutoAccessors(interpreter, instance);

        // Then initialize this class's auto-accessors
        if (_instanceAutoAccessors.Count == 0)
            return;

        var storage = new Dictionary<string, object?>();
        foreach (var autoAccessor in _instanceAutoAccessors)
        {
            object? value = autoAccessor.Initializer != null
                ? interpreter.Evaluate(autoAccessor.Initializer)
                : null;
            storage[autoAccessor.Name.Lexeme] = value;
        }

        // Add to ConditionalWeakTable - GC-friendly storage
        _autoAccessorStorage.Add(instance, storage);
    }

    /// <summary>
    /// Gets an auto-accessor backing value for an instance.
    /// </summary>
    public object? GetAutoAccessorValue(object instance, string name)
    {
        if (_autoAccessorStorage.TryGetValue(instance, out var storage) &&
            storage.TryGetValue(name, out var value))
        {
            return value;
        }

        // Check superclass
        return Superclass?.GetAutoAccessorValue(instance, name);
    }

    /// <summary>
    /// Sets an auto-accessor backing value for an instance.
    /// </summary>
    public void SetAutoAccessorValue(object instance, string name, object? value)
    {
        if (_autoAccessorStorage.TryGetValue(instance, out var storage) &&
            storage.ContainsKey(name))
        {
            storage[name] = value;
            return;
        }

        // Check superclass
        Superclass?.SetAutoAccessorValue(instance, name, value);
    }

    /// <summary>
    /// Checks if this class has an instance auto-accessor with the given name.
    /// </summary>
    public bool HasInstanceAutoAccessor(string name)
    {
        return _instanceAutoAccessors.Any(a => a.Name.Lexeme == name) ||
               (Superclass?.HasInstanceAutoAccessor(name) ?? false);
    }

    /// <summary>
    /// Gets the auto-accessor declaration for readonly checking.
    /// </summary>
    public Stmt.AutoAccessor? GetAutoAccessorDeclaration(string name)
    {
        var accessor = _instanceAutoAccessors.FirstOrDefault(a => a.Name.Lexeme == name);
        if (accessor != null) return accessor;
        return Superclass?.GetAutoAccessorDeclaration(name);
    }

    /// <summary>
    /// Gets a static auto-accessor backing value.
    /// </summary>
    public object? GetStaticAutoAccessorValue(string name)
    {
        if (_staticAutoAccessorStorage.TryGetValue(name, out var value))
        {
            return value;
        }
        return Superclass?.GetStaticAutoAccessorValue(name);
    }

    /// <summary>
    /// Sets a static auto-accessor backing value.
    /// </summary>
    public void SetStaticAutoAccessorValue(string name, object? value)
    {
        if (_staticAutoAccessorStorage.ContainsKey(name))
        {
            _staticAutoAccessorStorage[name] = value;
            return;
        }
        Superclass?.SetStaticAutoAccessorValue(name, value);
    }

    /// <summary>
    /// Checks if this class has a static auto-accessor with the given name.
    /// </summary>
    public bool HasStaticAutoAccessor(string name)
    {
        return _staticAutoAccessorStorage.ContainsKey(name) ||
               (Superclass?.HasStaticAutoAccessor(name) ?? false);
    }

    #endregion

    public override string ToString() => Name;
}

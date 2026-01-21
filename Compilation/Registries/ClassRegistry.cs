using System.Reflection.Emit;

namespace SharpTS.Compilation.Registries;

/// <summary>
/// Provides centralized lookup and management of class-related compilation state.
/// Wraps the raw dictionaries from ClassCompilationState with clean, encapsulated methods.
/// </summary>
/// <remarks>
/// This registry consolidates class lookups that were previously scattered across ILEmitter files
/// with repeated null checks. It walks inheritance chains for instance member resolution
/// and handles module-qualified name resolution.
/// </remarks>
public sealed class ClassRegistry
{
    // Core class builders
    private readonly Dictionary<string, TypeBuilder> _builders;
    private readonly Dictionary<string, Type> _externalTypes;
    private readonly Dictionary<string, string?> _superclass;
    private readonly Dictionary<string, ConstructorBuilder> _constructors;
    private readonly Dictionary<string, List<ConstructorBuilder>> _constructorOverloads;

    // Instance members
    private readonly Dictionary<string, Dictionary<string, MethodBuilder>> _instanceMethods;
    private readonly Dictionary<string, Dictionary<string, MethodBuilder>> _instanceGetters;
    private readonly Dictionary<string, Dictionary<string, MethodBuilder>> _instanceSetters;

    // Static members
    private readonly Dictionary<string, Dictionary<string, FieldBuilder>> _staticFields;
    private readonly Dictionary<string, Dictionary<string, MethodBuilder>> _staticMethods;
    private readonly Dictionary<string, Dictionary<string, MethodBuilder>> _staticGetters;
    private readonly Dictionary<string, Dictionary<string, MethodBuilder>> _staticSetters;

    // Generic parameters
    private readonly Dictionary<string, GenericTypeParameterBuilder[]> _genericParams;

    // ES2022 Private class elements
    private readonly Dictionary<string, FieldBuilder> _privateFieldStorage;
    private readonly Dictionary<string, List<string>> _privateFieldNames;
    private readonly Dictionary<string, Dictionary<string, FieldBuilder>> _staticPrivateFields;
    private readonly Dictionary<string, Dictionary<string, MethodBuilder>> _privateMethods;
    private readonly Dictionary<string, Dictionary<string, MethodBuilder>> _staticPrivateMethods;

    // Module mapping for qualified name resolution
    private readonly Dictionary<string, string>? _classToModule;
    private readonly Func<string?>? _getCurrentModulePath;
    private readonly Func<string?>? _getDotNetNamespace;

    /// <summary>
    /// Creates a new ClassRegistry wrapping the given state dictionaries.
    /// </summary>
    public ClassRegistry(
        Dictionary<string, TypeBuilder> builders,
        Dictionary<string, Type> externalTypes,
        Dictionary<string, string?> superclass,
        Dictionary<string, ConstructorBuilder> constructors,
        Dictionary<string, List<ConstructorBuilder>> constructorOverloads,
        Dictionary<string, Dictionary<string, MethodBuilder>> instanceMethods,
        Dictionary<string, Dictionary<string, MethodBuilder>> instanceGetters,
        Dictionary<string, Dictionary<string, MethodBuilder>> instanceSetters,
        Dictionary<string, Dictionary<string, FieldBuilder>> staticFields,
        Dictionary<string, Dictionary<string, MethodBuilder>> staticMethods,
        Dictionary<string, Dictionary<string, MethodBuilder>> staticGetters,
        Dictionary<string, Dictionary<string, MethodBuilder>> staticSetters,
        Dictionary<string, GenericTypeParameterBuilder[]> genericParams,
        Dictionary<string, FieldBuilder> privateFieldStorage,
        Dictionary<string, List<string>> privateFieldNames,
        Dictionary<string, Dictionary<string, FieldBuilder>> staticPrivateFields,
        Dictionary<string, Dictionary<string, MethodBuilder>> privateMethods,
        Dictionary<string, Dictionary<string, MethodBuilder>> staticPrivateMethods,
        Dictionary<string, string>? classToModule = null,
        Func<string?>? getCurrentModulePath = null,
        Func<string?>? getDotNetNamespace = null)
    {
        _builders = builders;
        _externalTypes = externalTypes;
        _superclass = superclass;
        _constructors = constructors;
        _constructorOverloads = constructorOverloads;
        _instanceMethods = instanceMethods;
        _instanceGetters = instanceGetters;
        _instanceSetters = instanceSetters;
        _staticFields = staticFields;
        _staticMethods = staticMethods;
        _staticGetters = staticGetters;
        _staticSetters = staticSetters;
        _genericParams = genericParams;
        _privateFieldStorage = privateFieldStorage;
        _privateFieldNames = privateFieldNames;
        _staticPrivateFields = staticPrivateFields;
        _privateMethods = privateMethods;
        _staticPrivateMethods = staticPrivateMethods;
        _classToModule = classToModule;
        _getCurrentModulePath = getCurrentModulePath;
        _getDotNetNamespace = getDotNetNamespace;
    }

    #region Name Resolution

    /// <summary>
    /// Resolves a simple class name to its qualified name for lookup.
    /// In multi-module compilation, class names are qualified with their module to avoid collisions.
    /// Also applies .NET namespace prefix if set via @Namespace directive.
    /// </summary>
    public string ResolveClassName(string simpleClassName)
    {
        string baseName;

        // If we have a module mapping, use it to create the qualified name
        if (_classToModule != null && _classToModule.TryGetValue(simpleClassName, out var modulePath))
        {
            string sanitizedModule = SanitizeModuleName(Path.GetFileNameWithoutExtension(modulePath));
            baseName = $"$M_{sanitizedModule}_{simpleClassName}";
        }
        else
        {
            baseName = simpleClassName;
        }

        // Apply .NET namespace if set
        var dotNetNamespace = _getDotNetNamespace?.Invoke();
        if (dotNetNamespace != null)
        {
            return $"{dotNetNamespace}.{baseName}";
        }

        return baseName;
    }

    /// <summary>
    /// Gets the qualified class name for the current module context.
    /// Also applies .NET namespace if set via @Namespace directive.
    /// </summary>
    public string GetQualifiedClassName(string simpleClassName)
    {
        string baseName;
        var currentModulePath = _getCurrentModulePath?.Invoke();

        if (currentModulePath == null)
        {
            baseName = simpleClassName;
        }
        else
        {
            string sanitizedModule = SanitizeModuleName(Path.GetFileNameWithoutExtension(currentModulePath));
            baseName = $"$M_{sanitizedModule}_{simpleClassName}";
        }

        // Apply .NET namespace if set
        var dotNetNamespace = _getDotNetNamespace?.Invoke();
        if (dotNetNamespace != null)
        {
            return $"{dotNetNamespace}.{baseName}";
        }

        return baseName;
    }

    private static string SanitizeModuleName(string name)
    {
        return name.Replace("/", "_").Replace("\\", "_").Replace(".", "_").Replace("-", "_");
    }

    #endregion

    #region Class Lookups

    /// <summary>
    /// Tries to get a class TypeBuilder by its simple name, resolving to qualified name.
    /// </summary>
    public bool TryGetClass(string simpleName, out TypeBuilder? builder)
    {
        var resolvedName = ResolveClassName(simpleName);
        if (_builders.TryGetValue(resolvedName, out var tb))
        {
            builder = tb;
            return true;
        }

        builder = null;
        return false;
    }

    /// <summary>
    /// Tries to get a class TypeBuilder by its already-resolved qualified name.
    /// </summary>
    public bool TryGetClassByQualifiedName(string qualifiedName, out TypeBuilder? builder)
    {
        if (_builders.TryGetValue(qualifiedName, out var tb))
        {
            builder = tb;
            return true;
        }

        builder = null;
        return false;
    }

    /// <summary>
    /// Checks if a class exists with the given simple name.
    /// </summary>
    public bool HasClass(string simpleName)
    {
        var resolvedName = ResolveClassName(simpleName);
        return _builders.ContainsKey(resolvedName);
    }

    /// <summary>
    /// Gets all class builders (for enumeration during finalization).
    /// </summary>
    public IReadOnlyDictionary<string, TypeBuilder> GetAllClasses() => _builders;

    /// <summary>
    /// Gets external .NET types registered for classes (for @DotNetType support).
    /// </summary>
    public bool TryGetExternalType(string className, out Type? type)
    {
        if (_externalTypes.TryGetValue(className, out var t))
        {
            type = t;
            return true;
        }

        type = null;
        return false;
    }

    #endregion

    #region Superclass Resolution

    /// <summary>
    /// Gets the superclass name for a class, if any.
    /// </summary>
    public string? GetSuperclass(string className)
    {
        return _superclass.GetValueOrDefault(className);
    }

    /// <summary>
    /// Checks if a class has a superclass.
    /// </summary>
    public bool HasSuperclass(string className)
    {
        return _superclass.TryGetValue(className, out var super) && super != null;
    }

    #endregion

    #region Constructor Lookups

    /// <summary>
    /// Gets the constructor for a class by its simple name.
    /// </summary>
    public ConstructorBuilder? GetConstructor(string simpleName)
    {
        var resolvedName = ResolveClassName(simpleName);
        return _constructors.GetValueOrDefault(resolvedName);
    }

    /// <summary>
    /// Gets the constructor for a class by its already-resolved qualified name.
    /// </summary>
    public ConstructorBuilder? GetConstructorByQualifiedName(string qualifiedName)
    {
        return _constructors.GetValueOrDefault(qualifiedName);
    }

    /// <summary>
    /// Gets constructor overloads for a class (for default parameter support).
    /// </summary>
    public List<ConstructorBuilder>? GetConstructorOverloads(string qualifiedName)
    {
        return _constructorOverloads.GetValueOrDefault(qualifiedName);
    }

    #endregion

    #region Instance Method Resolution (with inheritance walking)

    /// <summary>
    /// Resolves an instance method by walking up the inheritance chain.
    /// </summary>
    public MethodBuilder? ResolveInstanceMethod(string className, string methodName)
    {
        string? current = className;
        while (current != null)
        {
            if (_instanceMethods.TryGetValue(current, out var methods) &&
                methods.TryGetValue(methodName, out var method))
                return method;
            current = _superclass.GetValueOrDefault(current);
        }
        return null;
    }

    /// <summary>
    /// Resolves an instance getter by walking up the inheritance chain.
    /// </summary>
    public MethodBuilder? ResolveInstanceGetter(string className, string propertyName)
    {
        string? current = className;
        while (current != null)
        {
            if (_instanceGetters.TryGetValue(current, out var getters) &&
                getters.TryGetValue(propertyName, out var getter))
                return getter;
            current = _superclass.GetValueOrDefault(current);
        }
        return null;
    }

    /// <summary>
    /// Resolves an instance setter by walking up the inheritance chain.
    /// </summary>
    public MethodBuilder? ResolveInstanceSetter(string className, string propertyName)
    {
        string? current = className;
        while (current != null)
        {
            if (_instanceSetters.TryGetValue(current, out var setters) &&
                setters.TryGetValue(propertyName, out var setter))
                return setter;
            current = _superclass.GetValueOrDefault(current);
        }
        return null;
    }

    #endregion

    #region Static Member Lookups

    /// <summary>
    /// Tries to get a static field for a class.
    /// </summary>
    public bool TryGetStaticField(string qualifiedClassName, string fieldName, out FieldBuilder? field)
    {
        if (_staticFields.TryGetValue(qualifiedClassName, out var classFields) &&
            classFields.TryGetValue(fieldName, out var f))
        {
            field = f;
            return true;
        }

        field = null;
        return false;
    }

    /// <summary>
    /// Tries to get a static method for a class.
    /// </summary>
    public bool TryGetStaticMethod(string qualifiedClassName, string methodName, out MethodBuilder? method)
    {
        if (_staticMethods.TryGetValue(qualifiedClassName, out var classMethods) &&
            classMethods.TryGetValue(methodName, out var m))
        {
            method = m;
            return true;
        }

        method = null;
        return false;
    }

    /// <summary>
    /// Tries to get a static getter for a class.
    /// </summary>
    public bool TryGetStaticGetter(string qualifiedClassName, string propertyName, out MethodBuilder? getter)
    {
        if (_staticGetters.TryGetValue(qualifiedClassName, out var classGetters) &&
            classGetters.TryGetValue(propertyName, out var g))
        {
            getter = g;
            return true;
        }

        getter = null;
        return false;
    }

    /// <summary>
    /// Tries to get a static setter for a class.
    /// </summary>
    public bool TryGetStaticSetter(string qualifiedClassName, string propertyName, out MethodBuilder? setter)
    {
        if (_staticSetters.TryGetValue(qualifiedClassName, out var classSetters) &&
            classSetters.TryGetValue(propertyName, out var s))
        {
            setter = s;
            return true;
        }

        setter = null;
        return false;
    }

    #endregion

    #region Generic Parameters

    /// <summary>
    /// Gets generic type parameters for a class.
    /// </summary>
    public GenericTypeParameterBuilder[]? GetGenericParams(string qualifiedClassName)
    {
        return _genericParams.GetValueOrDefault(qualifiedClassName);
    }

    #endregion

    #region Private Class Elements (ES2022)

    /// <summary>
    /// Gets the private field storage field (ConditionalWeakTable) for a class.
    /// </summary>
    public FieldBuilder? GetPrivateFieldStorage(string qualifiedClassName)
    {
        return _privateFieldStorage.GetValueOrDefault(qualifiedClassName);
    }

    /// <summary>
    /// Gets the list of private field names for a class.
    /// </summary>
    public List<string>? GetPrivateFieldNames(string qualifiedClassName)
    {
        return _privateFieldNames.GetValueOrDefault(qualifiedClassName);
    }

    /// <summary>
    /// Tries to get a static private field for a class.
    /// </summary>
    public bool TryGetStaticPrivateField(string qualifiedClassName, string fieldName, out FieldBuilder? field)
    {
        if (_staticPrivateFields.TryGetValue(qualifiedClassName, out var classFields) &&
            classFields.TryGetValue(fieldName, out var f))
        {
            field = f;
            return true;
        }

        field = null;
        return false;
    }

    /// <summary>
    /// Tries to get a private instance method for a class.
    /// </summary>
    public bool TryGetPrivateMethod(string qualifiedClassName, string methodName, out MethodBuilder? method)
    {
        if (_privateMethods.TryGetValue(qualifiedClassName, out var classMethods) &&
            classMethods.TryGetValue(methodName, out var m))
        {
            method = m;
            return true;
        }

        method = null;
        return false;
    }

    /// <summary>
    /// Tries to get a static private method for a class.
    /// </summary>
    public bool TryGetStaticPrivateMethod(string qualifiedClassName, string methodName, out MethodBuilder? method)
    {
        if (_staticPrivateMethods.TryGetValue(qualifiedClassName, out var classMethods) &&
            classMethods.TryGetValue(methodName, out var m))
        {
            method = m;
            return true;
        }

        method = null;
        return false;
    }

    #endregion

    #region Raw Dictionary Access (for backward compatibility during migration)

    /// <summary>
    /// Gets the raw static fields dictionary for backward compatibility.
    /// </summary>
    [Obsolete("Use TryGetStaticField instead. This property will be removed in a future version.")]
    public Dictionary<string, Dictionary<string, FieldBuilder>> StaticFields => _staticFields;

    /// <summary>
    /// Gets the raw static methods dictionary for backward compatibility.
    /// </summary>
    [Obsolete("Use TryGetStaticMethod instead. This property will be removed in a future version.")]
    public Dictionary<string, Dictionary<string, MethodBuilder>> StaticMethods => _staticMethods;

    /// <summary>
    /// Gets the raw instance methods dictionary for backward compatibility.
    /// </summary>
    [Obsolete("Use ResolveInstanceMethod instead. This property will be removed in a future version.")]
    public Dictionary<string, Dictionary<string, MethodBuilder>> InstanceMethods => _instanceMethods;

    /// <summary>
    /// Gets the raw instance getters dictionary for backward compatibility.
    /// </summary>
    [Obsolete("Use ResolveInstanceGetter instead. This property will be removed in a future version.")]
    public Dictionary<string, Dictionary<string, MethodBuilder>> InstanceGetters => _instanceGetters;

    /// <summary>
    /// Gets the raw instance setters dictionary for backward compatibility.
    /// </summary>
    [Obsolete("Use ResolveInstanceSetter instead. This property will be removed in a future version.")]
    public Dictionary<string, Dictionary<string, MethodBuilder>> InstanceSetters => _instanceSetters;

    /// <summary>
    /// Gets the raw static getters dictionary for backward compatibility.
    /// </summary>
    [Obsolete("Use TryGetStaticGetter instead. This property will be removed in a future version.")]
    public Dictionary<string, Dictionary<string, MethodBuilder>> StaticGetters => _staticGetters;

    /// <summary>
    /// Gets the raw static setters dictionary for backward compatibility.
    /// </summary>
    [Obsolete("Use TryGetStaticSetter instead. This property will be removed in a future version.")]
    public Dictionary<string, Dictionary<string, MethodBuilder>> StaticSetters => _staticSetters;

    /// <summary>
    /// Gets the raw constructors dictionary for backward compatibility.
    /// </summary>
    [Obsolete("Use GetConstructor or GetConstructorByQualifiedName instead. This property will be removed in a future version.")]
    public Dictionary<string, ConstructorBuilder> Constructors => _constructors;

    /// <summary>
    /// Gets the raw superclass dictionary for backward compatibility.
    /// </summary>
    [Obsolete("Use GetSuperclass instead. This property will be removed in a future version.")]
    public Dictionary<string, string?> Superclass => _superclass;

    #endregion
}

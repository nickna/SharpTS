using System.Reflection;
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
    private readonly Dictionary<string, string?> _superclass;
    private readonly Dictionary<string, ConstructorBuilder> _constructors;

    // Instance members
    private readonly Dictionary<string, Dictionary<string, MethodBuilder>> _instanceMethods;
    private readonly Dictionary<(TypeBuilder Owner, MethodBuilder Parent), (Type Receiver, MethodInfo Target)> _superCallBridges = [];
    private readonly Dictionary<string, Dictionary<string, MethodBuilder>> _instanceGetters;
    private readonly Dictionary<string, Dictionary<string, MethodBuilder>> _instanceSetters;

    // Static members
    private readonly Dictionary<string, Dictionary<string, FieldBuilder>> _staticFields;
    private readonly Dictionary<string, Dictionary<string, MethodBuilder>> _staticMethods;
    private readonly Dictionary<string, Dictionary<string, MethodBuilder>> _staticGetters;
    private readonly Dictionary<string, Dictionary<string, MethodBuilder>> _staticSetters;

    // Generic parameters
    private readonly ClassGenericParameterRegistry _genericParameters;

    // ES2022 Private class elements
    private readonly PrivateClassElementRegistry _privateElements;

    // Module mapping for qualified name resolution
    private readonly Dictionary<string, string>? _classToModule;
    private readonly Func<string?>? _getCurrentModulePath;
    private readonly Func<string?>? _getDotNetNamespace;

    /// <summary>
    /// Creates a new ClassRegistry wrapping the given state dictionaries.
    /// </summary>
    public ClassRegistry(
        Dictionary<string, TypeBuilder> builders,
        Dictionary<string, string?> superclass,
        Dictionary<string, ConstructorBuilder> constructors,
        Dictionary<string, Dictionary<string, MethodBuilder>> instanceMethods,
        Dictionary<string, Dictionary<string, MethodBuilder>> instanceGetters,
        Dictionary<string, Dictionary<string, MethodBuilder>> instanceSetters,
        Dictionary<string, Dictionary<string, FieldBuilder>> staticFields,
        Dictionary<string, Dictionary<string, MethodBuilder>> staticMethods,
        Dictionary<string, Dictionary<string, MethodBuilder>> staticGetters,
        Dictionary<string, Dictionary<string, MethodBuilder>> staticSetters,
        ClassGenericParameterRegistry genericParameters,
        PrivateClassElementRegistry privateElements,
        Dictionary<string, string>? classToModule = null,
        Func<string?>? getCurrentModulePath = null,
        Func<string?>? getDotNetNamespace = null)
    {
        _builders = builders;
        _superclass = superclass;
        _constructors = constructors;
        _instanceMethods = instanceMethods;
        _instanceGetters = instanceGetters;
        _instanceSetters = instanceSetters;
        _staticFields = staticFields;
        _staticMethods = staticMethods;
        _staticGetters = staticGetters;
        _staticSetters = staticSetters;
        _genericParameters = genericParameters;
        _privateElements = privateElements;
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

    #endregion

    #region Superclass Resolution

    /// <summary>
    /// Gets the superclass name for a class, if any.
    /// </summary>
    public string? GetSuperclass(string className)
    {
        return _superclass.GetValueOrDefault(className);
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

    #endregion

    #region Instance Method Resolution (with inheritance walking)

    /// <summary>
    /// State-machine methods cannot make a nonvirtual call to a virtual parent method:
    /// the CLR requires that call's receiver to be the enclosing class's own this.
    /// Keep that call in a compiler-only method on the lexical class.
    /// The bridge is absent from the published JavaScript method registry.
    /// </summary>
    internal (Type Receiver, MethodInfo Target) GetOrCreateSuperCallBridge(TypeBuilder owner, MethodBuilder parent)
    {
        var key = (owner, parent);
        if (_superCallBridges.TryGetValue(key, out var cached))
            return cached;

        if (!EmitterTypeHelpers.TryResolveSuperCall(owner, parent, owner, out var target))
            throw new InvalidOperationException($"Cannot resolve super method {parent.Name} on {owner.Name}.");

        var parameters = parent.GetParameters();
        var attributes = MethodAttributes.Assembly | MethodAttributes.HideBySig;
        Type receiver = owner;
        MethodBuilder? contractMethod = null;
        var parameterTypes = parameters.Select(parameter => parameter.ParameterType).ToArray();
        if (owner.IsGenericTypeDefinition)
        {
            // The state machine has no enclosing class type parameters in scope.
            // A nongeneric contract reaches the bridge on every closed owner type,
            // retaining that owner's actual base instantiation and receiver.
            var contract = ((ModuleBuilder)owner.Module).DefineType(
                $"<>SuperCall${_superCallBridges.Count}",
                TypeAttributes.Interface | TypeAttributes.Abstract | TypeAttributes.NotPublic);
            contractMethod = contract.DefineMethod("Invoke",
                MethodAttributes.Public | MethodAttributes.Abstract | MethodAttributes.Virtual,
                parent.ReturnType, parameterTypes);
            contract.CreateType();
            EmitTypeDefinitions.AddInterfaceImplementation(owner, contract);
            receiver = contract;
            attributes |= MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.NewSlot;
        }

        var bridge = owner.DefineMethod($"<>super${_superCallBridges.Count}", attributes,
            parent.ReturnType, parameterTypes);
        if (contractMethod != null)
            owner.DefineMethodOverride(bridge, contractMethod);
        var il = bridge.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        for (int i = 0; i < parameters.Length; i++)
            il.Emit(OpCodes.Ldarg, i + 1);
        il.Emit(OpCodes.Call, target);
        il.Emit(OpCodes.Ret);
        var result = (receiver, (MethodInfo?)contractMethod ?? bridge);
        _superCallBridges.Add(key, result);
        return result;
    }

    /// <summary>
    /// Resolves an instance method by walking up the inheritance chain.
    /// </summary>
    public MethodBuilder? ResolveInstanceMethod(string className, string methodName)
    {
        return TryResolveInstanceMethod(className, methodName, out _, out var method)
            ? method
            : null;
    }

    /// <summary>
    /// Resolves an instance method and the emitted class that declares it by walking
    /// up the inheritance chain.
    /// </summary>
    public bool TryResolveInstanceMethod(
        string className,
        string methodName,
        out string declaringClassName,
        out MethodBuilder method)
    {
        string? current = className;
        while (current != null)
        {
            if (_instanceMethods.TryGetValue(current, out var methods) &&
                methods.TryGetValue(methodName, out var resolvedMethod))
            {
                declaringClassName = current;
                method = resolvedMethod;
                return true;
            }
            current = _superclass.GetValueOrDefault(current);
        }
        declaringClassName = null!;
        method = null!;
        return false;
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
    /// Walks the superclass chain (starting at <paramref name="qualifiedClassName"/>) looking for the
    /// nearest class that declares the named member in <paramref name="table"/>. Static members are
    /// inherited by subclasses in TS/JS, but the underlying .NET token references the *declaring* class,
    /// so callers need both the member and the class that owns it. The walk starts at the requested
    /// class so a subclass redeclaration (shadowing) wins over an inherited member.
    /// </summary>
    private bool TryResolveStaticMember<T>(
        Dictionary<string, Dictionary<string, T>> table,
        string qualifiedClassName,
        string memberName,
        out string declaringClass,
        out T member)
    {
        string? current = qualifiedClassName;
        for (int depth = 0; current != null && depth < 64; depth++)
        {
            if (table.TryGetValue(current, out var members) && members.TryGetValue(memberName, out var m))
            {
                declaringClass = current;
                member = m;
                return true;
            }
            current = _superclass.GetValueOrDefault(current);
        }

        declaringClass = null!;
        member = default!;
        return false;
    }

    /// <summary>
    /// When <paramref name="declaringClass"/> is generic, returns its TypeBuilder closed over
    /// <c>object</c> for every type parameter (matching the type-erased instantiation used everywhere
    /// else for class statics), so the resolved member token targets a concrete type. Returns null for
    /// non-generic declaring classes, in which case the raw builder/token is already concrete.
    /// </summary>
    private Type? GetClosedGenericDeclaringType(string declaringClass, string requestedClass, TypeBuilder requestedBuilder)
    {
        // For the requested (own) class the caller already handed us the builder; otherwise look up
        // the declaring base's builder. Both are entries in the same _builders dictionary.
        var declaringBuilder = declaringClass == requestedClass
            ? requestedBuilder
            : _builders.GetValueOrDefault(declaringClass);
        if (declaringBuilder == null)
            return null;

        var parameters = _genericParameters.Require(declaringBuilder);
        if (parameters.Count == 0)
            return null;
        var typeArgs = new Type[parameters.Count];
        for (int i = 0; i < parameters.Count; i++)
            typeArgs[i] = typeof(object);
        return EmitGenerics.MakeGenericType(declaringBuilder, typeArgs);
    }

    /// <summary>
    /// Tries to get a static field declared directly on the class (no superclass walk). Used by
    /// write/read-modify-write sites that must bind only to a field the class itself declares.
    /// </summary>
    public bool TryGetOwnStaticField(string qualifiedClassName, string fieldName, out FieldBuilder? field)
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
    /// True when the class (directly or transitively) extends the built-in
    /// Promise (#242) — its superclass chain ends at the name "Promise"
    /// without a user class claiming that name. Used for static-side
    /// inheritance of the Promise built-ins (MyPromise.resolve etc.).
    /// </summary>
    public bool IsPromiseSubclass(string qualifiedClassName)
    {
        string? current = qualifiedClassName;
        for (int depth = 0; current != null && depth < 64; depth++)
        {
            if (!_superclass.TryGetValue(current, out var superName) || superName == null)
                return false;
            if (!_builders.ContainsKey(superName))
                return superName == "Promise";
            current = superName;
        }
        return false;
    }

    /// <summary>
    /// Tries to get a static getter for a class.
    /// </summary>
    public bool TryGetStaticGetter(string qualifiedClassName, string propertyName, out MethodBuilder? getter)
    {
        if (TryResolveStaticMember(_staticGetters, qualifiedClassName, propertyName, out _, out var g))
        {
            getter = g;
            return true;
        }

        getter = null;
        return false;
    }

    #endregion

    #region Generic Parameters

    /// <summary>
    /// Gets generic type parameters for a class.
    /// </summary>
    public IReadOnlyList<GenericTypeParameterBuilder>? GetGenericParams(string qualifiedClassName)
    {
        if (!_builders.TryGetValue(qualifiedClassName, out var owner))
            return null;
        var parameters = _genericParameters.Require(owner);
        return parameters.Count == 0 ? null : parameters;
    }

    #endregion

    #region Private Class Elements (ES2022)

    /// <summary>
    /// Gets the private field storage field (ConditionalWeakTable) for a class.
    /// </summary>
    public FieldBuilder? GetPrivateFieldStorage(string qualifiedClassName)
    {
        return _privateElements.TryGet(qualifiedClassName, out var declaration) ? declaration.Storage : null;
    }

    public PrivateInstanceBridge? GetPrivateInstanceBridge(string qualifiedClassName)
        => _privateElements.TryGet(qualifiedClassName, out var declaration) ? declaration.InstanceBridge : null;

    /// <summary>
    /// Gets the list of private field names for a class.
    /// </summary>
    public IReadOnlyList<string>? GetPrivateFieldNames(string qualifiedClassName)
    {
        return _privateElements.TryGet(qualifiedClassName, out var declaration) ? declaration.FieldNames : null;
    }

    /// <summary>
    /// Tries to get a static private field for a class.
    /// </summary>
    public bool TryGetStaticPrivateField(string qualifiedClassName, string fieldName, out FieldBuilder? field)
    {
        if (_privateElements.TryGet(qualifiedClassName, out var declaration) &&
            declaration.StaticFields.TryGetValue(fieldName, out var f))
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
        if (_privateElements.TryGet(qualifiedClassName, out var declaration) &&
            declaration.Methods.TryGetValue(methodName, out var m))
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
        if (_privateElements.TryGet(qualifiedClassName, out var declaration) &&
            declaration.StaticMethods.TryGetValue(methodName, out var m))
        {
            method = m;
            return true;
        }

        method = null;
        return false;
    }

    /// <summary>Resolves a private static field on its type-erased declaring owner.</summary>
    public bool TryGetCallableStaticPrivateField(string qualifiedClassName, string fieldName, out System.Reflection.FieldInfo? field)
    {
        if (!TryGetStaticPrivateField(qualifiedClassName, fieldName, out var builder))
        {
            field = null;
            return false;
        }

        var closedType = GetClosedGenericDeclaringType(qualifiedClassName, qualifiedClassName, _builders[qualifiedClassName]);
        field = closedType != null ? EmitterTypeHelpers.ResolveField(closedType, builder!) : builder;
        return true;
    }

    /// <summary>Resolves a private static method on its type-erased declaring owner.</summary>
    public bool TryGetCallableStaticPrivateMethod(string qualifiedClassName, string methodName, out System.Reflection.MethodInfo? method)
    {
        if (!TryGetStaticPrivateMethod(qualifiedClassName, methodName, out var builder))
        {
            method = null;
            return false;
        }

        var closedType = GetClosedGenericDeclaringType(qualifiedClassName, qualifiedClassName, _builders[qualifiedClassName]);
        method = closedType != null ? EmitterTypeHelpers.ResolveMethod(closedType, builder!) : builder;
        return true;
    }

    /// <summary>
    /// Gets a static method that can be called, handling generic types by creating a closed generic type.
    /// For non-generic classes, returns the MethodBuilder directly.
    /// For generic classes, returns a MethodInfo from a closed generic type (e.g., Box&lt;object&gt;).
    /// </summary>
    public bool TryGetCallableStaticMethod(string qualifiedClassName, string methodName, TypeBuilder classBuilder, out System.Reflection.MethodInfo? method)
    {
        if (!TryResolveStaticMember(_staticMethods, qualifiedClassName, methodName, out var declaringClass, out var methodBuilder))
        {
            method = null;
            return false;
        }

        // When the method is declared on (or inherited from) a generic class, call it on a closed
        // generic type — the declaring class's, not the requested subclass's.
        var closedType = GetClosedGenericDeclaringType(declaringClass, qualifiedClassName, classBuilder);
        method = closedType != null
            ? EmitterTypeHelpers.ResolveMethod(closedType, methodBuilder!)
            : methodBuilder;
        return true;
    }

    /// <summary>
    /// Gets a static getter that can be called, walking the superclass chain and handling generic
    /// declaring classes by resolving against a closed generic type. For non-generic classes returns
    /// the MethodBuilder directly (its token already references the declaring class).
    /// </summary>
    public bool TryGetCallableStaticGetter(string qualifiedClassName, string propertyName, TypeBuilder classBuilder, out System.Reflection.MethodInfo? getter)
    {
        if (!TryResolveStaticMember(_staticGetters, qualifiedClassName, propertyName, out var declaringClass, out var getterBuilder))
        {
            getter = null;
            return false;
        }

        var closedType = GetClosedGenericDeclaringType(declaringClass, qualifiedClassName, classBuilder);
        getter = closedType != null
            ? EmitterTypeHelpers.ResolveMethod(closedType, getterBuilder!)
            : getterBuilder;
        return true;
    }

    /// <summary>
    /// Gets a static *data field* declared directly on the class (no superclass walk), resolving
    /// against a closed generic type for generic classes. Used by assignment sites: per JS semantics
    /// a write through a subclass (<c>Sub.field = v</c>) creates an own shadow on the subclass and must
    /// NOT mutate the base's storage, so writes only bind to a field the class itself declares.
    /// (Inherited-static-field writes through a subclass would need runtime shadow storage — see #332
    /// follow-up; reads, getters, setters and methods are inherited and use the chain-walking lookups.)
    /// </summary>
    public bool TryGetOwnCallableStaticField(string qualifiedClassName, string fieldName, TypeBuilder classBuilder, out System.Reflection.FieldInfo? field)
    {
        if (!_staticFields.TryGetValue(qualifiedClassName, out var classFields) ||
            !classFields.TryGetValue(fieldName, out var fieldBuilder))
        {
            field = null;
            return false;
        }

        var closedType = GetClosedGenericDeclaringType(qualifiedClassName, qualifiedClassName, classBuilder);
        field = closedType != null
            ? EmitterTypeHelpers.ResolveField(closedType, fieldBuilder)
            : fieldBuilder;
        return true;
    }

    /// <summary>
    /// Gets a static setter that can be called, walking the superclass chain and handling generic
    /// declaring classes by resolving against a closed generic type.
    /// </summary>
    public bool TryGetCallableStaticSetter(string qualifiedClassName, string propertyName, TypeBuilder classBuilder, out System.Reflection.MethodInfo? setter)
    {
        if (!TryResolveStaticMember(_staticSetters, qualifiedClassName, propertyName, out var declaringClass, out var setterBuilder))
        {
            setter = null;
            return false;
        }

        var closedType = GetClosedGenericDeclaringType(declaringClass, qualifiedClassName, classBuilder);
        setter = closedType != null
            ? EmitterTypeHelpers.ResolveMethod(closedType, setterBuilder!)
            : setterBuilder;
        return true;
    }

    /// <summary>
    /// Gets a static field that can be accessed, handling generic types by creating a closed generic type.
    /// For non-generic classes, returns the FieldBuilder directly.
    /// For generic classes, returns a FieldInfo from a closed generic type.
    /// </summary>
    public bool TryGetCallableStaticField(string qualifiedClassName, string fieldName, TypeBuilder classBuilder, out System.Reflection.FieldInfo? field)
    {
        if (!TryResolveStaticMember(_staticFields, qualifiedClassName, fieldName, out var declaringClass, out var fieldBuilder))
        {
            field = null;
            return false;
        }

        // When the field is declared on (or inherited from) a generic class, access it on a closed
        // generic type — the declaring class's, not the requested subclass's.
        var closedType = GetClosedGenericDeclaringType(declaringClass, qualifiedClassName, classBuilder);
        field = closedType != null
            ? EmitterTypeHelpers.ResolveField(closedType, fieldBuilder!)
            : fieldBuilder;
        return true;
    }

    #endregion

}

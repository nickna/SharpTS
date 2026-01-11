using SharpTS.TypeSystem;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Registry that maps TypeInfo types to their corresponding emitter strategies.
/// Provides type-first dispatch for method calls and property access.
/// </summary>
public sealed class TypeEmitterRegistry
{
    private readonly Dictionary<Type, ITypeEmitterStrategy> _instanceStrategies = new();
    private readonly Dictionary<string, IStaticTypeEmitterStrategy> _staticStrategies = new();

    // Special strategies for Instance types that need context-aware dispatch
    private ITypeEmitterStrategy? _externalTypeEmitter;
    private ITypeEmitterStrategy? _classInstanceEmitter;

    // Reference to external types for Instance dispatch
    private IReadOnlyDictionary<string, Type>? _externalTypes;

    /// <summary>
    /// Sets the external types dictionary for Instance type dispatch.
    /// </summary>
    public void SetExternalTypes(IReadOnlyDictionary<string, Type> externalTypes)
    {
        _externalTypes = externalTypes;
    }

    /// <summary>
    /// Registers an emitter strategy for a specific TypeInfo type.
    /// </summary>
    /// <typeparam name="TTypeInfo">The TypeInfo type to handle.</typeparam>
    /// <param name="strategy">The strategy implementation.</param>
    public void Register<TTypeInfo>(ITypeEmitterStrategy strategy) where TTypeInfo : TypeInfo
    {
        _instanceStrategies[typeof(TTypeInfo)] = strategy;
    }

    /// <summary>
    /// Registers the emitter for external .NET types (@DotNetType).
    /// </summary>
    public void RegisterExternalTypeEmitter(ITypeEmitterStrategy strategy)
    {
        _externalTypeEmitter = strategy;
    }

    /// <summary>
    /// Registers the emitter for user-defined class instances.
    /// </summary>
    public void RegisterClassInstanceEmitter(ITypeEmitterStrategy strategy)
    {
        _classInstanceEmitter = strategy;
    }

    /// <summary>
    /// Registers a static type emitter by name (e.g., "Math", "JSON", "Object").
    /// </summary>
    public void RegisterStatic(string typeName, IStaticTypeEmitterStrategy strategy)
    {
        _staticStrategies[typeName] = strategy;
    }

    /// <summary>
    /// Gets the appropriate emitter strategy for the given TypeInfo.
    /// Returns null if no strategy is registered for the type.
    /// </summary>
    public ITypeEmitterStrategy? GetStrategy(TypeInfo typeInfo)
    {
        // Handle Instance types specially - determine if external or user-defined
        if (typeInfo is TypeInfo.Instance instance)
        {
            return GetInstanceStrategy(instance);
        }

        // Look up by TypeInfo's concrete type
        return _instanceStrategies.GetValueOrDefault(typeInfo.GetType());
    }

    /// <summary>
    /// Gets the static emitter strategy for the given type name.
    /// </summary>
    public IStaticTypeEmitterStrategy? GetStaticStrategy(string typeName)
    {
        return _staticStrategies.GetValueOrDefault(typeName);
    }

    /// <summary>
    /// Determines the appropriate strategy for Instance types by checking
    /// whether the class is an external .NET type or a user-defined class.
    /// </summary>
    private ITypeEmitterStrategy? GetInstanceStrategy(TypeInfo.Instance instance)
    {
        // Extract the class name from the instance's class type
        string? className = instance.ClassType switch
        {
            TypeInfo.Class c => c.Name,
            TypeInfo.InstantiatedGeneric ig when ig.GenericDefinition is TypeInfo.GenericClass gc => gc.Name,
            _ => null
        };

        if (className == null)
            return _classInstanceEmitter;

        // Check if this is an external .NET type
        if (_externalTypes?.ContainsKey(className) == true)
            return _externalTypeEmitter;

        // It's a user-defined class
        return _classInstanceEmitter;
    }

    /// <summary>
    /// Checks if a strategy is registered for the given TypeInfo type.
    /// </summary>
    public bool HasStrategy(TypeInfo typeInfo)
    {
        if (typeInfo is TypeInfo.Instance)
            return _externalTypeEmitter != null || _classInstanceEmitter != null;

        return _instanceStrategies.ContainsKey(typeInfo.GetType());
    }

    /// <summary>
    /// Checks if a static strategy is registered for the given type name.
    /// </summary>
    public bool HasStaticStrategy(string typeName)
    {
        return _staticStrategies.ContainsKey(typeName);
    }
}

using SharpTS.Execution;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Provides O(1) member lookup for built-in types with cached method instances.
/// Eliminates per-access allocation by storing pre-built BuiltInMethod instances.
/// </summary>
/// <typeparam name="TReceiver">The receiver type (e.g., SharpTSMap, SharpTSSet)</typeparam>
public sealed class BuiltInTypeMemberLookup<TReceiver>
{
    private readonly Dictionary<string, BuiltInMethod> _methods;
    private readonly Dictionary<string, Func<TReceiver, object?>> _properties;
    private readonly bool _requiresBind;

    internal BuiltInTypeMemberLookup(
        Dictionary<string, BuiltInMethod> methods,
        Dictionary<string, Func<TReceiver, object?>> properties,
        bool requiresBind)
    {
        _methods = methods;
        _properties = properties;
        _requiresBind = requiresBind;
    }

    /// <summary>
    /// Gets a member (property or method) for an instance type.
    /// Methods are bound to the receiver before being returned.
    /// </summary>
    public object? GetMember(TReceiver receiver, string name)
    {
        if (_properties.TryGetValue(name, out var propertyGetter))
        {
            return propertyGetter(receiver);
        }

        if (_methods.TryGetValue(name, out var method))
        {
            return _requiresBind ? method.Bind(receiver) : method;
        }

        return null;
    }

    /// <summary>
    /// Gets a static member (for types like Math, JSON that don't have receivers).
    /// </summary>
    public object? GetStaticMember(string name)
    {
        if (_properties.TryGetValue(name, out var propertyGetter))
        {
            // For static properties, pass default(TReceiver) - typically unused
            return propertyGetter(default!);
        }

        if (_methods.TryGetValue(name, out var method))
        {
            return method;
        }

        return null;
    }
}

/// <summary>
/// Non-generic lookup for static-only types (Math, JSON) that have no receiver.
/// Supports both raw constants (returned as-is) and methods (returned as BuiltInMethod).
/// </summary>
public sealed class BuiltInStaticMemberLookup
{
    private readonly Dictionary<string, BuiltInMethod> _methods;
    private readonly Dictionary<string, object?> _rawConstants;

    internal BuiltInStaticMemberLookup(
        Dictionary<string, BuiltInMethod> methods,
        Dictionary<string, object?> rawConstants)
    {
        _methods = methods;
        _rawConstants = rawConstants;
    }

    /// <summary>
    /// Gets a static member (constant or method).
    /// Raw constants are returned as-is; methods are returned as BuiltInMethod.
    /// </summary>
    public object? GetMember(string name)
    {
        if (_rawConstants.TryGetValue(name, out var constant))
        {
            return constant;
        }

        if (_methods.TryGetValue(name, out var method))
        {
            return method;
        }

        return null;
    }
}

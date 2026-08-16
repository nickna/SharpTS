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
    /// The names of every property and method in this table, for REPL autocomplete.
    /// The dispatch switches themselves are not enumerable, so completion reads the tables directly.
    /// </summary>
    public IEnumerable<string> MemberNames => _properties.Keys.Concat(_methods.Keys);

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
    /// Gets the raw (unbound) <see cref="BuiltInMethod"/> for a method name,
    /// or null if not found. Used by prototype objects (e.g.
    /// <c>Array.prototype</c>) that need to expose the same method instance
    /// as the instance-type lookup so callers can rebind the receiver via
    /// <c>.call</c>/<c>.apply</c>.
    /// </summary>
    public BuiltInMethod? GetMethod(string name)
        => _methods.TryGetValue(name, out var method) ? method : null;
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
    /// The names of every constant and method in this table, for REPL autocomplete.
    /// </summary>
    public IEnumerable<string> MemberNames => _rawConstants.Keys.Concat(_methods.Keys);

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

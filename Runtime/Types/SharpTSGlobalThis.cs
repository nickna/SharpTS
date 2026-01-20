using SharpTS.Runtime.BuiltIns;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Singleton representing the JavaScript globalThis object (ES2020).
/// Provides access to all built-in globals and supports dynamic property assignment.
/// </summary>
/// <remarks>
/// globalThis provides a standard way to access the global object across JavaScript environments.
/// It contains references to all built-in globals (Math, console, JSON, etc.) and provides
/// self-reference (globalThis.globalThis === globalThis).
/// </remarks>
public sealed class SharpTSGlobalThis
{
    /// <summary>
    /// The singleton instance of globalThis.
    /// </summary>
    public static readonly SharpTSGlobalThis Instance = new();

    /// <summary>
    /// User-assigned properties on globalThis.
    /// </summary>
    private readonly Dictionary<string, object?> _properties = new();

    private SharpTSGlobalThis() { }

    /// <summary>
    /// Gets a property from globalThis.
    /// </summary>
    /// <param name="name">The property name.</param>
    /// <returns>
    /// Returns this for "globalThis" (self-reference),
    /// checks user-assigned properties first,
    /// then delegates to BuiltInRegistry for built-in namespaces.
    /// </returns>
    public object? GetProperty(string name)
    {
        // Self-reference: globalThis.globalThis === globalThis
        if (name == "globalThis")
        {
            return this;
        }

        // Check user-assigned properties first
        if (_properties.TryGetValue(name, out var value))
        {
            return value;
        }

        // Delegate to BuiltInRegistry for built-in namespaces (Math, JSON, etc.)
        var singleton = BuiltInRegistry.Instance.GetSingleton(name);
        if (singleton != null)
        {
            return singleton;
        }

        // Check for static methods on namespaces (e.g., globalThis.parseInt)
        // Global functions like parseInt, parseFloat, isNaN, isFinite are on Number namespace
        if (name == "parseInt" || name == "parseFloat" || name == "isNaN" || name == "isFinite")
        {
            return BuiltInRegistry.Instance.GetStaticMethod("Number", name);
        }

        // Built-in constants
        if (name == "undefined")
        {
            return SharpTSUndefined.Instance;
        }
        if (name == "NaN")
        {
            return double.NaN;
        }
        if (name == "Infinity")
        {
            return double.PositiveInfinity;
        }

        // Return undefined for unknown properties
        return SharpTSUndefined.Instance;
    }

    /// <summary>
    /// Sets a property on globalThis.
    /// </summary>
    /// <param name="name">The property name.</param>
    /// <param name="value">The value to set.</param>
    public void SetProperty(string name, object? value)
    {
        _properties[name] = value;
    }

    /// <summary>
    /// Checks if a property exists on globalThis.
    /// </summary>
    /// <param name="name">The property name.</param>
    /// <returns>True if the property exists (either user-assigned or built-in).</returns>
    public bool HasProperty(string name)
    {
        if (name == "globalThis") return true;
        if (_properties.ContainsKey(name)) return true;

        // Check if it's a built-in singleton
        var singleton = BuiltInRegistry.Instance.GetSingleton(name);
        if (singleton != null) return true;

        // Check global functions
        if (name == "parseInt" || name == "parseFloat" || name == "isNaN" || name == "isFinite")
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Gets all user-assigned property names.
    /// </summary>
    public IEnumerable<string> GetUserPropertyNames() => _properties.Keys;

    public override string ToString() => "[object globalThis]";
}

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
public sealed class SharpTSGlobalThis : ISharpTSPropertyAccessor
{
    /// <summary>
    /// Process-wide template instance, retained as a fallback (and the
    /// BuiltInRegistry singleton). Guest reads of <c>globalThis</c> / <c>global</c>
    /// resolve to a per-realm instance (see <c>Interpreter.GlobalThis</c>) so
    /// user-assigned properties (<c>globalThis.x = …</c>) stay realm-local and
    /// don't race across worker threads. Mirrors the per-realm RegExp.prototype
    /// (#101) and Math.
    /// </summary>
    public static readonly SharpTSGlobalThis Instance = new();

    /// <summary>
    /// User-assigned properties on globalThis.
    /// </summary>
    private readonly Dictionary<string, object?> _properties = new();

    // internal (not private) so each Interpreter can construct its own realm
    // global object; only the _properties bag differs between instances.
    internal SharpTSGlobalThis() { }

    /// <summary>
    /// True if guest code has assigned an own (user) property with this name.
    /// A guest assignment (<c>globalThis.Math = x</c>) shadows the built-in of
    /// the same name per ECMA-262, so per-realm intrinsic resolution must defer
    /// to it. Distinct from <see cref="HasProperty"/>, which also reports
    /// built-in globals.
    /// </summary>
    public bool HasUserProperty(string name) => _properties.ContainsKey(name);

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

        // Buffer is globally available
        if (name == "Buffer")
        {
            return SharpTSBufferConstructor.Instance;
        }

        // Array: the bare-reference global constructor (not a singleton namespace).
        if (name == "Array")
        {
            return SharpTSArrayGlobal.Instance;
        }

        // Error class hierarchy — exposed as globals so that CommonJS packages
        // can look up `global.Error`, `global.TypeError`, etc.
        foreach (var errTypeName in BuiltInNames.ErrorTypeNames)
        {
            if (errTypeName == name)
            {
                return new SharpTSErrorClass(errTypeName, null);
            }
        }

        // Constructor-like globals surfaced through BuiltInConstructorFactory so that
        // `globalThis.Map`, `globalThis.Date`, etc. resolve for CommonJS packages
        // (lodash aliases all constructors from the context object).
        foreach (var (ctorName, factory) in BuiltInConstructorFactory.GetConstructors())
        {
            if (ctorName == name)
            {
                return new SharpTSBuiltInConstructor(ctorName, factory);
            }
        }

        // Function constructor placeholder — lodash uses this only for `.prototype`
        // access via `funcProto = Function.prototype` and `funcToString = funcProto.toString`.
        // Returns a minimal constructor that satisfies these lookups.
        if (name == "Function")
        {
            return SharpTSFunctionGlobal.Instance;
        }

        // Global async functions
        if (name == "fetch")
        {
            return SharpTSFetchGlobal.Instance;
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
        if (name == "parseInt" || name == "parseFloat" || name == "isNaN" || name == "isFinite" || name == "fetch")
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Gets all property names (ISharpTSPropertyAccessor).
    /// Returns user-assigned properties plus some well-known built-in globals.
    /// </summary>
    public IEnumerable<string> PropertyNames
    {
        get
        {
            foreach (var key in _properties.Keys)
                yield return key;
            // Also include well-known globals
            yield return "globalThis";
            yield return "Math";
            yield return "JSON";
            yield return "console";
            yield return "Object";
            yield return "Array";
            yield return "Number";
            yield return "String";
            yield return "Boolean";
            yield return "Symbol";
            yield return "Promise";
            yield return "process";
            yield return "Buffer";
            yield return "fetch";
        }
    }

    public override string ToString() => "[object globalThis]";
}

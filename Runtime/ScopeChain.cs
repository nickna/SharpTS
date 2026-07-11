namespace SharpTS.Runtime;

/// <summary>
/// Generic base class for scope chain implementations.
/// </summary>
/// <remarks>
/// Provides the common functionality shared between <see cref="TypeEnvironment"/> and
/// <see cref="RuntimeEnvironment"/>: a dictionary of values, read-only tracking, and
/// scope chain traversal via an enclosing reference. Subclasses add domain-specific
/// behavior (type parameters, namespace handling, etc.).
/// </remarks>
/// <typeparam name="TValue">The type of values stored in the scope</typeparam>
/// <typeparam name="TSelf">The concrete derived type (for covariant Enclosing)</typeparam>
public abstract class ScopeChain<TValue, TSelf> where TSelf : ScopeChain<TValue, TSelf>
{
    protected readonly Dictionary<string, TValue> _values = new(StringComparer.Ordinal);

    // Lazily allocated: only named function expressions mark a read-only name, so the
    // overwhelming majority of scopes never need the set.
    private HashSet<string>? _readOnlyNames;

    /// <summary>
    /// The enclosing scope, or null if this is the global scope.
    /// </summary>
    public TSelf? Enclosing { get; }

    /// <summary>
    /// Whether this environment is in JavaScript strict mode.
    /// Strict mode is inherited from enclosing scopes unless explicitly set.
    /// </summary>
    public bool IsStrictMode { get; }

    protected ScopeChain(TSelf? enclosing = null, bool? strictMode = null)
    {
        Enclosing = enclosing;
        IsStrictMode = strictMode ?? enclosing?.IsStrictMode ?? false;
    }

    /// <summary>
    /// Defines a variable in the current scope.
    /// </summary>
    public void Define(string name, TValue value) => _values[name] = value;

    /// <summary>
    /// Gets a variable value, searching up the scope chain.
    /// </summary>
    public virtual TValue? Get(string name)
    {
        if (_values.TryGetValue(name, out var value))
            return value;
        return Enclosing != null ? Enclosing.Get(name) : default;
    }

    /// <summary>
    /// Checks if a variable is defined in this scope or any enclosing scope.
    /// </summary>
    public bool IsDefined(string name)
    {
        if (_values.ContainsKey(name)) return true;
        return Enclosing?.IsDefined(name) ?? false;
    }

    /// <summary>
    /// Checks if a variable is defined in this scope only (not in enclosing scopes).
    /// Used for function hoisting to avoid re-defining already hoisted functions.
    /// </summary>
    public bool IsDefinedLocally(string name) => _values.ContainsKey(name);

    /// <summary>
    /// Marks a variable as read-only. Used for named function expressions
    /// where the function name cannot be reassigned inside the function body.
    /// </summary>
    public void MarkAsReadOnly(string name) =>
        (_readOnlyNames ??= new(StringComparer.Ordinal)).Add(name);

    /// <summary>
    /// Checks if a variable is read-only in the current or enclosing scopes.
    /// </summary>
    public bool IsReadOnly(string name)
    {
        if (_readOnlyNames?.Contains(name) == true) return true;
        return Enclosing?.IsReadOnly(name) ?? false;
    }
}

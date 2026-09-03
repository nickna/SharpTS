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
    // Most transient scopes never declare a binding (for example a catch without
    // a parameter). Defer the dictionary until the first actual definition.
    protected Dictionary<string, TValue>? _values;
    protected Dictionary<string, TValue> Values =>
        _values ??= new Dictionary<string, TValue>(StringComparer.Ordinal);

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
    /// The names defined directly in this scope, excluding enclosing scopes.
    /// Walk <see cref="Enclosing"/> to enumerate the whole chain.
    /// </summary>
    /// <remarks>
    /// Used by REPL autocomplete to list the bindings currently in scope.
    /// </remarks>
    public virtual IEnumerable<string> Names =>
        _values is null ? Array.Empty<string>() : _values.Keys;

    /// <summary>
    /// Defines a variable in the current scope.
    /// </summary>
    public virtual void Define(string name, TValue value) => Values[name] = value;

    /// <summary>
    /// Gets a variable value, searching up the scope chain.
    /// </summary>
    public virtual TValue? Get(string name)
    {
        if (_values?.TryGetValue(name, out var value) == true)
            return value;
        return Enclosing != null ? Enclosing.Get(name) : default;
    }

    /// <summary>
    /// Checks if a variable is defined in this scope or any enclosing scope.
    /// </summary>
    public virtual bool IsDefined(string name)
    {
        if (_values?.ContainsKey(name) == true) return true;
        return Enclosing?.IsDefined(name) ?? false;
    }

    /// <summary>
    /// Checks if a variable is defined in this scope only (not in enclosing scopes).
    /// Used for function hoisting to avoid re-defining already hoisted functions.
    /// </summary>
    public virtual bool IsDefinedLocally(string name) =>
        _values?.ContainsKey(name) == true;

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

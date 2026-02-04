namespace SharpTS.TypeSystem.Narrowing;

/// <summary>
/// Tracks variables that have "escaped" to outer scopes and could potentially
/// be aliased by other variables. Used to determine when property mutations
/// through one variable should invalidate narrowings on another.
/// </summary>
/// <remarks>
/// A variable "escapes" when:
/// - It's passed as an argument to a function (the function might store a reference)
/// - It's assigned to a variable in an outer scope (global, closure capture)
///
/// When a property is mutated on a variable that could alias an escaped variable,
/// we must invalidate narrowings on that property for the escaped variable.
/// </remarks>
public class EscapeAnalyzer
{
    /// <summary>
    /// Variables that have escaped to an outer scope (passed to function, assigned to global).
    /// </summary>
    private readonly HashSet<string> _escapedVariables = new();

    /// <summary>
    /// Global or outer-scope variables that might hold references to escaped objects.
    /// Maps from the global variable name to the set of local variables it might alias.
    /// </summary>
    private readonly Dictionary<string, HashSet<string>> _globalAliases = new();

    /// <summary>
    /// Current function scope depth. Used to detect assignments to outer scopes.
    /// </summary>
    private int _scopeDepth = 0;

    /// <summary>
    /// Variables defined at each scope depth.
    /// </summary>
    private readonly Stack<HashSet<string>> _scopeVariables = new();

    /// <summary>
    /// Marks a variable as having escaped (passed to a function).
    /// After escaping, mutations through any potential alias should invalidate
    /// narrowings on this variable.
    /// </summary>
    public void MarkEscaped(string variableName)
    {
        _escapedVariables.Add(variableName);
    }

    /// <summary>
    /// Checks if a variable has escaped.
    /// </summary>
    public bool IsEscaped(string variableName)
    {
        return _escapedVariables.Contains(variableName);
    }

    /// <summary>
    /// Records that a global/outer-scope variable might alias a local variable.
    /// Called when we detect an assignment like `globalVar = localVar` inside a function.
    /// </summary>
    public void RecordGlobalAlias(string globalVar, string localVar)
    {
        if (!_globalAliases.TryGetValue(globalVar, out var aliases))
        {
            aliases = new HashSet<string>();
            _globalAliases[globalVar] = aliases;
        }
        aliases.Add(localVar);
    }

    /// <summary>
    /// Gets all local variables that a global variable might be aliasing.
    /// </summary>
    public IEnumerable<string> GetAliasedLocals(string globalVar)
    {
        if (_globalAliases.TryGetValue(globalVar, out var aliases))
        {
            return aliases;
        }
        return [];
    }

    /// <summary>
    /// Gets all escaped variables that might be affected by a mutation on the given variable.
    /// </summary>
    /// <remarks>
    /// When a property is mutated on `mutatedVar`, we need to invalidate narrowings
    /// on any escaped variable that `mutatedVar` might be aliasing.
    ///
    /// For a conservative approach: if `mutatedVar` is a global and any escaped
    /// variable was passed to a function, the global might alias any of them.
    /// </remarks>
    public IEnumerable<string> GetPotentiallyAffectedEscapedVariables(string mutatedVar)
    {
        // If mutatedVar is a known global that aliases specific locals, return those
        if (_globalAliases.TryGetValue(mutatedVar, out var directAliases))
        {
            foreach (var alias in directAliases)
            {
                if (_escapedVariables.Contains(alias))
                {
                    yield return alias;
                }
            }
        }

        // Conservative: if mutatedVar is not a local (might be a global),
        // and we have escaped variables, consider them potentially affected
        if (!IsLocalVariable(mutatedVar))
        {
            foreach (var escaped in _escapedVariables)
            {
                yield return escaped;
            }
        }
    }

    /// <summary>
    /// Checks if a variable is defined in the current local scope.
    /// </summary>
    private bool IsLocalVariable(string variableName)
    {
        foreach (var scopeVars in _scopeVariables)
        {
            if (scopeVars.Contains(variableName))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Enters a new scope (e.g., function body).
    /// </summary>
    public void EnterScope()
    {
        _scopeDepth++;
        _scopeVariables.Push(new HashSet<string>());
    }

    /// <summary>
    /// Exits the current scope, clearing local variables defined in it.
    /// </summary>
    public void ExitScope()
    {
        if (_scopeDepth > 0)
        {
            _scopeDepth--;
            if (_scopeVariables.Count > 0)
            {
                var scopeVars = _scopeVariables.Pop();
                // Clear escape status for variables going out of scope
                foreach (var v in scopeVars)
                {
                    _escapedVariables.Remove(v);
                }
            }
        }
    }

    /// <summary>
    /// Registers a variable as being defined in the current scope.
    /// </summary>
    public void DefineVariable(string variableName)
    {
        if (_scopeVariables.Count > 0)
        {
            _scopeVariables.Peek().Add(variableName);
        }
    }

    /// <summary>
    /// Clears all tracking state. Called between top-level statements or modules.
    /// </summary>
    public void Clear()
    {
        _escapedVariables.Clear();
        _globalAliases.Clear();
        _scopeVariables.Clear();
        _scopeDepth = 0;
    }
}

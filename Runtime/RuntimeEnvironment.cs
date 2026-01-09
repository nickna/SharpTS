using SharpTS.Parsing;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime;

/// <summary>
/// Manages variable scopes during runtime interpretation.
/// </summary>
/// <remarks>
/// Implements a linked list of scopes via the <see cref="Enclosing"/> property.
/// Each scope holds variable bindings in a dictionary. Variable lookup (Get) and
/// assignment (Assign) walk up the scope chain until found. Used by <see cref="Interpreter"/>
/// for lexical scoping and by <see cref="SharpTSFunction"/> for closures.
/// </remarks>
/// <seealso cref="TypeEnvironment"/>
public class RuntimeEnvironment(RuntimeEnvironment? enclosing = null)
{
    private readonly Dictionary<string, object?> _values = [];
    private readonly Dictionary<string, SharpTSNamespace> _namespaces = [];
    public RuntimeEnvironment? Enclosing { get; } = enclosing;

    public void Define(string name, object? value)
    {
        _values[name] = value;
    }

    public object? Get(Token name)
    {
        if (_values.TryGetValue(name.Lexeme, out object? value))
        {
            return value;
        }

        if (Enclosing != null) return Enclosing.Get(name);

        throw new Exception($"Undefined variable '{name.Lexeme}'.");
    }

    public void Assign(Token name, object? value)
    {
        if (_values.ContainsKey(name.Lexeme))
        {
            _values[name.Lexeme] = value;
            return;
        }

        if (Enclosing != null)
        {
            Enclosing.Assign(name, value);
            return;
        }

        throw new Exception($"Undefined variable '{name.Lexeme}'.");
    }

    /// <summary>
    /// Gets a variable value at a specific scope distance.
    /// </summary>
    public object? GetAt(int distance, string name)
    {
        return Ancestor(distance)._values.GetValueOrDefault(name);
    }

    /// <summary>
    /// Assigns a variable at a specific scope distance.
    /// </summary>
    public void AssignAt(int distance, Token name, object? value)
    {
        Ancestor(distance)._values[name.Lexeme] = value;
    }

    /// <summary>
    /// Traverses up the scope chain a specific number of steps.
    /// </summary>
    private RuntimeEnvironment Ancestor(int distance)
    {
        RuntimeEnvironment environment = this;
        for (int i = 0; i < distance; i++)
        {
            environment = environment.Enclosing!; 
        }
        return environment;
    }

    /// <summary>
    /// Defines or merges a namespace in the current scope.
    /// If a namespace with the same name already exists, merges the members.
    /// </summary>
    public void DefineNamespace(string name, SharpTSNamespace ns)
    {
        if (_namespaces.TryGetValue(name, out var existing))
        {
            // Merge: combine members from both namespace declarations
            existing.Merge(ns);
        }
        else
        {
            _namespaces[name] = ns;
            // Also define in values so it can be looked up as a variable
            _values[name] = ns;
        }
    }

    /// <summary>
    /// Gets a namespace by name, searching up the scope chain.
    /// </summary>
    public SharpTSNamespace? GetNamespace(string name)
    {
        if (_namespaces.TryGetValue(name, out var ns))
            return ns;
        return Enclosing?.GetNamespace(name);
    }
}

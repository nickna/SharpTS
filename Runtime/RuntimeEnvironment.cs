using SharpTS.Parsing;

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
}

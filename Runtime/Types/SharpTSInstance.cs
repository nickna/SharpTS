using SharpTS.Parsing;
using SharpTS.Execution;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of an instantiated class object.
/// </summary>
/// <remarks>
/// Created when a <see cref="SharpTSClass"/> is invoked (e.g., <c>new MyClass()</c>).
/// Stores instance field values in a dictionary. Property access delegates to the
/// class for method lookups, getters, and setters. Uses <see cref="Token"/> for
/// property names to enable precise error reporting with source location.
/// </remarks>
/// <seealso cref="SharpTSClass"/>
/// <seealso cref="SharpTSObject"/>
public class SharpTSInstance(SharpTSClass klass)
{
    private readonly SharpTSClass _klass = klass;
    private readonly Dictionary<string, object?> _fields = [];
    private Interpreter? _interpreter;

    public void SetInterpreter(Interpreter interpreter) => _interpreter = interpreter;

    public object? Get(Token name)
    {
        // Check for getter first
        SharpTSFunction? getter = _klass.FindGetter(name.Lexeme);
        if (getter != null && _interpreter != null)
        {
            return getter.Bind(this).Call(_interpreter, []);
        }

        if (_fields.TryGetValue(name.Lexeme, out object? value))
        {
            return value;
        }

        SharpTSFunction? method = _klass.FindMethod(name.Lexeme);
        if (method != null) return method.Bind(this);

        throw new Exception($"Undefined property '{name.Lexeme}'.");
    }

    public void Set(Token name, object? value)
    {
        // Check for setter first
        SharpTSFunction? setter = _klass.FindSetter(name.Lexeme);
        if (setter != null && _interpreter != null)
        {
            setter.Bind(this).Call(_interpreter, [value]);
            return;
        }

        _fields[name.Lexeme] = value;
    }

    public bool HasProperty(string name)
    {
        if (_fields.ContainsKey(name)) return true;
        if (_klass.HasGetter(name)) return true;
        return _klass.FindMethod(name) != null;
    }

    public SharpTSClass GetClass() => _klass;

    /// <summary>
    /// Get all field names for Object.keys() support
    /// </summary>
    public IEnumerable<string> GetFieldNames() => _fields.Keys;

    /// <summary>
    /// Get a field value by name for object rest patterns
    /// </summary>
    public object? GetFieldValue(string name) => _fields.TryGetValue(name, out var value) ? value : null;

    public override string ToString() => _klass.Name + " instance";
}

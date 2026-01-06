using SharpTS.Runtime.Types;
using SharpTS.Execution;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Callable wrapper for native C# implementations of built-in methods.
/// </summary>
/// <remarks>
/// Implements <see cref="ISharpTSCallable"/> to provide a uniform calling convention for
/// built-in methods (array methods like push/pop/map, string methods, Math functions).
/// Supports variable arity via min/max argument counts. The <see cref="Bind"/> method
/// associates a receiver object (e.g., the array instance for array methods).
/// Used by <see cref="Interpreter"/> when resolving method calls on built-in types.
/// </remarks>
/// <seealso cref="ISharpTSCallable"/>
/// <seealso cref="MathBuiltIns"/>
public class BuiltInMethod : ISharpTSCallable
{
    private readonly string _name;
    private readonly int _minArity;
    private readonly int _maxArity;
    private readonly Func<Interpreter, object?, List<object?>, object?> _implementation;
    private object? _receiver;

    /// <summary>
    /// The minimum number of arguments this method accepts.
    /// </summary>
    public int MinArity => _minArity;

    /// <summary>
    /// The maximum number of arguments this method accepts.
    /// </summary>
    public int MaxArity => _maxArity;

    public BuiltInMethod(string name, int arity, Func<Interpreter, object?, List<object?>, object?> implementation)
        : this(name, arity, arity, implementation) { }

    public BuiltInMethod(string name, int minArity, int maxArity, Func<Interpreter, object?, List<object?>, object?> implementation)
    {
        _name = name;
        _minArity = minArity;
        _maxArity = maxArity;
        _implementation = implementation;
    }

    public int Arity() => _minArity;

    public BuiltInMethod Bind(object? receiver)
    {
        return new BuiltInMethod(_name, _minArity, _maxArity, _implementation) { _receiver = receiver };
    }

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        if (arguments.Count < _minArity || arguments.Count > _maxArity)
        {
            throw new Exception($"Runtime Error: '{_name}' expects {_minArity}-{_maxArity} arguments but got {arguments.Count}.");
        }
        return _implementation(interpreter, _receiver, arguments);
    }

    public override string ToString() => $"<built-in {_name}>";
}

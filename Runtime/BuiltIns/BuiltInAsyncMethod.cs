using SharpTS.Execution;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Callable wrapper for native C# async implementations of built-in methods.
/// Used by Promise methods that must return Promises.
/// </summary>
public class BuiltInAsyncMethod : ISharpTSCallable, ISharpTSAsyncCallable
{
    private readonly string _name;
    private readonly int _minArity;
    private readonly int _maxArity;
    private readonly Func<Interpreter, object?, List<object?>, Task<object?>> _implementation;
    private object? _receiver;

    public BuiltInAsyncMethod(
        string name,
        int arity,
        Func<Interpreter, object?, List<object?>, Task<object?>> implementation)
        : this(name, arity, arity, implementation) { }

    public BuiltInAsyncMethod(
        string name,
        int minArity,
        int maxArity,
        Func<Interpreter, object?, List<object?>, Task<object?>> implementation)
    {
        _name = name;
        _minArity = minArity;
        _maxArity = maxArity;
        _implementation = implementation;
    }

    public int Arity() => _minArity;

    public BuiltInAsyncMethod Bind(object? receiver)
    {
        return new BuiltInAsyncMethod(_name, _minArity, _maxArity, _implementation)
        {
            _receiver = receiver
        };
    }

    /// <summary>
    /// Synchronous call - returns a Promise that wraps the async execution.
    /// </summary>
    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        ValidateArguments(arguments);
        var task = _implementation(interpreter, _receiver, arguments);
        return new SharpTSPromise(task);
    }

    /// <summary>
    /// Async call - awaits the implementation directly.
    /// </summary>
    public async Task<object?> CallAsync(Interpreter interpreter, List<object?> arguments)
    {
        ValidateArguments(arguments);
        return await _implementation(interpreter, _receiver, arguments);
    }

    private void ValidateArguments(List<object?> arguments)
    {
        if (arguments.Count < _minArity || arguments.Count > _maxArity)
        {
            throw new Exception(
                $"Runtime Error: '{_name}' expects {_minArity}-{_maxArity} arguments but got {arguments.Count}.");
        }
    }

    public override string ToString() => $"<built-in async {_name}>";
}

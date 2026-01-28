using SharpTS.Execution;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Wrapper for deprecated functions that logs a warning on first invocation.
/// Used by util.deprecate() to mark functions as deprecated.
/// </summary>
public class SharpTSDeprecatedFunction : ISharpTSCallable
{
    private readonly ISharpTSCallable _wrapped;
    private readonly string _message;
    private bool _warned;

    /// <summary>
    /// Creates a deprecated function wrapper.
    /// </summary>
    /// <param name="fn">The function to wrap.</param>
    /// <param name="message">The deprecation warning message.</param>
    public SharpTSDeprecatedFunction(ISharpTSCallable fn, string message)
    {
        _wrapped = fn ?? throw new ArgumentNullException(nameof(fn));
        _message = message ?? "";
        _warned = false;
    }

    /// <summary>
    /// Creates a deprecated function wrapper from a BuiltInMethod.
    /// </summary>
    public SharpTSDeprecatedFunction(BuiltInMethod method, string message)
        : this((ISharpTSCallable)method, message)
    {
    }

    public int Arity() => _wrapped.Arity();

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        if (!_warned)
        {
            _warned = true;
            Console.Error.WriteLine($"DeprecationWarning: {_message}");
        }
        return _wrapped.Call(interpreter, arguments);
    }

    public override string ToString() => $"<deprecated fn>";
}

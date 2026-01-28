using SharpTS.Execution;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Wrapper for functions that have been converted to callback-style by util.callbackify().
/// Calls the original function and passes the result (or error) to a callback.
/// </summary>
public class SharpTSCallbackifiedFunction : ISharpTSCallable
{
    private readonly ISharpTSCallable _wrapped;

    /// <summary>
    /// Creates a callbackified function wrapper.
    /// </summary>
    /// <param name="fn">The synchronous function to wrap.</param>
    public SharpTSCallbackifiedFunction(ISharpTSCallable fn)
    {
        _wrapped = fn ?? throw new ArgumentNullException(nameof(fn));
    }

    /// <summary>
    /// Creates a callbackified function wrapper from a BuiltInMethod.
    /// </summary>
    public SharpTSCallbackifiedFunction(BuiltInMethod method)
        : this((ISharpTSCallable)method)
    {
    }

    // Callbackified functions need one more argument than the original (the callback)
    public int Arity() => _wrapped.Arity() + 1;

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        if (arguments.Count == 0)
            throw new Exception("callbackified function requires at least a callback argument");

        // The callback is the last argument
        var callback = arguments[^1] as ISharpTSCallable
            ?? throw new Exception("Last argument to callbackified function must be a callback");

        // Get arguments for the original function (all except last)
        var originalArgs = arguments.Take(arguments.Count - 1).ToList();

        try
        {
            // Call the original function
            var result = _wrapped.Call(interpreter, originalArgs);

            // Call callback with (null, result)
            callback.Call(interpreter, [null, result]);
        }
        catch (Exception ex)
        {
            // Call callback with (error, null)
            var error = new SharpTSError(ex.Message);
            callback.Call(interpreter, [error, null]);
        }

        return null;
    }

    public override string ToString() => $"<callbackified fn>";
}

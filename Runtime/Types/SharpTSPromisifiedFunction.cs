using SharpTS.Execution;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Wrapper for functions that have been converted to Promise-style by util.promisify().
/// Takes a callback-style function (last arg is (err, value) => void) and returns a Promise.
/// </summary>
public class SharpTSPromisifiedFunction : ISharpTSCallable
{
    private readonly ISharpTSCallable _wrapped;

    /// <summary>
    /// Creates a promisified function wrapper.
    /// </summary>
    /// <param name="fn">The callback-style function to wrap.</param>
    public SharpTSPromisifiedFunction(ISharpTSCallable fn)
    {
        _wrapped = fn ?? throw new ArgumentNullException(nameof(fn));
    }

    /// <summary>
    /// Creates a promisified function wrapper from a BuiltInMethod.
    /// </summary>
    public SharpTSPromisifiedFunction(BuiltInMethod method)
        : this((ISharpTSCallable)method)
    {
    }

    // Promisified functions need one less argument than the original (no callback needed)
    public int Arity() => Math.Max(0, _wrapped.Arity() - 1);

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        var tcs = new TaskCompletionSource<object?>();

        // Create a callback function that will resolve or reject the promise
        var callback = new SharpTSPromisifyCallback(tcs);

        // Add the callback as the last argument
        var argsWithCallback = arguments.ToList();
        argsWithCallback.Add(callback);

        try
        {
            // Call the original function with the callback
            _wrapped.Call(interpreter, argsWithCallback);
        }
        catch (Exception ex)
        {
            // If the function throws synchronously, reject the promise
            tcs.TrySetException(new SharpTSPromiseRejectedException(new SharpTSError(ex.Message)));
        }

        return new SharpTSPromise(tcs.Task);
    }

    public override string ToString() => "<promisified fn>";
}

/// <summary>
/// Internal callback function used by promisified functions.
/// Called with (err, value) - resolves or rejects the promise accordingly.
/// </summary>
internal class SharpTSPromisifyCallback : ISharpTSCallable
{
    private readonly TaskCompletionSource<object?> _tcs;

    public SharpTSPromisifyCallback(TaskCompletionSource<object?> tcs)
    {
        _tcs = tcs;
    }

    public int Arity() => 2;

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        var err = arguments.Count > 0 ? arguments[0] : null;
        var value = arguments.Count > 1 ? arguments[1] : null;

        // Check if err is truthy (non-null, non-false, non-empty string, non-zero)
        bool hasError = err switch
        {
            null => false,
            false => false,
            "" => false,
            0.0 => false,
            _ => true
        };

        if (hasError)
        {
            // Reject with the error
            var error = err is SharpTSError e ? e : new SharpTSError(err?.ToString() ?? "Unknown error");
            _tcs.TrySetException(new SharpTSPromiseRejectedException(error));
        }
        else
        {
            // Resolve with the value
            _tcs.TrySetResult(value);
        }

        return null;
    }

    public override string ToString() => "<promisify callback>";
}

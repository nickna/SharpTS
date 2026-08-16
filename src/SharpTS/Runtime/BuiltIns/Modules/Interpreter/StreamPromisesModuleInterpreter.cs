using SharpTS.Runtime.Types;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

/// <summary>
/// Interpreter-mode implementation of the Node.js 'stream/promises' module.
/// Provides promise-based versions of pipeline and finished.
/// </summary>
public static class StreamPromisesModuleInterpreter
{
    /// <summary>
    /// Gets all exported values for the stream/promises module.
    /// </summary>
    public static Dictionary<string, object?> GetExports()
    {
        return new Dictionary<string, object?>
        {
            ["pipeline"] = new BuiltInAsyncMethod("pipeline", 2, int.MaxValue, PipelineAsync),
            ["finished"] = new BuiltInAsyncMethod("finished", 1, 3, FinishedAsync)
        };
    }

    /// <summary>
    /// Creates a namespace object for stream.promises property access.
    /// </summary>
    public static SharpTSObject CreatePromisesNamespace()
    {
        return new SharpTSObject(GetExports());
    }

    /// <summary>
    /// Promise-based pipeline: resolves on success, rejects on error.
    /// </summary>
    private static Task<object?> PipelineAsync(Interp interpreter, object? receiver, List<object?> args)
    {
        var tcs = new TaskCompletionSource<object?>();

        // Add a callback at the end that resolves/rejects the promise
        var argsWithCallback = new List<object?>(args);
        argsWithCallback.Add(new StreamModuleInterpreter.FinishedListener((interp, cbArgs) =>
        {
            var error = cbArgs.Count > 0 ? cbArgs[0] : null;
            if (error != null)
                tcs.TrySetException(new Exception(error.ToString()));
            else
                tcs.TrySetResult(SharpTSUndefined.Instance);
        }));

        try
        {
            StreamModuleInterpreter.PipelineInternal(interpreter, null, argsWithCallback);
        }
        catch (Exception ex)
        {
            tcs.TrySetException(ex);
        }

        return tcs.Task;
    }

    /// <summary>
    /// Promise-based finished: resolves when stream ends, rejects on error.
    /// </summary>
    private static Task<object?> FinishedAsync(Interp interpreter, object? receiver, List<object?> args)
    {
        var tcs = new TaskCompletionSource<object?>();

        // Build args with a callback that resolves/rejects
        var finishedArgs = new List<object?>();
        if (args.Count >= 1) finishedArgs.Add(args[0]); // stream

        // If there's an options object, include it
        if (args.Count >= 2 && args[1] is SharpTSObject)
            finishedArgs.Add(args[1]);

        finishedArgs.Add(new StreamModuleInterpreter.FinishedListener((interp, cbArgs) =>
        {
            var error = cbArgs.Count > 0 ? cbArgs[0] : null;
            if (error != null)
                tcs.TrySetException(new Exception(error.ToString()));
            else
                tcs.TrySetResult(SharpTSUndefined.Instance);
        }));

        try
        {
            StreamModuleInterpreter.FinishedInternal(interpreter, null, finishedArgs);
        }
        catch (Exception ex)
        {
            tcs.TrySetException(ex);
        }

        return tcs.Task;
    }
}

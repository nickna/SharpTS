using SharpTS.Runtime.Types;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

/// <summary>
/// Interpreter-mode implementation of <c>primitive:timers</c> and
/// <c>primitive:timers/promises</c>. The user-facing <c>timers</c> and
/// <c>timers/promises</c> modules live in <c>stdlib/node/timers.ts</c> and
/// <c>stdlib/node/timers/promises.ts</c>; both import from these primitives.
/// GetExports() returns the callback API; GetPromisesExports() returns the
/// promise-based API.
/// </summary>
public static class TimersPrimitiveInterpreter
{
    /// <summary>
    /// Gets exported values for the timers/promises module.
    /// </summary>
    public static Dictionary<string, object?> GetPromisesExports()
    {
        return new Dictionary<string, object?>
        {
            ["setTimeout"] = new BuiltInAsyncMethod("setTimeout", 0, 3, SetTimeoutPromise),
            ["setImmediate"] = new BuiltInAsyncMethod("setImmediate", 0, 2, SetImmediatePromise),
            ["setInterval"] = new BuiltInMethod("setInterval", 0, 3, SetIntervalIterable)
        };
    }

    private const string AbortMessage = "AbortError: The operation was aborted";

    /// <summary>
    /// Throws a JavaScript-style AbortError. Wraps in SharpTSPromiseRejectedException so
    /// the catch handler receives a SharpTSError with a .message property (matching compiled mode).
    /// </summary>
    private static Exception NewAbortError() =>
        new SharpTSPromiseRejectedException(new SharpTSError(AbortMessage));

    /// <summary>
    /// Extracts a CancellationToken from the options argument if it has a signal.
    /// Returns null if no signal is present. Throws AbortError if signal is already aborted.
    /// </summary>
    private static CancellationToken? ExtractSignalToken(object? optionsArg)
    {
        if (optionsArg == null) return null;

        SharpTSAbortSignal? signal = null;
        if (optionsArg is SharpTSObject options &&
            options.Fields.TryGetValue("signal", out var signalObj) &&
            signalObj is SharpTSAbortSignal s)
        {
            signal = s;
        }
        // Also accept a bare SharpTSAbortSignal as the options arg (legacy/convenience)
        else if (optionsArg is SharpTSAbortSignal directSignal)
        {
            signal = directSignal;
        }

        if (signal == null) return null;
        if (signal.Aborted) throw NewAbortError();
        return signal.Token;
    }

    /// <summary>
    /// Promise-based setTimeout: resolves with value after delay milliseconds.
    /// Supports options.signal for AbortSignal cancellation.
    /// Signature: setTimeout(delay?, value?, options?)
    /// </summary>
    private static async Task<object?> SetTimeoutPromise(Interp interpreter, object? receiver, List<object?> args)
    {
        double delay = args.Count > 0 && args[0] is double d ? d : 0;
        object? value = args.Count > 1 ? args[1] : SharpTSUndefined.Instance;
        var token = ExtractSignalToken(args.Count > 2 ? args[2] : null) ?? CancellationToken.None;

        int delayMs = Math.Max(0, (int)delay);

        // A bare Task.Delay holds no handle, so the event loop's quiescence
        // check can't see it; Ref keeps the pending delay counted as live work
        // or a top-level promise wait gives up mid-delay (same as fetch()).
        interpreter.Ref();
        try
        {
            await Task.Delay(delayMs, token);
        }
        catch (OperationCanceledException)
        {
            throw NewAbortError();
        }
        finally
        {
            interpreter.Unref();
        }

        return value;
    }

    /// <summary>
    /// Promise-based setImmediate: resolves with value on the next event loop tick.
    /// Supports options.signal for AbortSignal cancellation.
    /// </summary>
    private static async Task<object?> SetImmediatePromise(Interp interpreter, object? receiver, List<object?> args)
    {
        object? value = args.Count > 0 ? args[0] : SharpTSUndefined.Instance;
        var token = ExtractSignalToken(args.Count > 1 ? args[1] : null) ?? CancellationToken.None;

        interpreter.Ref();
        try
        {
            await Task.Delay(0, token);
        }
        catch (OperationCanceledException)
        {
            throw NewAbortError();
        }
        finally
        {
            interpreter.Unref();
        }

        return value;
    }

    /// <summary>
    /// Promise-based setInterval: returns an AsyncIterable that yields value on each interval.
    /// Supports options.signal for AbortSignal cancellation. Pre-aborted signal throws synchronously.
    /// </summary>
    private static object? SetIntervalIterable(Interp interpreter, object? receiver, List<object?> args)
    {
        double delay = args.Count > 0 && args[0] is double d ? d : 0;
        object? value = args.Count > 1 ? args[1] : SharpTSUndefined.Instance;
        int delayMs = Math.Max(0, (int)delay);

        var token = ExtractSignalToken(args.Count > 2 ? args[2] : null);

        return new SharpTSAsyncIntervalIterator(delayMs, value, token);
    }

    /// <summary>
    /// Gets all exported values for the timers module.
    /// </summary>
    public static Dictionary<string, object?> GetExports()
    {
        return new Dictionary<string, object?>
        {
            ["setTimeout"] = new BuiltInMethod("setTimeout", 1, int.MaxValue, SetTimeout),
            ["clearTimeout"] = new BuiltInMethod("clearTimeout", 0, 1, ClearTimeout),
            ["setInterval"] = new BuiltInMethod("setInterval", 1, int.MaxValue, SetInterval),
            ["clearInterval"] = new BuiltInMethod("clearInterval", 0, 1, ClearInterval),
            ["setImmediate"] = new BuiltInMethod("setImmediate", 1, int.MaxValue, SetImmediate),
            ["clearImmediate"] = new BuiltInMethod("clearImmediate", 0, 1, ClearImmediate)
        };
    }

    /// <summary>
    /// setTimeout(callback, delay?, ...args) - schedules callback after delay milliseconds.
    /// </summary>
    private static object? SetTimeout(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0)
            throw new Exception("Runtime Error: setTimeout requires at least 1 argument");

        var callback = args[0] as ISharpTSCallable
            ?? throw new Exception("Runtime Error: setTimeout callback must be a function");

        double delayMs = 0;
        if (args.Count > 1 && args[1] is double d)
            delayMs = d;

        var callbackArgs = args.Count > 2
            ? args.Skip(2).ToList()
            : new List<object?>();

        return TimerBuiltIns.SetTimeout(interpreter, callback, delayMs, callbackArgs);
    }

    /// <summary>
    /// clearTimeout(handle?) - cancels a pending timeout.
    /// </summary>
    private static object? ClearTimeout(Interp interpreter, object? receiver, List<object?> args)
    {
        object? handle = args.Count > 0 ? args[0] : null;
        TimerBuiltIns.ClearTimeout(handle);
        return null;
    }

    /// <summary>
    /// setInterval(callback, delay?, ...args) - schedules callback to repeat every delay milliseconds.
    /// </summary>
    private static object? SetInterval(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0)
            throw new Exception("Runtime Error: setInterval requires at least 1 argument");

        var callback = args[0] as ISharpTSCallable
            ?? throw new Exception("Runtime Error: setInterval callback must be a function");

        double delayMs = 0;
        if (args.Count > 1 && args[1] is double d)
            delayMs = d;

        var callbackArgs = args.Count > 2
            ? args.Skip(2).ToList()
            : new List<object?>();

        return TimerBuiltIns.SetInterval(interpreter, callback, delayMs, callbackArgs);
    }

    /// <summary>
    /// clearInterval(handle?) - cancels a pending interval.
    /// </summary>
    private static object? ClearInterval(Interp interpreter, object? receiver, List<object?> args)
    {
        object? handle = args.Count > 0 ? args[0] : null;
        TimerBuiltIns.ClearInterval(handle);
        return null;
    }

    /// <summary>
    /// setImmediate(callback, ...args) - schedules callback to run in the next iteration of the event loop.
    /// </summary>
    /// <remarks>
    /// Implemented as setTimeout(callback, 0, ...args) since the interpreter doesn't have
    /// a separate immediate queue.
    /// </remarks>
    private static object? SetImmediate(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0)
            throw new Exception("Runtime Error: setImmediate requires at least 1 argument");

        var callback = args[0] as ISharpTSCallable
            ?? throw new Exception("Runtime Error: setImmediate callback must be a function");

        var callbackArgs = args.Count > 1
            ? args.Skip(1).ToList()
            : new List<object?>();

        // setImmediate is essentially setTimeout with 0 delay
        return TimerBuiltIns.SetTimeout(interpreter, callback, 0, callbackArgs);
    }

    /// <summary>
    /// clearImmediate(handle?) - cancels a pending immediate.
    /// </summary>
    private static object? ClearImmediate(Interp interpreter, object? receiver, List<object?> args)
    {
        // clearImmediate uses the same mechanism as clearTimeout
        object? handle = args.Count > 0 ? args[0] : null;
        TimerBuiltIns.ClearTimeout(handle);
        return null;
    }
}

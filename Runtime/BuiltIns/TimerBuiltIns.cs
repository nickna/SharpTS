using SharpTS.Execution;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Provides runtime implementation for timer functions (setTimeout, clearTimeout).
/// Uses System.Threading.Timer for reliable timer scheduling that doesn't compete
/// with the general thread pool.
/// </summary>
public static class TimerBuiltIns
{
    /// <summary>
    /// Executes setTimeout: schedules a callback to run after a delay.
    /// Uses System.Threading.Timer for reliable scheduling under load.
    /// </summary>
    /// <param name="interpreter">The interpreter instance for callback execution.</param>
    /// <param name="callback">The callback function to execute.</param>
    /// <param name="delayMs">The delay in milliseconds (defaults to 0).</param>
    /// <param name="args">Additional arguments to pass to the callback.</param>
    /// <returns>A SharpTSTimeout handle that can be used with clearTimeout.</returns>
    public static SharpTSTimeout SetTimeout(Interpreter interpreter, ISharpTSCallable callback, double delayMs, List<object?> args)
    {
        var cts = new CancellationTokenSource();
        var timeout = new SharpTSTimeout(cts);

        // Ensure delay is non-negative
        int delay = Math.Max(0, (int)delayMs);

        // Use System.Threading.Timer for reliable timer scheduling
        Timer? timer = null;
        timer = new Timer(_ =>
        {
            // Check both cancellation and interpreter disposal before queueing
            // This prevents race conditions where callbacks fire after test cleanup
            if (!cts.IsCancellationRequested && !interpreter.IsDisposed)
            {
                // Queue the callback for execution on the main thread during loop iterations.
                // This avoids thread scheduling issues on macOS where background threads
                // may not get CPU time during tight loops.
                interpreter.EnqueueCallback(() =>
                {
                    if (!cts.IsCancellationRequested && !interpreter.IsDisposed)
                    {
                        callback.Call(interpreter, args);
                    }
                });
            }
            timer?.Dispose();
        }, null, delay, Timeout.Infinite);

        timeout.Timer = timer;

        // Register timer with interpreter for cleanup on disposal
        interpreter.RegisterTimer(timeout);

        return timeout;
    }

    /// <summary>
    /// Cancels a pending timeout.
    /// Safe to call with null/undefined - follows Node.js behavior.
    /// </summary>
    /// <param name="handle">The timeout handle to cancel (can be null).</param>
    public static void ClearTimeout(object? handle)
    {
        if (handle is SharpTSTimeout timeout)
        {
            timeout.Cancel();
        }
        // If handle is null/undefined/wrong type, silently ignore (Node.js behavior)
    }

    /// <summary>
    /// Executes setInterval: schedules callback to run repeatedly after each delay.
    /// Uses System.Threading.Timer for reliable scheduling under load.
    /// </summary>
    /// <param name="interpreter">The interpreter instance for callback execution.</param>
    /// <param name="callback">The callback function to execute.</param>
    /// <param name="delayMs">The delay in milliseconds between executions.</param>
    /// <param name="args">Additional arguments to pass to the callback.</param>
    /// <returns>A SharpTSTimeout handle that can be used with clearInterval.</returns>
    public static SharpTSTimeout SetInterval(Interpreter interpreter, ISharpTSCallable callback, double delayMs, List<object?> args)
    {
        var cts = new CancellationTokenSource();
        var interval = new SharpTSTimeout(cts);
        int delay = Math.Max(0, (int)delayMs);

        // Use System.Threading.Timer with periodic callback
        Timer? timer = null;
        timer = new Timer(_ =>
        {
            // Check both cancellation and interpreter disposal before queueing
            // This prevents race conditions where callbacks fire after test cleanup
            if (!cts.IsCancellationRequested && !interpreter.IsDisposed)
            {
                // Queue the callback for execution on the main thread during loop iterations.
                // This avoids thread scheduling issues on macOS where background threads
                // may not get CPU time during tight loops.
                interpreter.EnqueueCallback(() =>
                {
                    if (!cts.IsCancellationRequested && !interpreter.IsDisposed)
                    {
                        callback.Call(interpreter, args);
                    }
                });
            }
        }, null, delay, delay);

        interval.Timer = timer;

        // Register timer with interpreter for cleanup on disposal
        interpreter.RegisterTimer(interval);

        return interval;
    }

    /// <summary>
    /// Cancels a pending interval.
    /// Safe to call with null/undefined - follows Node.js behavior.
    /// </summary>
    /// <param name="handle">The interval handle to cancel (can be null).</param>
    public static void ClearInterval(object? handle)
    {
        if (handle is SharpTSTimeout interval)
        {
            interval.Cancel();
        }
        // If handle is null/undefined/wrong type, silently ignore (Node.js behavior)
    }

    /// <summary>
    /// Gets a member (method or property) from a SharpTSTimeout instance.
    /// </summary>
    /// <param name="timeout">The timeout instance.</param>
    /// <param name="name">The member name.</param>
    /// <returns>The member value or method.</returns>
    public static object? GetMember(SharpTSTimeout timeout, string name)
    {
        return name switch
        {
            "ref" => new BuiltInMethod("ref", 0, 0, (_, receiver, _) =>
            {
                if (receiver is SharpTSTimeout t)
                    return t.Ref();
                return receiver;
            }),
            "unref" => new BuiltInMethod("unref", 0, 0, (_, receiver, _) =>
            {
                if (receiver is SharpTSTimeout t)
                    return t.Unref();
                return receiver;
            }),
            "hasRef" => timeout.HasRef,
            "toString" => new BuiltInMethod("toString", 0, 0, (_, receiver, _) =>
            {
                if (receiver is SharpTSTimeout t)
                    return t.ToString();
                return receiver?.ToString() ?? "null";
            }),
            _ => null
        };
    }
}
